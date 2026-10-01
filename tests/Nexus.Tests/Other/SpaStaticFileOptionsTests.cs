// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Moq;
using Nexus.Core;
using Xunit;

namespace Other;

public class SpaStaticFileOptionsTests
{
    [Theory]
    [InlineData("index.html")]
    [InlineData("index.HTML")]
    [InlineData("other.html")]
    public void ApplyCachePolicyRequiresRevalidationForHtml(string fileName)
    {
        // Arrange
        var context = CreateContext(fileName);

        // Act
        SpaStaticFileOptions.ApplyCachePolicy(context);

        // Assert
        Assert.Equal(
            SpaStaticFileOptions.DOCUMENT_CACHE_CONTROL,
            context.Context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("main-4F3A2B.js")]
    [InlineData("styles-9C1D2E.css")]
    [InlineData("icon.svg")]
    public void ApplyCachePolicyLeavesNonHtmlUntouched(string fileName)
    {
        // Arrange
        var context = CreateContext(fileName);

        // Act
        SpaStaticFileOptions.ApplyCachePolicy(context);

        // Assert
        Assert.False(context.Context.Response.Headers.ContainsKey(HeaderNames.CacheControl));
    }

    private static StaticFileResponseContext CreateContext(string fileName)
    {
        var file = new Mock<IFileInfo>();
        file.SetupGet(fileInfo => fileInfo.Name).Returns(fileName);

        return new StaticFileResponseContext(new DefaultHttpContext(), file.Object);
    }
}
