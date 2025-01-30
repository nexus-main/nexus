// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Reflection;
using System.Text.Json;
using Apollo3zehn.PackageManagement.Services;
using Nexus.Core.V1;
using Nexus.Extensibility;
using Polly.Fallback;

namespace Nexus.Services;

internal interface IUpgradeConfigurationService
{
    Task UpgradeAsync();
}

internal class UpgradeConfigurationService(
    IPipelineService pipelineService,
    IExtensionHive<IDataSource> extensionHive,
    ILogger<UpgradeConfigurationService> logger
) : IUpgradeConfigurationService
{
    private readonly IPipelineService _pipelineService = pipelineService;

    private readonly IExtensionHive<IDataSource> _extensionHive = extensionHive;

    private readonly ILogger _logger = logger;

    public async Task UpgradeAsync()
    {
        foreach (var (userId, pipelineMap) in await _pipelineService.GetAllAsync())
        {
            _logger.LogDebug("Upgrade source registration configurations for user {UserId}", userId);

            foreach (var (pipelineId, pipeline) in pipelineMap)
            {
                _logger.LogTrace("Upgrade pipeline {PipelineId}", pipelineId);

                var index = 0;
                var isDirty = false;
                var registrations = pipeline.Registrations.ToList();

                foreach (var registration in registrations)
                {
                    var sourceTypeName = registration.Type;

                    try
                    {
                        /* Find generic parameters */
                        var sourceType = _extensionHive.GetExtensionType(sourceTypeName);
                        var sourceInterfaceTypes = sourceType.GetInterfaces();

                        if (!sourceInterfaceTypes.Contains(typeof(IUpgradableDataSource)))
                            continue;

                        var genericInterface = sourceInterfaceTypes
                            .FirstOrDefault(x =>
                                x.IsGenericType &&
                                x.GetGenericTypeDefinition() == typeof(IDataSource<>)
                            );

                        if (genericInterface is null)
                            throw new Exception("Data sources must implement IDataSource<T>.");

                        var configurationType = genericInterface.GenericTypeArguments[0];

                        /* Invoke InternalUpgradeAsync */
                        var methodInfo = typeof(DataSourceController)
                            .GetMethod(nameof(InternalUpgradeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

                        var genericMethod = methodInfo
                            .MakeGenericMethod(sourceType, configurationType);

                        var upgradedConfiguration = await (Task<JsonElement>)genericMethod.Invoke(
                            default,
                            [
                                registration.Configuration
                            ]
                        )!;

                        /* Update pipeline */
                        if (!JsonElement.DeepEquals(registration.Configuration, upgradedConfiguration))
                        {
                            registrations[index] = registration with
                            {
                                Configuration = upgradedConfiguration
                            };

                            isDirty = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unable to upgrade source registration");
                    }

                    finally
                    {
                        index++;
                    }
                }

                /* Save changes */
                if (isDirty)
                    _ = _pipelineService.TryUpdateAsync(userId, pipelineId, pipeline);
            }
        }
    }

    private static async Task<JsonElement> InternalUpgradeAsync<TSource, TConfiguration>(JsonElement configuration)
        where TSource : IUpgradableDataSource
    {
        var upgradedConfiguration = await TSource.UpgradeSourceConfigurationAsync(configuration);

        /* ensure it can be deserialized */
        _ = JsonSerializer.Deserialize<TConfiguration>(upgradedConfiguration);

        return upgradedConfiguration;
    }
}
