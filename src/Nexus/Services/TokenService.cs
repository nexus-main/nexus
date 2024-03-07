using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Nexus.Core;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface ITokenService
{
    Task<string> CreateAsync(
        string userId,
        string description,
        DateTime expires,
        IReadOnlyList<TokenClaim> claims);

    Task DeleteAsync(
        string userId,
        Guid tokenId);

    Task DeleteAsync(
        string tokenValue);

    Task<IDictionary<Guid, InternalPersonalAccessToken>> GetAllAsync(
        string userId);
}

internal class TokenService : ITokenService
{
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, InternalPersonalAccessToken>> _cache = new();

    private readonly IDatabaseService _databaseService;

    public TokenService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public Task<string> CreateAsync(
        string userId,
        string description, 
        DateTime expires,
        IReadOnlyList<TokenClaim> claims)
    {
        return UpdateTokenMapAsync(userId, tokenMap =>
        {
            var id = Guid.NewGuid();

            Span<byte> secretBytes = stackalloc byte[64];
            _rng.GetBytes(secretBytes);

            var secret = Convert.ToBase64String(secretBytes);

            var token = new InternalPersonalAccessToken(
                id,
                description,
                expires,
                claims
            );

            tokenMap.AddOrUpdate(
                secret,
                token,
                (key, _) => token
            );

            var tokenValue = $"{secret}_{userId}";

            return tokenValue;
        }, saveChanges: true);
    }

    public Task DeleteAsync(string userId, Guid tokenId)
    {
        return UpdateTokenMapAsync<object?>(userId, tokenMap =>
        {
            var tokenEntry = tokenMap
                .FirstOrDefault(entry => entry.Value.Id == tokenId);

            tokenMap.TryRemove(tokenEntry.Key, out _);
            return default;
        }, saveChanges: true);
    }

    public Task DeleteAsync(string tokenValue)
    {
        var splittedTokenValue = tokenValue.Split('_', count: 1);
        var userId = splittedTokenValue[0];
        var secret = splittedTokenValue[1];

        return UpdateTokenMapAsync<object?>(userId, tokenMap =>
        {
            tokenMap.TryRemove(secret, out _);
            return default;
        }, saveChanges: true);
    }

    public Task<IDictionary<Guid, InternalPersonalAccessToken>> GetAllAsync(
        string userId)
    {
        return UpdateTokenMapAsync(userId, tokenMap =>
        {
            var result = tokenMap.ToDictionary(
                entry => entry.Value.Id, 
                entry => entry.Value);

            return (IDictionary<Guid, InternalPersonalAccessToken>)result;
        }, saveChanges: false);
    }

    private async Task<T> UpdateTokenMapAsync<T>(
        string userId, 
        Func<ConcurrentDictionary<string, InternalPersonalAccessToken>, T> func,
        bool saveChanges)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            var tokenMap = _cache.GetOrAdd(
                userId,
                key => 
                {
                    if (_databaseService.TryReadTokenMap(userId, out var jsonString))
                    {
                        return JsonSerializer.Deserialize<ConcurrentDictionary<string, InternalPersonalAccessToken>>(jsonString) 
                            ?? throw new Exception("tokenMap is null");
                    }

                    else
                    {
                        return new ConcurrentDictionary<string, InternalPersonalAccessToken>();
                    }
                });

            var result = func(tokenMap);

            if (saveChanges)
            {
                using var stream = _databaseService.WriteTokenMap(userId);
                JsonSerializerHelper.SerializeIndented(stream, tokenMap);
            }

            return result;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
