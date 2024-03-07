using System.Collections.Concurrent;
using System.Security.Cryptography;
using Nexus.Core;

namespace Nexus.Services;

internal interface ITokenService
{
    Task<string> CreateAsync(
        string userId,
        string description,
        DateTime expires,
        IDictionary<string, string> claims);

    Task DeleteAsync(
        string userId,
        Guid tokenId);

    Task DeleteAsync(
        string tokenValue);

    Task<IDictionary<Guid, PersonalAccessToken>> GetAllAsync(
        string userId);
}

internal class TokenService : ITokenService
{
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PersonalAccessToken>> _cache = new();

    public Task<string> CreateAsync(
        string userId,
        string description, 
        DateTime expires,
        IDictionary<string, string> claims)
    {
        var userMap = GetUserMap(userId);
        var id = Guid.NewGuid();

        Span<byte> secretBytes = stackalloc byte[64];
        _rng.GetBytes(secretBytes);

        var secret = Convert.ToBase64String(secretBytes);

        var token = new PersonalAccessToken(
            id,
            secret,
            description,
            expires,
            claims
        );

        userMap.AddOrUpdate(
            secret,
            token,
            (key, _) => token
        );

        var tokenValue = $"{userId}_{secret}";

        return Task.FromResult(tokenValue);
    }

    public Task DeleteAsync(string userId, Guid tokenId)
    {
        var userMap = GetUserMap(userId);


    }

    public Task DeleteAsync(string tokenValue)
    {
        var userMap = GetUserMap(userId);
    }

    public Task<IDictionary<Guid, PersonalAccessToken>> GetAllAsync(
        string userId)
    {
        var userMap = GetUserMap(userId);

        var result = userMap.ToDictionary(
            entry => entry.Value.Id, 
            entry => entry.Value);

        return Task.FromResult((IDictionary<Guid, PersonalAccessToken>)result);
    }

    private ConcurrentDictionary<string, PersonalAccessToken> GetUserMap(string userId)
    {
        return _cache.GetOrAdd(
            userId, 
            key => new ConcurrentDictionary<string, PersonalAccessToken>());
    }
}
