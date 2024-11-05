using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Nexus.PackageManagement.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up MVC services in an <see cref="IServiceCollection" />.
/// </summary>
public static class MvcServiceCollectionExtensions
{
    /// <summary>
    /// Adds services required for the package management.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPackageManagement(this IServiceCollection services)
    {
        return services
            .AddSingleton<IPackageService, PackageService>()
            .AddSingleton<IPackageManagementDatabaseService, IPackageManagementDatabaseService>()
            .AddSingleton<IExtensionHive, ExtensionHive>()
            .Configure<IPackageManagementPathsOptions>(options => new PackageManagementPathsOptions());
    }
}

internal class DatabaseService(IOptions<IPackageManagementPathsOptions> pathsOptions)
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