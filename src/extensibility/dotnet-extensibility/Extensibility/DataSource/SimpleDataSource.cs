// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Nexus.DataModel;

namespace Nexus.Extensibility;

/// <summary>
/// A simple implementation of a data source.
/// </summary>
public abstract class SimpleDataSource<T> : IDataSource<T>
{
    /// <summary>
    /// Gets the data source context. This property is not accessible from within class constructors as it will bet set later.
    /// </summary>
    protected DataSourceContext<T> Context { get; private set; } = default!;

    /// <summary>
    /// Gets the data logger. This property is not accessible from within class constructors as it will bet set later.
    /// </summary>
    protected ILogger Logger { get; private set; } = default!;

    /// <inheritdoc />
    public Task SetContextAsync(
        DataSourceContext<T> context,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Context = context;
        Logger = logger;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public abstract Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
        string path,
        CancellationToken cancellationToken
    );

    /// <inheritdoc />
    public abstract Task<ResourceCatalog> EnrichCatalogAsync(
        ResourceCatalog catalog,
        CancellationToken cancellationToken
    );

    /// <inheritdoc />
    public virtual Task<CatalogTimeRange> GetTimeRangeAsync(
        string catalogId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(new CatalogTimeRange(DateTime.MinValue, DateTime.MaxValue));
    }

    /// <inheritdoc />
    public virtual Task<double> GetAvailabilityAsync(
        string catalogId,
        DateTime begin,
        DateTime end,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(double.NaN);
    }

    /// <inheritdoc />
    public abstract Task ReadAsync(
        DateTime begin,
        DateTime end,
        ReadRequest[] requests,
        ReadDataHandler readData,
        IProgress<double> progress,
        CancellationToken cancellationToken
    );
}
