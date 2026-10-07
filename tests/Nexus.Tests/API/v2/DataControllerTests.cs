// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Controllers.V2;
using Nexus.Core.V2;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Tests;
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
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>(), It.IsAny<VisualizationTiming?>()))
            .ThrowsAsync(error);
        var controller = CreateController(Mock.Of<IDataService>());
        controller.Request.Headers.Accept = "application/vnd.apache.arrow.stream";

        var actual = await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None);
        var result = Assert.IsType<ContentResult>(actual);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(message, result.Content);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-an-id", null)]
    [InlineData("adac95f1234648dd866f293563c57b68", null)]
    [InlineData("{adac95f1-2346-48dd-866f-293563c57b68}", null)]
    [InlineData(" adac95f1-2346-48dd-866f-293563c57b68", null)]
    [InlineData("adac95f1-2346-48dd-866f-293563c57b68 ", null)]
    [InlineData("adac95f1-2346-48dd-866f-293563c57b6z", null)]
    [InlineData("+dac95f1-2346-48dd-866f-293563c57b68", null)]
    [InlineData("adac95f1-2346-48dd-866f-293563c57b68,adac95f1-2346-48dd-866f-293563c57b68", null)]
    [InlineData("ADAC95F1-2346-48DD-866F-293563C57B68", "adac95f1-2346-48dd-866f-293563c57b68")]
    [InlineData("adac95f1-2346-48dd-866f-293563c57b68", "adac95f1-2346-48dd-866f-293563c57b68")]
    public async Task VisualizationTraceRequiresSingleCanonicalGuid(string? header, string? expected)
    {
        using var logs = new VisualizationTimingLogger();
        var controller = new DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        if (header is not null)
            controller.Request.Headers[VisualizationTiming.Header] = header;

        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        VisualizationTiming? captured = null;
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>(), It.IsAny<VisualizationTiming?>()))
            .Callback<VisualizationRequest, CancellationToken, VisualizationTiming?>((_, _, timing) => captured = timing)
            .ReturnsAsync((Stream _, CancellationToken _) =>
            {
                Assert.Equal(expected ?? "", controller.Response.Headers[VisualizationTiming.Header].ToString());
                return Task.CompletedTask;
            });

        Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None));
        Assert.Equal(expected, captured?.RequestId);
        Assert.Equal(expected ?? "", controller.Response.Headers[VisualizationTiming.Header].ToString());

        if (expected is null)
            Assert.Empty(logs.Entries);
        else
        {
            Assert.Equal(new[] { "prepare", "prepare", "request" }, logs.Entries.Select(entry => entry.Phase));
            Assert.Equal("success", logs.Entries.Last().Outcome);
            Assert.All(logs.Entries, entry =>
            {
                Assert.Equal(VisualizationTiming.Category, entry.Category);
                Assert.Equal(expected, entry.Fields["RequestId"]);
                Assert.Contains(expected, entry.Message);
            });
        }

        // Multiple header values are invalid even if each value is a valid GUID.
        controller.Request.Headers[VisualizationTiming.Header] = new Microsoft.Extensions.Primitives.StringValues([
            "adac95f1-2346-48dd-866f-293563c57b68", "adac95f1-2346-48dd-866f-293563c57b68"]);
        controller.Response.Headers.Clear();
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>(), It.IsAny<VisualizationTiming?>()))
            .Callback<VisualizationRequest, CancellationToken, VisualizationTiming?>((_, _, timing) => Assert.Null(timing))
            .ReturnsAsync((Stream _, CancellationToken _) => Task.CompletedTask);
        int count = logs.Entries.Count;
        await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None);
        Assert.False(controller.Response.Headers.ContainsKey(VisualizationTiming.Header));
        Assert.Equal(count, logs.Entries.Count);
    }

    [Theory]
    [InlineData(422, "invalid-secret", "rejected")]
    [InlineData(404, "Could not find resource path /secret.", "not-found")]
    [InlineData(403, "The current user is not permitted to access the catalog /secret.", "forbidden")]
    [InlineData(500, "unexpected-secret", "error")]
    public async Task TraceIncludesPreparationFailuresWithoutExceptionDetails(int status, string message, string outcome)
    {
        using var logs = new VisualizationTimingLogger();
        var controller = new DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        string id = Guid.NewGuid().ToString("D");
        controller.Request.Headers[VisualizationTiming.Header] = id;
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        var error = status == 422 ? new ValidationException(message) : new Exception(message);
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>(), It.IsAny<VisualizationTiming?>()))
            .ThrowsAsync(error);

        if (status == 500)
            Assert.Same(error, await Assert.ThrowsAsync<Exception>(() => controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None)));
        else
            Assert.Equal(status, Assert.IsType<ContentResult>(await controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None)).StatusCode);

        Assert.Equal(id, controller.Response.Headers[VisualizationTiming.Header]);
        var prepare = Assert.Single(logs.Entries, entry => entry.Phase == "prepare" && entry.Milestone == "end");
        Assert.Equal("error", prepare.Outcome);
        Assert.Equal(outcome, logs.Entries.Last().Outcome);
        Assert.Equal("request", logs.Entries.Last().Phase);
        Assert.All(logs.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain("secret", entry.Message);
            Assert.Equal(id, entry.Fields["RequestId"]);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task CancelledVisualizationAbortsWithoutServerError(bool duringPreparation, bool responseStarted)
    {
        using var cancellation = new CancellationTokenSource();
        using var logs = new VisualizationTimingLogger();
        var context = new DefaultHttpContext();
        var response = new Mock<IHttpResponseFeature>();
        response.SetupProperty(feature => feature.StatusCode, StatusCodes.Status200OK);
        response.SetupGet(feature => feature.Headers).Returns(new HeaderDictionary());
        response.SetupGet(feature => feature.HasStarted).Returns(responseStarted);
        context.Features.Set(response.Object);
        var lifetime = new Mock<IHttpRequestLifetimeFeature>();
        lifetime.SetupGet(feature => feature.RequestAborted).Returns(cancellation.Token);
        context.Features.Set(lifetime.Object);
        var controller = new DataController(Mock.Of<IDataService>(), logs)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        controller.Request.Headers[VisualizationTiming.Header] = Guid.NewGuid().ToString("D");
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        visualization.Setup(service => service.PrepareAsync(request, cancellation.Token, It.IsAny<VisualizationTiming?>()))
            .Returns<VisualizationRequest, CancellationToken, VisualizationTiming?>((_, token, _) =>
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

        Assert.IsType<EmptyResult>(await controller.GetVisualizationAsync(request, visualization.Object, cancellation.Token));
        Assert.Equal(responseStarted ? StatusCodes.Status200OK : StatusCodes.Status499ClientClosedRequest, controller.Response.StatusCode);
        lifetime.Verify(feature => feature.Abort(), Times.Once);
        if (responseStarted)
            response.VerifySet(feature => feature.StatusCode = It.IsAny<int>(), Times.Never);
        Assert.Equal("cancelled", logs.Entries.Last().Outcome);
        Assert.Equal("request", logs.Entries.Last().Phase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VisualizationDoesNotSwallowUnrequestedCancellation(bool duringPreparation)
    {
        var request = new VisualizationRequest(default, default, [], []);
        var visualization = new Mock<IVisualizationService>();
        var error = new OperationCanceledException();
        visualization.Setup(service => service.PrepareAsync(request, It.IsAny<CancellationToken>(), It.IsAny<VisualizationTiming?>()))
            .Returns(() => duringPreparation
                ? Task.FromException<Func<Stream, CancellationToken, Task>>(error)
                : Task.FromResult<Func<Stream, CancellationToken, Task>>((_, _) => Task.FromException(error)));
        var controller = CreateController(Mock.Of<IDataService>());

        Assert.Same(error, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            controller.GetVisualizationAsync(request, visualization.Object, CancellationToken.None)));
    }

    private static DataController CreateController(IDataService service)
    {
        return new DataController(service, NullLoggerFactory.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
