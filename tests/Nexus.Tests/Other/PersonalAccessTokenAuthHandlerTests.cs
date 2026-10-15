// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.Services;
using Xunit;

namespace Other;

public class PersonalAccessTokenAuthHandlerTests
{
    [Theory]
    [InlineData("Bearer ")]
    [InlineData("Bearer malformed")]
    [InlineData("Bearer malformedtoken")]
    public async Task MalformedBearerTokenFailsAuthentication(string authorization)
    {
        // Arrange
        var handler = CreateHandler(authorization);

        // Act
        var result = await handler.AuthenticateInternalAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task UnknownBearerTokenIsNotAuthenticated()
    {
        // Arrange
        var handler = CreateHandler("Bearer secret_userId");

        // Act
        var result = await handler.AuthenticateInternalAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(AuthenticateResult.NoResult(), result);
    }

    private static TestablePersonalAccessTokenAuthHandler CreateHandler(string? authorization)
    {
        var tokenService = new Mock<ITokenService>();

        tokenService
            .Setup(service => service.TryGet(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<Nexus.Core.InternalPersonalAccessToken?>.IsAny))
            .Returns(false);

        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();

        optionsMonitor
            .Setup(monitor => monitor.Get(It.IsAny<string>()))
            .Returns(new AuthenticationSchemeOptions());

        var handler = new TestablePersonalAccessTokenAuthHandler(
            tokenService.Object,
            Options.Create(new SecurityOptions()),
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default
        );

        var context = new DefaultHttpContext();

        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;

        var scheme = new AuthenticationScheme(
            PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme,
            displayName: null,
            handlerType: typeof(PersonalAccessTokenAuthHandler)
        );

        handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();

        return handler;
    }

    private sealed class TestablePersonalAccessTokenAuthHandler(
        ITokenService tokenService,
        IOptions<SecurityOptions> securityOptions,
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder
    ) : PersonalAccessTokenAuthHandler(tokenService, securityOptions, options, logger, encoder)
    {
        public Task<AuthenticateResult> AuthenticateInternalAsync() => HandleAuthenticateAsync();
    }
}
