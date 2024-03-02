using Nexus.DataModel;
using Nexus.Extensibility;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core
{
    internal record InternalRefreshToken(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("i")] Guid Id,
        [property: JsonPropertyName("t")] string Value)
    {
        internal static string Serialize(InternalRefreshToken token)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(token);
            var base64String = Convert.ToBase64String(bytes);

            return base64String;
        }

        internal static InternalRefreshToken Deserialize(string token)
        {
            var bytes = Convert.FromBase64String(token);
            var internalRefreshToken = JsonSerializer.Deserialize<InternalRefreshToken>(bytes)!;

            return internalRefreshToken;
        }
    }

    internal record struct Interval(
        DateTime Begin,
        DateTime End);

    internal record ReadUnit(
        CatalogItemRequest CatalogItemRequest,
        PipeWriter DataWriter);

    internal record CatalogItemRequest(
        CatalogItem Item,
        CatalogItem? BaseItem,
        CatalogContainer Container);

    internal record NexusProject(
        IReadOnlyDictionary<string, JsonElement>? SystemConfiguration,
        IReadOnlyDictionary<Guid, InternalPackageReference> PackageReferences,
        IReadOnlyDictionary<string, UserConfiguration> UserConfigurations);

    internal record UserConfiguration(
        IReadOnlyDictionary<Guid, InternalDataSourceRegistration> DataSourceRegistrations);

    internal record CatalogState(
        CatalogContainer Root,
        CatalogCache Cache);

    internal record LazyCatalogInfo(
        DateTime Begin,
        DateTime End,
        ResourceCatalog Catalog);

    internal record ExportContext(
        TimeSpan SamplePeriod,
        IEnumerable<CatalogItemRequest> CatalogItemRequests,
        ReadDataHandler ReadDataHandler,
        ExportParameters ExportParameters);

    internal record JobControl(
        DateTime Start,
        Job Job,
        CancellationTokenSource CancellationTokenSource)
    {
        public event EventHandler<double>? ProgressUpdated;
        public event EventHandler? Completed;

        public double Progress { get; private set; }

        public Task<object?> Task { get; set; } = default!;

        public void OnProgressUpdated(double e)
        {
            Progress = e;
            ProgressUpdated?.Invoke(this, e);
        }

        public void OnCompleted()
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
