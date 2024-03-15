using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Nexus.Services;

internal interface IExtensionHive
{
    IEnumerable<Type> GetExtensions<T>(
        ) where T : IExtension;

    InternalPackageReference GetPackageReference<T>(
        string fullName) where T : IExtension;

    T GetInstance<T>(
        string fullName) where T : IExtension;

    Task LoadPackagesAsync(
        IEnumerable<InternalPackageReference> packageReferences,
        IProgress<double> progress,
        CancellationToken cancellationToken);

    Task<string[]> GetVersionsAsync(
        InternalPackageReference packageReference,
        CancellationToken cancellationToken);
}

internal class ExtensionHive(
    IOptions<PathsOptions> pathsOptions,
    ILogger<ExtensionHive> logger,
    ILoggerFactory loggerFactory) : IExtensionHive
{
    private readonly ILogger<ExtensionHive> _logger = logger;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    private Dictionary<PackageController, ReadOnlyCollection<Type>>? _packageControllerMap = default!;

    public async Task LoadPackagesAsync(
        IEnumerable<InternalPackageReference> packageReferences,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        // clean up
        if (_packageControllerMap is not null)
        {
            _logger.LogDebug("Unload previously loaded packages");

            foreach (var (controller, _) in _packageControllerMap)
            {
                controller.Unload();
            }

            _packageControllerMap = default;
        }

        var nexusPackageReference = new InternalPackageReference(
            Id: PackageController.BUILTIN_ID,
            Provider: PackageController.BUILTIN_PROVIDER,
            Configuration: []
        );

        packageReferences = new List<InternalPackageReference>() { nexusPackageReference }.Concat(packageReferences);

        // build new
        var packageControllerMap = new Dictionary<PackageController, ReadOnlyCollection<Type>>();
        var currentCount = 0;
        var totalCount = packageReferences.Count();

        foreach (var packageReference in packageReferences)
        {
            var packageController = new PackageController(packageReference, _loggerFactory.CreateLogger<PackageController>());
            using var scope = _logger.BeginScope(packageReference.Configuration.ToDictionary(entry => entry.Key, entry => (object)entry.Value));

            try
            {
                _logger.LogDebug("Load package");
                var assembly = await packageController.LoadAsync(_pathsOptions.Packages, cancellationToken);

                /* Currently, only the directly referenced assembly is being searched for extensions. When this
                 * behavior should change, it is important to think about the consequences: What should happen when
                 * an extension is references as usual but at the same time it serves as a base class extensions in
                 * other packages. If all assemblies in that package are being scanned, the original extension would
                 * be found twice.
                 */
                var types = ScanAssembly(assembly, packageReference.Provider == PackageController.BUILTIN_PROVIDER
                    ? assembly.DefinedTypes
                    : assembly.ExportedTypes);

                packageControllerMap[packageController] = types;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loading package failed");
            }

            currentCount++;
            progress.Report(currentCount / (double)totalCount);
        }

        _packageControllerMap = packageControllerMap;
    }

    public Task<string[]> GetVersionsAsync(
        InternalPackageReference packageReference,
        CancellationToken cancellationToken)
    {
        var controller = new PackageController(
            packageReference,
            _loggerFactory.CreateLogger<PackageController>());

        return controller.DiscoverAsync(cancellationToken);
    }

    public IEnumerable<Type> GetExtensions<T>() where T : IExtension
    {
        if (_packageControllerMap is null)
        {
            return Enumerable.Empty<Type>();
        }

        else
        {
            var types = _packageControllerMap.SelectMany(entry => entry.Value);

            return types
                .Where(type => typeof(T).IsAssignableFrom(type));
        }
    }

    public InternalPackageReference GetPackageReference<T>(string fullName) where T : IExtension
    {
        if (!TryGetTypeInfo<T>(fullName, out var packageController, out var _))
            throw new Exception($"Could not find extension {fullName} of type {typeof(T).FullName}.");

        return packageController.PackageReference;
    }

    public T GetInstance<T>(string fullName) where T : IExtension
    {
        if (!TryGetTypeInfo<T>(fullName, out var _, out var type))
            throw new Exception($"Could not find extension {fullName} of type {typeof(T).FullName}.");

        _logger.LogDebug("Instantiate extension {ExtensionType}", fullName);

        var instance = (T)(Activator.CreateInstance(type) ?? throw new Exception("instance is null"));

        return instance;
    }

    private bool TryGetTypeInfo<T>(
        string fullName,
        [NotNullWhen(true)] out PackageController? packageController,
        [NotNullWhen(true)] out Type? type)
        where T : IExtension
    {
        type = default;
        packageController = default;

        if (_packageControllerMap is null)
            return false;

        IEnumerable<(PackageController Controller, Type Type)> typeInfos = _packageControllerMap
            .SelectMany(entry => entry.Value.Select(type => (entry.Key, type)));

        (packageController, type) = typeInfos
            .Where(typeInfo => typeof(T).IsAssignableFrom(typeInfo.Type) && typeInfo.Type.FullName == fullName)
            .FirstOrDefault();

        if (type is null)
            return false;

        return true;
    }

    private ReadOnlyCollection<Type> ScanAssembly(Assembly assembly, IEnumerable<Type> types)
    {
        var foundTypes = types
            .Where(type =>
            {
                var isClass = type.IsClass;
                var isInstantiatable = !type.IsAbstract;
                var isDataSource = typeof(IDataSource).IsAssignableFrom(type);
                var isDataWriter = typeof(IDataWriter).IsAssignableFrom(type);

                if (isClass && isInstantiatable && (isDataSource | isDataWriter))
                {
                    var hasParameterlessConstructor = type.GetConstructor(Type.EmptyTypes) is not null;

                    if (!hasParameterlessConstructor)
                        _logger.LogWarning("Type {TypeName} from assembly {AssemblyName} has no parameterless constructor", type.FullName, assembly.FullName);

                    return hasParameterlessConstructor;
                }

                return false;
            })
            .ToList()
            .AsReadOnly();

        return foundTypes;
    }
}
