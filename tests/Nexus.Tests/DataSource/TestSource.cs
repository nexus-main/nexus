// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.UI.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DataSource;

public record TestSourceSettings(
    int Version,
    double Bar
);

[ExtensionDescription(
    "Augments existing catalogs with more awesome data.",
    "https://github.com/nexus-main/nexus",
    "https://github.com/nexus-main/nexus/blob/master/tests/Nexus.Tests/DataSource/TestSource.cs")]
public class TestSource : IDataSource<TestSourceSettings?>
{
    public const string LocalCatalogId = "/SAMPLE/LOCAL";

    public static Task<JsonElement> UpgradeSourceConfigurationAsync(JsonElement configuration)
    {
        // Nothing to upgrade
        if (configuration.ValueKind == JsonValueKind.Null)
            return Task.FromResult(configuration);

        // Ensure configuration is an object
        if (configuration.ValueKind != JsonValueKind.Object)
            throw new Exception("Invalid configuration");

        // If version exists, it should be equal to 2
        if (configuration.TryGetProperty("version", out var versionElement))
        {
            if (versionElement.ValueKind != JsonValueKind.Number)
                throw new Exception("Invalid configuration");

            if (versionElement.GetInt32() != 2)
                throw new Exception("Invalid configuration");

            return Task.FromResult(configuration);
        }

        // Else: upgrade
        else
        {
            var configurationNode = JsonSerializer.SerializeToNode(configuration)!;

            configurationNode["version"] = 2;
            configurationNode["bar"] = configurationNode["foo"]!.GetValue<double>();

            var upgradedConfiguration = JsonSerializer.SerializeToElement(configurationNode);

            return Task.FromResult(upgradedConfiguration);
        }
    }

    public Task SetContextAsync(
        DataSourceContext<TestSourceSettings?> context,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        return Task.CompletedTask;
    }

    public Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(Array.Empty<CatalogRegistration>());
    }

    public Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken
    )
    {
        if (catalog.Resources is null)
            return Task.FromResult(catalog);

        var representation = new Representation(NexusDataType.UINT8, TimeSpan.FromSeconds(1));
        var resource = new Resource(id: "foo", representations: [representation]);

        var newCatalog = catalog with
        {
            Resources = [
                catalog.Resources[0],
                catalog.Resources[2],
                catalog.Resources[3],

                /* test modifying */
                catalog.Resources[1] with { Id = "V1_renamed" }, 

                /* test adding */
                resource
            ]
        };

        return Task.FromResult(newCatalog);
    }

    public Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(double.NaN);
    }

    public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult((DateTime.MaxValue, DateTime.MinValue));
    }

    public Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken
    )
    {
        foreach (var request in requests)
        {
            request.Data.Span.Fill(1);
            request.Status.Span.Fill(1);
        }

        return Task.CompletedTask;
    }
}