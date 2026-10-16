// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nexus.Controllers.V2;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Services;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace API.v2;

public class DataControllerTests
{
    [Fact]
    public async Task StreamsData()
    {
        // Arrange
        var expected = new MemoryStream([1, 2, 3]);
        var service = Mock.Of<IDataService>();
        var request = new BatchStreamRequest(default, default, ["/A/B"], Precision.Float32);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController(service);

        // Act
        var actual = await controller.GetStreamAsync(request, CancellationToken.None);

        // Assert
        var result = Assert.IsType<FileStreamResult>(actual);

        Assert.Same(expected, result.FileStream);
        Assert.Equal("application/vnd.apache.arrow.stream", result.ContentType);
    }

    [Fact]
    public async Task ReturnsUnprocessableEntityForInvalidRequest()
    {
        // Arrange
        var service = Mock.Of<IDataService>();
        var request = new BatchStreamRequest(default, default, [], Precision.Float32);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException("invalid"));

        // Act
        var actual = await CreateController(service).GetStreamAsync(request, CancellationToken.None);

        // Assert
        var result = Assert.IsType<UnprocessableEntityObjectResult>(actual);

        Assert.Equal("invalid", result.Value);
    }

    [Theory]
    [InlineData(422, "invalid")]
    [InlineData(404, "Could not find resource path /missing.")]
    [InlineData(403, "The current user is not permitted to access the catalog /private.")]
    public async Task VisualizationErrorsDoNotNegotiateArrow(int status, string message)
    {
        // Arrange
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        var error = status == 422 ? new ValidationException(message) : new Exception(message);

        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

        var controller = CreateController(Mock.Of<IDataService>());
        controller.Request.Headers.Accept = "application/vnd.apache.arrow.stream";

        // Act
        var actual = await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None);

        // Assert
        var result = Assert.IsType<ContentResult>(actual);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(message, result.Content);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task CancelledVisualizationAbortsWithoutServerError(bool duringPreparation, bool responseStarted)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var context = new DefaultHttpContext();
        var response = new Mock<IHttpResponseFeature>();

        response.SetupProperty(feature => feature.StatusCode, StatusCodes.Status200OK);
        response.SetupGet(feature => feature.Headers).Returns(new HeaderDictionary());
        response.SetupGet(feature => feature.HasStarted).Returns(responseStarted);

        context.Features.Set(response.Object);

        var lifetime = new Mock<IHttpRequestLifetimeFeature>();

        lifetime.SetupGet(feature => feature.RequestAborted).Returns(cancellation.Token);

        context.Features.Set(lifetime.Object);

        var controller = new DataController(Mock.Of<IDataService>())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();

        visualization.Setup(service => service.PrepareAsync(request, cancellation.Token))
            .Returns<VisualizationRequest, CancellationToken>((_, token) =>
            {
                if (duringPreparation)
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<Func<Stream, CancellationToken, Task>>(token);
                }

                return Task.FromResult<Func<Stream, CancellationToken, Task>>((_, streamToken) =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled(streamToken);
                });
            });

        // Act
        var actual = await controller.GetVisualizationAsync(request, visualization.Object, cancellation.Token);

        // Assert
        Assert.IsType<EmptyResult>(actual);
        Assert.Equal(responseStarted ? StatusCodes.Status200OK : StatusCodes.Status499ClientClosedRequest, controller.Response.StatusCode);
        lifetime.Verify(feature => feature.Abort(), Times.Once);

        if (responseStarted)
        {
            response.VerifySet(feature => feature.StatusCode = It.IsAny<int>(), Times.Never);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VisualizationDoesNotSwallowUnrequestedCancellation(bool duringPreparation)
    {
        // Arrange
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        var error = new OperationCanceledException();

        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>()))
            .Returns(() => duringPreparation
                ? Task.FromException<Func<Stream, CancellationToken, Task>>(error)
                : Task.FromResult<Func<Stream, CancellationToken, Task>>((_, _) => Task.FromException(error)));

        var controller = CreateController(Mock.Of<IDataService>());

        // Act
        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None));

        // Assert
        Assert.Same(error, actual);
    }

    private static DataController CreateController(IDataService service)
    {
        return new DataController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
