// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.DataModel;

namespace Nexus.Extensibility;

/// <summary>
/// A base interface for IDataSource&gt;T&lt;.
/// </summary>
public interface IDataSource
{
    /// <summary>
    /// Gets the catalog registrations that are located under <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The parent path for which to return catalog registrations.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The catalog identifiers task.</returns>
    Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Enriches the provided <see cref="ResourceCatalog"/>.
    /// </summary>
    /// <param name="catalog">The catalog to enrich.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The catalog request task.</returns>
    Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the time range of the <see cref="ResourceCatalog"/>.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The time range task.</returns>
    Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets the availability of the <see cref="ResourceCatalog"/>.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">The begin of the availability period.</param>
    /// <param name="end">The end of the availability period.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The availability task.</returns>
    Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Performs a number of read requests.
    /// </summary>
    /// <param name="begin">The beginning of the period to read.</param>
    /// <param name="end">The end of the period to read.</param>
    /// <param name="requests">The array of read requests.</param>
    /// <param name="readData">A delegate to asynchronously read data from Nexus.</param>
    /// <param name="progress">An object to report the read progress between 0.0 and 1.0.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The task.</returns>
    Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken
    );
}

#pragma warning disable CS0618 // Type or member is obsolete

/// <summary>
/// A data source.
/// </summary>
/// <typeparam name="T">The source configuration type</typeparam>
public interface IDataSource<T> : IDataSource
{
    /// <summary>
    /// Invoked by Nexus right after construction to provide the context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The task.</returns>
    Task SetContextAsync(
        DataSourceContext<T> context,
        ILogger logger,
        CancellationToken cancellationToken
    );
}

#pragma warning restore CS0618 // Type or member is obsolete

/// <summary>
/// Data sources which have configuration data to be upgraded should implement this interface.
/// </summary>
public interface IUpgradableDataSource
{
    /// <summary>
    /// Upgrades the source configuration.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>The upgraded source configuration.</returns>
    static abstract Task<JsonElement> UpgradeSourceConfigurationAsync(
        JsonElement configuration,
        CancellationToken cancellationToken
    );
}