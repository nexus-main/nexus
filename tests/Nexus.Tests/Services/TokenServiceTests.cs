// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Moq;
using Nexus.Core;
using Nexus.Services;
using Nexus.Utilities;
using Xunit;

namespace Services;

public class TokenServiceTests
{
    delegate bool GobbleReturns(string userId, out string? tokenMap);

    [Fact]
    public async Task CanCreateToken()
    {
        // Arrange
        var filePath = Path.GetTempFileName();
        var tokenService = GetTokenService(filePath, []);
        var description = "The description.";
        var expires = new DateTime(2020, 01, 01);
        var claim1Type = "claim1";
        var claim1Value = "value1";
        var claim2Type = "claim2";
        var claim2Value = "value2";

        // Act
        await tokenService.CreateAsync(
            userId: "starlord",
            description,
            expires,
            new List<TokenClaim>
            {
                new(claim1Type, claim1Value),
                new(claim2Type, claim2Value),
            }
        );

        // Assert
        var jsonString = File.ReadAllText(filePath);
        var actualTokenMap = JsonSerializer.Deserialize<Dictionary<string, InternalPersonalAccessToken>>(jsonString)!;

        var entry1 = Assert.Single(actualTokenMap);
        Assert.Equal(description, entry1.Value.Description);
        Assert.Equal(expires, entry1.Value.Expires);

        Assert.Collection(entry1.Value.Claims,
            entry1_1 =>
            {
                Assert.Equal(claim1Type, entry1_1.Type);
                Assert.Equal(claim1Value, entry1_1.Value);
            },
            entry1_2 =>
            {
                Assert.Equal(claim2Type, entry1_2.Type);
                Assert.Equal(claim2Value, entry1_2.Value);
            });
    }

    [Fact]
    public void CanTryGetToken()
    {
        // Arrange
        var expectedDescription = "The description";

        var tokenMap = new Dictionary<string, InternalPersonalAccessToken>()
        {
            ["abc"] = new InternalPersonalAccessToken(
                default,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            ),
            ["def"] = new InternalPersonalAccessToken(
                default,
                Description: "The description",
                Expires: default,
                Claims: new List<TokenClaim>()
            )
        };

        var tokenService = GetTokenService(default!, tokenMap);

        // Act
        var actual = tokenService.TryGet("starlord", "def", out var actualToken);

        // Assert
        Assert.True(actual);
        Assert.Equal(expectedDescription, actualToken!.Description);
    }

    [Fact]
    public async Task CanDeleteTokenByValue()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var tokenMap = new Dictionary<string, InternalPersonalAccessToken>()
        {
            ["abc"] = new InternalPersonalAccessToken(
                id1,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            ),
            ["def"] = new InternalPersonalAccessToken(
                id2,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            )
        };

        var filePath = Path.GetTempFileName();
        var tokenService = GetTokenService(filePath, tokenMap);

        // Act
        await tokenService.DeleteAsync("starlord", "abc");

        // Assert
        tokenMap.Remove("abc");
        var expected = JsonSerializerHelper.SerializeIndented(tokenMap);
        var actual = File.ReadAllText(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanDeleteTokenById()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var tokenMap = new Dictionary<string, InternalPersonalAccessToken>()
        {
            ["abc"] = new InternalPersonalAccessToken(
                id1,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            ),
            ["def"] = new InternalPersonalAccessToken(
                id2,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            )
        };

        var filePath = Path.GetTempFileName();
        var tokenService = GetTokenService(filePath, tokenMap);

        // Act
        await tokenService.DeleteAsync(string.Empty, id1);

        // Assert
        tokenMap.Remove("abc");
        var expected = JsonSerializerHelper.SerializeIndented(tokenMap);
        var actual = File.ReadAllText(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanGetAllTokens()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var expectedTokenMap = new Dictionary<string, InternalPersonalAccessToken>()
        {
            ["abc"] = new InternalPersonalAccessToken(
                id1,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            ),
            ["def"] = new InternalPersonalAccessToken(
                id2,
                Description: string.Empty,
                Expires: default,
                Claims: new List<TokenClaim>()
            )
        };

        var filePath = Path.GetTempFileName();
        var tokenService = GetTokenService(filePath, expectedTokenMap);

        // Act
        var actualTokenMap = await tokenService.GetAllAsync(string.Empty);

        // Assert
        var expected = JsonSerializerHelper.SerializeIndented(expectedTokenMap);
        var actual = JsonSerializerHelper.SerializeIndented(actualTokenMap);

        Assert.Equal(expected, actual);
    }

    private static ITokenService GetTokenService(string filePath, Dictionary<string, InternalPersonalAccessToken> tokenMap)
    {
        var databaseService = Mock.Of<IDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadTokenMap(It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns(new GobbleReturns((string userId, out string? tokenMapString) =>
            {
                tokenMapString = JsonSerializer.Serialize(tokenMap);
                return true;
            }));

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.WriteTokenMap(It.IsAny<string>()))
            .Returns(() => File.OpenWrite(filePath));

        var tokenService = new TokenService(databaseService);

        return tokenService;
    }
}