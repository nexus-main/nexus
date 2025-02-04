// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Reflection;
using System.Text.Json;
using Apollo3zehn.PackageManagement.Services;
using Nexus.Extensibility;

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

                foreach (var registration in pipeline.Registrations)
                {
                    var dataSourceTypeName = registration.Type;

                    try
                    {
                        var dataSourceType = _extensionHive.GetExtensionType(dataSourceTypeName);

                        if (!dataSourceType.IsAssignableTo(dataSourceType))
                            continue;

                        /* Upgrade */
                        var upgradableDataSource = (IUpgradableDataSource)Activator.CreateInstance(dataSourceType)!;
                        var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(1));

                        var upgradedConfiguration = await upgradableDataSource.UpgradeSourceConfigurationAsync(
                            registration.Configuration,
                            timeoutTokenSource.Token
                        );

                        /* Ensure deserialization works */
                        var sourceInterfaceTypes = dataSourceType.GetInterfaces();

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

                        _ = JsonSerializer.Deserialize(upgradedConfiguration, configurationType, JsonSerializerOptions.Web);

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
                    _ = _pipelineService.TryUpdateAsync(userId, pipelineId, pipeline with { Registrations = registrations });
            }
        }
    }
}