using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Nexus.Core;
using Nexus.Services;
using System.IdentityModel.Tokens.Jwt;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Services
{
    public class NexusPersonalAccessTokenServiceTests
    {
        private static readonly IOptions<SecurityOptions> _securityOptions = Options.Create(new SecurityOptions()
        {
            AccessTokenLifetime = TimeSpan.FromHours(1),
            RefreshTokenLifetime = TimeSpan.FromHours(1)
        });

        private static readonly TokenValidationParameters _validationParameters = new()
        {
            NameClaimType = Claims.Name,
            LifetimeValidator = (before, expires, token, parameters) => expires > DateTime.UtcNow,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateActor = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(SecurityOptions.DefaultSigningKey))
        };

        private static NexusUser CreateUser(string name, params RefreshToken[] refreshTokens)
        {
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            var user = new NexusUser(
                id: Guid.NewGuid().ToString(),
                name: name
            )
            {
                RefreshTokens = refreshTokens.ToList()
            };

            foreach (var token in refreshTokens)
            {
                token.Owner = user;
            }

            return user;
        }

        [Fact]
        public async Task CanGenerateRefreshToken()
        {
            // Arrange
            var expectedName = "foo";
            var user = CreateUser(expectedName);

            var dbService = Mock.Of<IDBService>();

            var authService = new NexusPersonalAccessTokenService(
                dbService,
                _securityOptions);
            _ = new JwtSecurityTokenHandler();

            // Act
            var refreshToken = await authService.GenerateRefreshTokenAsync(user, string.Empty);

            // Assert
            Assert.Single(user.RefreshTokens);
            Assert.Equal(refreshToken, user.RefreshTokens.First().Token);

            Assert.Equal(
                user.RefreshTokens.First().Expires,
                DateTime.UtcNow.Add(_securityOptions.Value.RefreshTokenLifetime),
                TimeSpan.FromMinutes(1));
        }

        [Fact]
        public async Task CanRefresh()
        {
            // Arrange
            var expectedName = "foo";

            var internalRefreshToken = new InternalRefreshToken(
                Version: 1,
                Id: Guid.NewGuid(),
                Value: string.Empty
            );

            var serializedToken = InternalRefreshToken.Serialize(internalRefreshToken);
            var storedRefreshToken = new RefreshToken(Guid.NewGuid(), serializedToken, DateTime.UtcNow.AddDays(1), string.Empty);
            var user = CreateUser(expectedName, storedRefreshToken);
            storedRefreshToken.Owner = user;

            var dbService = Mock.Of<IDBService>();

            var service = new NexusPersonalAccessTokenService(
                dbService,
                _securityOptions);

            var tokenHandler = new JwtSecurityTokenHandler();

            // Act
            var tokenPair = await service.RefreshTokenAsync(storedRefreshToken);

            // Assert
            Assert.Single(user.RefreshTokens);

            var principal = tokenHandler.ValidateToken(tokenPair.AccessToken, _validationParameters, out var _);
            var actualName = principal.Identity!.Name;

            Assert.Equal(expectedName, actualName);
        }

        [Fact]
        public async Task CanRevoke()
        {
            // Arrange
            var internalRefreshToken = new InternalRefreshToken(
                Version: 1,
                Id: Guid.NewGuid(),
                Value: string.Empty
            );

            var serializedToken = InternalRefreshToken.Serialize(internalRefreshToken);
            var storedRefreshToken = new RefreshToken(Guid.NewGuid(), serializedToken, DateTime.UtcNow.AddDays(1), string.Empty);
            var user = CreateUser("foo", storedRefreshToken);
            storedRefreshToken.Owner = user;

            var dbService = Mock.Of<IDBService>();

            var service = new NexusPersonalAccessTokenService(
                dbService,
                _securityOptions);

            // Act
            await service.RevokeTokenAsync(storedRefreshToken);

            // Assert
            Assert.Empty(user.RefreshTokens);
        }
    }
}