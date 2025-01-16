// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Nexus.PackageManagement.Core;

namespace Nexus.PackageManagement.Services;

/// <summary>
/// An interface which defined interactions with package references.
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// Puts a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference.</param>
    Task<Guid> PutAsync(PackageReference packageReference);

    /// <summary>
    /// Tries to get the requested package reference. Returns null if the package reference does not exist.
    /// </summary>
    /// <param name="packageReferenceId">The package reference ID.</param>
    Task<PackageReference?> GetAsync(Guid packageReferenceId);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The package reference ID.</param>
    /// <returns></returns>
    Task DeleteAsync(Guid packageReferenceId);

    /// <summary>
    /// Gets all package references.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PackageReference>> GetAllAsync();
}

internal class PackageService(IPackageManagementDatabaseService databaseService)
    : IPackageService
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private Dictionary<Guid, PackageReference>? _cache;

    private readonly IPackageManagementDatabaseService _databaseService = databaseService;

    public Task<Guid> PutAsync(
        PackageReference packageReference)
    {
        return InteractWithPackageReferenceMapAsync(packageReferenceMap =>
        {
            var id = Guid.NewGuid();

            packageReferenceMap[id] = packageReference;

            return id;
        }, saveChanges: true);
    }

    public Task<PackageReference?> GetAsync(Guid packageReferenceId)
    {
        return InteractWithPackageReferenceMapAsync(packageReferenceMap =>
        {
            var _ = packageReferenceMap.TryGetValue(packageReferenceId, out var packageReference);

            return packageReference;
        }, saveChanges: false);
    }

    public Task DeleteAsync(Guid packageReferenceId)
    {
        return InteractWithPackageReferenceMapAsync<object?>(packageReferenceMap =>
        {
            var packageReferenceEntry = packageReferenceMap
                .FirstOrDefault(entry => entry.Key == packageReferenceId);

            packageReferenceMap.Remove(packageReferenceEntry.Key, out _);
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

    private Dictionary<Guid, PackageReference> GetPackageReferenceMap()
    {
        if (_cache is null)
        {
            if (_databaseService.TryReadPackageReferenceMap(out var jsonString))
            {
                _cache = JsonSerializer.Deserialize<Dictionary<Guid, PackageReference>>(jsonString)
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
        Func<Dictionary<Guid, PackageReference>, T> func,
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
