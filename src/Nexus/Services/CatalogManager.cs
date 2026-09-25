// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using Nexus.Utilities;
using System.Text.Json;

namespace Nexus.Services;

internal interface ICatalogManager
{
    Task<CatalogContainer[]> GetCatalogContainersAsync(
        CatalogContainer parent,
        CancellationToken cancellationToken
    );
}

internal class CatalogManager(
    IDataControllerService dataControllerService,
    IDatabaseService databaseService,
    IServiceProvider serviceProvider,
    IExtensionHive<IDataSource> sourcesExtensionHive,
    IPipelineService pipelineService,
    IOptions<SecurityOptions> _securityOptions,
    ILogger<CatalogManager> logger
) : ICatalogManager
{
    record CatalogPrototype(
        CatalogRegistration Registration,
        Guid PipelineId,
        DataSourcePipeline Pipeline,
        Guid[] PackageReferenceIds,
        CatalogMetadata Metadata
    );

    private readonly IDataControllerService _dataControllerService = dataControllerService;

    private readonly IDatabaseService _databaseService = databaseService;

    private readonly IServiceProvider _serviceProvider = serviceProvider;

    private readonly IExtensionHive<IDataSource> _sourcesExtensionHive = sourcesExtensionHive;

    private readonly IPipelineService _pipelineService = pipelineService;

    private readonly SecurityOptions _securityOptions = _securityOptions.Value;

    private readonly ILogger<CatalogManager> _logger = logger;

    public async Task<CatalogContainer[]> GetCatalogContainersAsync(
        CatalogContainer parent,
        CancellationToken cancellationToken)
    {
        CatalogContainer[] catalogContainers;

        using var loggerScope = _logger.BeginScope(new Dictionary<string, object>()
        {
            ["ParentCatalogId"] = parent.Id
        });

        /* special case: root */
        if (parent.Id == CatalogContainer.RootCatalogId)
        {
            /* load builtin data source */
            var builtinPipelines = new (Guid, DataSourcePipeline)[]
            {
                (Sample.PipelineId, new DataSourcePipeline(Registrations:
                    [
                        new(
                            Type: typeof(Sample).FullName!,
                            ResourceLocator: default,
                            Configuration: JsonSerializer.SerializeToElement<object?>(default)
                        )
                    ]
                ))
            };

            /* load all catalog identifiers */
            var path = CatalogContainer.RootCatalogId;
            var catalogPrototypes = new List<CatalogPrototype>();

            /* => for the built-in pipelines */

            // TODO: Load parallel?
            /* for each pipeline */
            foreach (var (pipelineId, pipeline) in builtinPipelines)
            {
                using var controller = await _dataControllerService.GetDataSourceControllerAsync(pipeline, cancellationToken);
                var catalogRegistrations = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);

                foreach (var registration in pipeline.Registrations)
                {
                    var packageReferenceIds = pipeline.Registrations
                        .Select(registration => _sourcesExtensionHive.GetPackageReference(registration.Type).Id)
                        .ToArray();

                    foreach (var catalogRegistration in catalogRegistrations)
                    {
                        var metadata = LoadMetadata(catalogRegistration.Path);

                        var catalogPrototype = new CatalogPrototype(
                            catalogRegistration,
                            pipelineId,
                            pipeline,
                            packageReferenceIds,
                            metadata
                        );

                        catalogPrototypes.Add(catalogPrototype);
                    }
                }
            }

            /* => for all configured pipelines */
            var pipelineMap = await _pipelineService.GetAllAsync();

            /* For each pipeline */
            foreach (var (pipelineId, pipeline) in pipelineMap)
            {
                if (pipeline.Disabled)
                    continue;

                try
                {
                    using var controller = await _dataControllerService.GetDataSourceControllerAsync(pipeline, cancellationToken);
                    var catalogRegistrations = await controller.GetCatalogRegistrationsAsync(path, cancellationToken);

                    var packageReferenceIds = pipeline.Registrations
                        .Select(registration => _sourcesExtensionHive.GetPackageReference(registration.Type).Id)
                        .ToArray();

                    foreach (var catalogRegistration in catalogRegistrations)
                    {
                        var metadata = LoadMetadata(catalogRegistration.Path);

                        var prototype = new CatalogPrototype(
                            catalogRegistration,
                            pipelineId,
                            pipeline,
                            packageReferenceIds,
                            metadata
                        );

                        catalogPrototypes.Add(prototype);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to get or process data source registration for pipeline {PipelineId}", pipelineId);
                }
            }

            catalogContainers = ProcessCatalogPrototypes(catalogPrototypes.ToArray());
            _logger.LogInformation("Found {CatalogCount} top level catalogs", catalogContainers.Length);
        }

        /* all other catalogs */
        else
        {
            using var controller = await _dataControllerService
                .GetDataSourceControllerAsync(parent.Pipeline, cancellationToken);

            /* Why trailing slash?
             * Because we want the "directory content" (see the "ls /home/karl/" example here:
             * https://stackoverflow.com/questions/980255/should-a-directory-path-variable-end-with-a-trailing-slash)
             */

            try
            {
                var catalogRegistrations = await controller
                    .GetCatalogRegistrationsAsync(parent.Id + "/", cancellationToken);

                var prototypes = catalogRegistrations.Select(catalogRegistration =>
                {
                    var metadata = LoadMetadata(catalogRegistration.Path);

                    return new CatalogPrototype(
                        catalogRegistration,
                        parent.PipelineId,
                        parent.Pipeline,
                        parent.PackageReferenceIds,
                        metadata);
                });

                catalogContainers = ProcessCatalogPrototypes(prototypes.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to get or process child data source registrations");
                catalogContainers = [];
            }
        }

        return catalogContainers;
    }

    private CatalogContainer[] ProcessCatalogPrototypes(
        IEnumerable<CatalogPrototype> catalogPrototypes
    )
    {
        /* clean up */
        catalogPrototypes = EnsureNoHierarchy(catalogPrototypes);

        /* convert to catalog containers */
        var catalogContainers = catalogPrototypes.Select(prototype =>
        {
            /* create catalog container */
            var catalogContainer = new CatalogContainer(
                prototype.Registration,
                prototype.PipelineId,
                prototype.Pipeline,
                prototype.PackageReferenceIds,
                prototype.Metadata,
                this,
                _databaseService,
                _dataControllerService);

            return catalogContainer;
        });

        return catalogContainers.ToArray();
    }

    private CatalogMetadata LoadMetadata(string catalogId)
    {
        if (_databaseService.TryReadCatalogMetadata(catalogId, out var jsonString))
            return JsonSerializer.Deserialize<CatalogMetadata>(jsonString, JsonSerializerOptions.Web) ?? throw new Exception("catalogMetadata is null");

        else
            return new CatalogMetadata(default, default, default);
    }

    private CatalogPrototype[] EnsureNoHierarchy(
        IEnumerable<CatalogPrototype> catalogPrototypes
    )
    {
        // Background:
        //
        // Nexus allows catalogs to have child catalogs like folders in a file system. To simplify things,
        // it is required that a catalog that comes from a certain data source can only have child
        // catalogs of the very same data source.
        //
        // In general, child catalogs will be loaded lazily. Therefore, for any catalog of the provided array that
        // appears to be a child catalog, it can be assumed it comes from a data source other than the one
        // from the parent catalog.
        //
        //
        // Example:
        //
        // The following combination of catalogs is allowed:
        // data source 1: /a + /a/a + /a/b
        // data source 2: /a2/c
        //
        // The following combination of catalogs is forbidden:
        // data source 1: /a + /a/a + /a/b
        // data source 2: /a/c

        var catalogPrototypesToKeep = new List<CatalogPrototype>();

        foreach (var catalogPrototype in catalogPrototypes)
        {
            var duplicateIndex = catalogPrototypesToKeep.FindIndex(
                current =>
                    {
                        var currentCatalogId = current.Registration.Path + '/';
                        var prototypeCatalogId = catalogPrototype.Registration.Path + '/';

                        return currentCatalogId.StartsWith(prototypeCatalogId, StringComparison.OrdinalIgnoreCase) ||
                               prototypeCatalogId.StartsWith(currentCatalogId, StringComparison.OrdinalIgnoreCase);
                    });

            /* nothing found */
            if (duplicateIndex < 0)
            {
                catalogPrototypesToKeep.Add(catalogPrototype);
            }

            /* duplicate found - keep first */
            else
            {
                _logger.LogWarning("Duplicate catalog {CatalogId}", catalogPrototypesToKeep[duplicateIndex].Registration.Path);
            }
        }

        return [.. catalogPrototypesToKeep];
    }
}
