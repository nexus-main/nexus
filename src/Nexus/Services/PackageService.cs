// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Nexus.Core.V1;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IPackageService
{
    Task<Guid> PutAsync(PackageReference packageReference);

    bool TryGet(
        Guid packageReferenceId,
        [NotNullWhen(true)] out PackageReference? packageReference
    );

    Task DeleteAsync(Guid packageReferenceId);

    Task<IReadOnlyDictionary<Guid, PackageReference>> GetAllAsync();
}

internal class PackageService(IDatabaseService databaseService)
    : IPackageService
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private ConcurrentDictionary<Guid, PackageReference>? _cache;

    private readonly IDatabaseService _databaseService = databaseService;

    public Task<Guid> PutAsync(
        PackageReference packageReference)
    {
        return InteractWithPackageReferenceMapAsync(packageReferenceMap =>
        {
            var id = Guid.NewGuid();

            packageReferenceMap.AddOrUpdate(
                id,
                packageReference,
                (key, _) => packageReference
            );

            return id;
        }, saveChanges: true);
    }

    public bool TryGet(
        Guid packageReferenceId,
        [NotNullWhen(true)] out PackageReference? packageReference)
    {
        var packageReferencMap = GetPackageReferenceMap();

        return packageReferencMap.TryGetValue(packageReferenceId, out packageReference);
    }

    public Task DeleteAsync(Guid packageReferenceId)
    {
        return InteractWithPackageReferenceMapAsync<object?>(packageReferenceMap =>
        {
            var packageReferenceEntry = packageReferenceMap
                .FirstOrDefault(entry => entry.Key == packageReferenceId);

            packageReferenceMap.TryRemove(packageReferenceEntry.Key, out _);
            return default;
        }, saveChanges: true);
    }

    public Task<IReadOnlyDictionary<Guid, PackageReference>> GetAllAsync()
    {
        return InteractWithPackageReferenceMapAsync(
            packageReferenceMap => (IReadOnlyDictionary<Guid, PackageReference>)packageReferenceMap,
            saveChanges: false
        );
    }

    private ConcurrentDictionary<Guid, PackageReference> GetPackageReferenceMap()
    {
        if (_cache is null)
        {
            if (_databaseService.TryReadPackageReferenceMap(out var jsonString))
            {
                _cache = JsonSerializer.Deserialize<ConcurrentDictionary<Guid, PackageReference>>(jsonString)
                    ?? throw new Exception("packageReferenceMap is null");
            }

            else
            {
                return new();
            }
        }

        return _cache;
    }

    private async Task<T> InteractWithPackageReferenceMapAsync<T>(
        Func<ConcurrentDictionary<Guid, PackageReference>, T> func,
        bool saveChanges
    )
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            var packageReferenceMap = GetPackageReferenceMap();
            var result = func(packageReferenceMap);

            if (saveChanges)
            {
                using var stream = _databaseService.WritePackageReferenceMap();
                JsonSerializerHelper.SerializeIndented(stream, packageReferenceMap);
            }

            return result;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
