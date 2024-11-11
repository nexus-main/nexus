using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace Nexus.PackageManagement.Services;

/// <summary>
/// An interface which defined interactions with the database.
/// </summary>
public interface IPackageManagementDatabaseService
{

    /// <summary>
    /// Reads the package reference map.
    /// </summary>
    /// <param name="packageReferenceMap">The package reference map.</param>
    bool TryReadPackageReferenceMap([NotNullWhen(true)] out string? packageReferenceMap);

    /// <summary>
    /// Returns a stream to write the package reference map to.
    /// </summary>
    Stream WritePackageReferenceMap();
}

internal class PackageManagementDatabaseService(IOptions<IPackageManagementPathsOptions> pathsOptions)
    : IPackageManagementDatabaseService
{
    private readonly IPackageManagementPathsOptions _pathsOptions = pathsOptions.Value;

    private const string FILE_EXTENSION = ".json";

    private const string PACKAGES = "packages";

    public bool TryReadPackageReferenceMap([NotNullWhen(true)] out string? packageReferenceMap)
    {
        var folderPath = _pathsOptions.Config;
        var packageReferencesFilePath = Path.Combine(folderPath, PACKAGES + FILE_EXTENSION);

        packageReferenceMap = default;

        if (File.Exists(packageReferencesFilePath))
        {
            packageReferenceMap = File.ReadAllText(packageReferencesFilePath);
            return true;
        }

        return false;
    }

    public Stream WritePackageReferenceMap()
    {
        var folderPath = _pathsOptions.Config;
        var packageReferencesFilePath = Path.Combine(folderPath, PACKAGES + FILE_EXTENSION);

        Directory.CreateDirectory(folderPath);

        return File.Open(packageReferencesFilePath, FileMode.Create, FileAccess.Write);
    }
}