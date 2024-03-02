using Microsoft.Extensions.Logging;
using Nexus.DataModel;

namespace Nexus.Extensibility
{
    /// <summary>
    /// A simple implementation of a data source.
    /// </summary>
    public abstract class SimpleDataSource : IDataSource
    {
        #region Properties

        /// <summary>
        /// Gets the data source context. This property is not accessible from within class constructors as it will bet set later.
        /// </summary>
        protected DataSourceContext Context { get; private set; } = default!;

        /// <summary>
        /// Gets the data logger. This property is not accessible from within class constructors as it will bet set later.
        /// </summary>
        protected ILogger Logger { get; private set; } = default!;

        #endregion

        #region Methods

        /// <inheritdoc />
        public Task SetContextAsync(
            DataSourceContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            Context = context;
            Logger = logger;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public abstract Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
            string path,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<ResourceCatalog> GetCatalogAsync(
            string catalogId,
            CancellationToken cancellationToken);

        /// <inheritdoc />
        public virtual Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult((DateTime.MinValue, DateTime.MaxValue));
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
            CancellationToken cancellationToken);

        #endregion
    }
}
