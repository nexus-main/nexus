// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics;

namespace Nexus.Core;

[DebuggerDisplay("{Id,nq}")]
internal class CatalogContainer
{
    public const string RootCatalogId = "/";

    internal ILogger? Logger { get; }

    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    private LazyCatalogInfo? _lazyCatalogInfo;

    private CatalogContainer[]? _childCatalogContainers;

    private readonly ICatalogManager _catalogManager;

    private readonly IDatabaseService _databaseService;

    private readonly IDataControllerService _dataControllerService;

    public CatalogContainer(
        CatalogRegistration catalogRegistration,
        Guid pipelineId,
        DataSourcePipeline pipeline,
        Guid[] packageReferenceIds,
        CatalogMetadata metadata,
        ICatalogManager catalogManager,
        IDatabaseService databaseService,
        IDataControllerService dataControllerService,
        string? sourceId = default,
        ILogger? logger = default)
    {
        Logger = logger;

        Id = catalogRegistration.Path;
        Title = catalogRegistration.Title;
        IsTransient = catalogRegistration.IsTransient;
        LinkTarget = catalogRegistration.LinkTarget;
        Begin = NormalizeDateTime(catalogRegistration.Begin);
        End = NormalizeDateTime(catalogRegistration.End);
        SourceId = sourceId ?? catalogRegistration.Path;
        PipelineId = pipelineId;
        Pipeline = pipeline;
        PackageReferenceIds = packageReferenceIds;
        Metadata = metadata;

        if (Begin > End)
        {
            logger?.LogWarning(
                "Catalog registration {CatalogId} has begin {Begin} after end {End}; it will expose an empty effective time range.",
                Id,
                Begin,
                End);
        }

        _catalogManager = catalogManager;
        _databaseService = databaseService;
        _dataControllerService = dataControllerService;
    }

    public string Id { get; }

    public string? Title { get; }

    public bool IsTransient { get; }

    public string? LinkTarget { get; }

    public DateTime? Begin { get; }

    public DateTime? End { get; }

    public string SourceId { get; }

    public string PhysicalName => Id.TrimStart('/').Replace('/', '_');

    public Guid PipelineId { get; }

    public DataSourcePipeline Pipeline { get; }

    public Guid[] PackageReferenceIds { get; }

    public CatalogMetadata Metadata { get; internal set; }

    public static CatalogContainer CreateRoot(ICatalogManager catalogManager, IDatabaseService databaseService)
    {
        return new CatalogContainer(
            new CatalogRegistration(RootCatalogId, string.Empty),
            default!,
            default!,
            default!,
            default!,
            catalogManager,
            databaseService, default!);
    }

    public CatalogContainer CreateLinkView(CatalogContainer target, bool applyAliasRange = true)
    {
        var begin = applyAliasRange
            ? Begin is null || (target.Begin is not null && target.Begin.Value > Begin.Value)
                ? target.Begin
                : Begin
            : target.Begin;

        var end = applyAliasRange
            ? End is null || (target.End is not null && target.End.Value < End.Value)
                ? target.End
                : End
            : target.End;

        return new CatalogContainer(
            new CatalogRegistration(Id, target.Title, target.IsTransient, default, begin, end),
            target.PipelineId,
            target.Pipeline,
            target.PackageReferenceIds,
            target.Metadata,
            _catalogManager,
            _databaseService,
            _dataControllerService,
            target.SourceId,
            Logger);
    }

    public async Task<CatalogTimeRange> GetTimeRangeAsync(CancellationToken cancellationToken)
    {
        using var controller = await _dataControllerService.GetDataSourceControllerAsync(Pipeline, cancellationToken);
        return ApplyTimeRangeLimit(await controller.GetTimeRangeAsync(SourceId, cancellationToken));
    }

    public CatalogTimeRange ApplyTimeRangeLimit(CatalogTimeRange timeRange)
    {
        var begin = Begin is null || Begin.Value < timeRange.Begin
            ? timeRange.Begin
            : Begin.Value;

        var end = End is null || End.Value > timeRange.End
            ? timeRange.End
            : End.Value;

        if (end < begin)
            end = begin;

        return new CatalogTimeRange(begin, end);
    }

    public bool Contains(DateTime begin, DateTime end)
    {
        return (Begin is null || begin >= Begin.Value) &&
            (End is null || end <= End.Value);
    }

    public bool TryGetContainedRange(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        out DateTime containedBegin,
        out DateTime containedEnd)
    {
        containedBegin = Begin is null || Begin.Value < begin
            ? begin
            : Begin.Value.RoundUp(samplePeriod);

        containedEnd = End is null || End.Value > end
            ? end
            : End.Value.RoundDown(samplePeriod);

        return containedBegin < containedEnd;
    }

    public async Task<IEnumerable<CatalogContainer>> GetChildCatalogContainersAsync(
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (IsTransient || _childCatalogContainers is null)
                _childCatalogContainers = await _catalogManager.GetCatalogContainersAsync(this, cancellationToken);

            return _childCatalogContainers;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // TODO: Use Lazy instead?
    public async Task<LazyCatalogInfo> GetLazyCatalogInfoAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureLazyCatalogInfoAsync(cancellationToken);

            var lazyCatalogInfo = _lazyCatalogInfo
                ?? throw new Exception("this should never happen");
            return lazyCatalogInfo;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdateMetadataAsync(CatalogMetadata metadata)
    {
        if (SourceId != Id)
            throw new InvalidOperationException("Alias catalogs are read-only views and cannot be modified.");

        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            // persist
            using var stream = _databaseService.WriteCatalogMetadata(SourceId);
            await JsonSerializerHelper.SerializeIndentedAsync(stream, metadata);

            // assign
            Metadata = metadata;

            // trigger merging of catalog and catalog overrides
            _lazyCatalogInfo = default;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureLazyCatalogInfoAsync(CancellationToken cancellationToken)
    {
        if (IsTransient || _lazyCatalogInfo is null)
        {
            var catalogBegin = default(DateTime);
            var catalogEnd = default(DateTime);

            using var controller = await _dataControllerService.GetDataSourceControllerAsync(Pipeline, cancellationToken);
            var catalog = await controller.GetCatalogAsync(SourceId, cancellationToken);

            if (SourceId != Id)
                catalog = catalog with { Id = Id };

            // get begin and end of project
            var catalogTimeRange = ApplyTimeRangeLimit(await controller.GetTimeRangeAsync(SourceId, cancellationToken));

            // merge time range
            if (catalogBegin == DateTime.MinValue)
                catalogBegin = catalogTimeRange.Begin;

            else
                catalogBegin = new DateTime(Math.Min(catalogBegin.Ticks, catalogTimeRange.Begin.Ticks), DateTimeKind.Utc);

            if (catalogEnd == DateTime.MinValue)
                catalogEnd = catalogTimeRange.End;

            else
                catalogEnd = new DateTime(Math.Max(catalogEnd.Ticks, catalogTimeRange.End.Ticks), DateTimeKind.Utc);

            // merge catalog
            if (Metadata?.Overrides is not null)
                catalog = catalog.Merge(Metadata.Overrides);

            //
            _lazyCatalogInfo = new LazyCatalogInfo(catalogBegin, catalogEnd, catalog);
        }
    }

    private static DateTime? NormalizeDateTime(DateTime? dateTime)
    {
        if (dateTime is null)
            return null;

        return dateTime.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc)
            : dateTime.Value.ToUniversalTime();
    }
}
