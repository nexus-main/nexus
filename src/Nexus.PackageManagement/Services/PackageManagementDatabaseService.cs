using System.Diagnostics.CodeAnalysis;

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