// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
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
        var expected = new MemoryStream([1, 2, 3]);
        var service = Mock.Of<IDataService>();
        var request = new BatchStreamRequest(default, default, ["/A/B"], Precision.Float32);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController(service);
        var actual = await controller.GetStreamAsync(request, CancellationToken.None);
        var result = Assert.IsType<FileStreamResult>(actual);

        Assert.Same(expected, result.FileStream);
        Assert.Equal("application/vnd.apache.arrow.stream", result.ContentType);
    }

    [Fact]
    public async Task ReturnsUnprocessableEntityForInvalidRequest()
    {
        var service = Mock.Of<IDataService>();
        var request = new BatchStreamRequest(default, default, [], Precision.Float32);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException("invalid"));

        var actual = await CreateController(service).GetStreamAsync(request, CancellationToken.None);
        var result = Assert.IsType<UnprocessableEntityObjectResult>(actual);

        Assert.Equal("invalid", result.Value);
    }

    [Theory]
    [InlineData(422, "invalid")]
    [InlineData(404, "Could not find resource path /missing.")]
    [InlineData(403, "The current user is not permitted to access the catalog /private.")]
    public async Task VisualizationErrorsDoNotNegotiateArrow(int status, string message)
    {
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        var error = status == 422 ? new ValidationException(message) : new Exception(message);
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);
        var controller = CreateController(Mock.Of<IDataService>());
        controller.Request.Headers.Accept = "application/vnd.apache.arrow.stream";

        var actual = await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None);
        var result = Assert.IsType<ContentResult>(actual);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(message, result.Content);
    }

    private static DataController CreateController(IDataService service)
    {
        return new DataController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
