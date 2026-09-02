// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nexus.Controllers.V2;
using Nexus.Core.V2;
using Nexus.Services;
using Xunit;

namespace API.v2;

public class DataControllerTests
{
    [Fact]
    public async Task RegisterBatchStreamRequiresHttp2()
    {
        var dataService = Mock.Of<IDataService>();
        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();
        var controller = CreateController(dataService, streamSessionManager, "HTTP/1.1");
        var request = new BatchStreamRequest(default, default, []);

        var actual = await controller.RegisterBatchStreamAsync(request, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(actual.Result);
        Assert.Equal(StatusCodes.Status426UpgradeRequired, result.StatusCode);

        Mock.Get(dataService).Verify(
            current => current.RegisterBatchStreamAsync(It.IsAny<BatchStreamRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterBatchStreamAllowsHttp2()
    {
        var expected = new BatchStreamResponse(Guid.NewGuid(), []);
        var dataService = Mock.Of<IDataService>();

        Mock.Get(dataService)
            .Setup(current => current.RegisterBatchStreamAsync(It.IsAny<BatchStreamRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();
        var controller = CreateController(dataService, streamSessionManager, "HTTP/2");
        var request = new BatchStreamRequest(default, default, []);

        var actual = await controller.RegisterBatchStreamAsync(request, CancellationToken.None);

        Assert.Equal(expected, actual.Value);
    }

    [Fact]
    public async Task GetBatchStreamChannelRequiresHttp2()
    {
        var dataService = Mock.Of<IDataService>();
        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();
        var controller = CreateController(dataService, streamSessionManager, "HTTP/1.1");

        var actual = await controller.GetBatchStreamChannelAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(actual);
        Assert.Equal(StatusCodes.Status426UpgradeRequired, result.StatusCode);

        Mock.Get(streamSessionManager).Verify(
            current => current.Attach(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>()),
            Times.Never);
    }

    [Fact]
    public void GetBatchStreamSessionStatusReturns404ForUnknownSession()
    {
        var dataService = Mock.Of<IDataService>();
        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();
        var controller = CreateController(dataService, streamSessionManager, "HTTP/1.1");

        var actual = controller.GetBatchStreamSessionStatus(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(actual.Result);
    }

    [Fact]
    public void GetBatchStreamSessionStatusDoesNotRequireHttp2()
    {
        var expected = new BatchStreamSessionStatus(
            BatchStreamSessionState.Faulted, Guid.NewGuid(), "/A/B", "boom");

        var dataService = Mock.Of<IDataService>();
        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();

        Mock.Get(streamSessionManager)
            .Setup(current => current.GetStatus(It.IsAny<Guid>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .Returns(expected);

        var controller = CreateController(dataService, streamSessionManager, "HTTP/1.1");

        var actual = controller.GetBatchStreamSessionStatus(Guid.NewGuid());

        Assert.Equal(expected, actual.Value);
    }

    [Fact]
    public void GetBatchStreamSessionStatusReturnsStatus()
    {
        var expected = new BatchStreamSessionStatus(
            BatchStreamSessionState.Completed, null, null, null);

        var dataService = Mock.Of<IDataService>();
        var streamSessionManager = Mock.Of<IDataStreamSessionManager>();

        Mock.Get(streamSessionManager)
            .Setup(current => current.GetStatus(It.IsAny<Guid>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .Returns(expected);

        var controller = CreateController(dataService, streamSessionManager, "HTTP/2");

        var actual = controller.GetBatchStreamSessionStatus(Guid.NewGuid());

        Assert.Equal(expected, actual.Value);
    }

    private static DataController CreateController(
        IDataService dataService,
        IDataStreamSessionManager streamSessionManager,
        string protocol)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Protocol = protocol;

        return new DataController(dataService, streamSessionManager)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }
}
