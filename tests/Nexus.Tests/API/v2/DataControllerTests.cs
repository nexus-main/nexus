// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nexus.Controllers.V2;
using Nexus.Core.V2;
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
        var request = new BatchStreamRequest(default, default, ["/A/B"]);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var controller = CreateController(service);
        var actual = await controller.GetStreamAsync(request, CancellationToken.None);
        var result = Assert.IsType<FileStreamResult>(actual);

        Assert.Same(expected, result.FileStream);
        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public async Task ReturnsUnprocessableEntityForInvalidRequest()
    {
        var service = Mock.Of<IDataService>();
        var request = new BatchStreamRequest(default, default, []);

        Mock.Get(service)
            .Setup(current => current.ReadBatchAsStreamAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException("invalid"));

        var actual = await CreateController(service).GetStreamAsync(request, CancellationToken.None);
        var result = Assert.IsType<UnprocessableEntityObjectResult>(actual);

        Assert.Equal("invalid", result.Value);
    }

    private static DataController CreateController(IDataService service)
    {
        return new DataController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }
}
