using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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

    bool TryGet(
        string userId,
        string secret,
        [NotNullWhen(true)] out InternalPersonalAccessToken? token);

    Task DeleteAsync(string userId, Guid tokenId);

    Task DeleteAsync(string userId, string secret);

    Task<IReadOnlyDictionary<string, InternalPersonalAccessToken>> GetAllAsync(string userId);
}

internal class TokenService(IDatabaseService databaseService) 
    : ITokenService
{
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, InternalPersonalAccessToken>> _cache = new();

    private readonly IDatabaseService _databaseService = databaseService;

    public Task<string> CreateAsync(
        string userId,
        string description,
        DateTime expires,
        IReadOnlyList<TokenClaim> claims)
    {
        return InteractWithTokenMapAsync(userId, tokenMap =>
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

            return secret;
        }, saveChanges: true);
    }

    public bool TryGet(
        string userId,
        string secret,
        [NotNullWhen(true)] out InternalPersonalAccessToken? token)
    {
        var tokenMap = GetTokenMap(userId);

        return tokenMap.TryGetValue(secret, out token);
    }

    public Task DeleteAsync(string userId, Guid tokenId)
    {
        return InteractWithTokenMapAsync<object?>(userId, tokenMap =>
        {
            var tokenEntry = tokenMap
                .FirstOrDefault(entry => entry.Value.Id == tokenId);

            tokenMap.TryRemove(tokenEntry.Key, out _);
            return default;
        }, saveChanges: true);
    }

    public Task DeleteAsync(string userId, string secret)
    {
        return InteractWithTokenMapAsync<object?>(userId, tokenMap =>
        {
            tokenMap.TryRemove(secret, out _);
            return default;
        }, saveChanges: true);
    }

    public Task<IReadOnlyDictionary<string, InternalPersonalAccessToken>> GetAllAsync(
        string userId)
    {
        return InteractWithTokenMapAsync(
            userId,
            tokenMap => (IReadOnlyDictionary<string, InternalPersonalAccessToken>)tokenMap,
            saveChanges: false);
    }

    private ConcurrentDictionary<string, InternalPersonalAccessToken> GetTokenMap(
        string userId)
    {
        return _cache.GetOrAdd(
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
    }

    private async Task<T> InteractWithTokenMapAsync<T>(
        string userId,
        Func<ConcurrentDictionary<string, InternalPersonalAccessToken>, T> func,
        bool saveChanges)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            var tokenMap = GetTokenMap(userId);
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
