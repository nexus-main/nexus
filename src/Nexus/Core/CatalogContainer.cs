// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics;

namespace Nexus.Core;

internal readonly record struct ContainedTimeRange(DateTime Begin, DateTime End);

internal readonly record struct ContainedBucketRange(
    int FirstIndex,
    int Count,
    DateTime Begin,
    DateTime End);

[DebuggerDisplay("{Id,nq}")]
internal class CatalogContainer
{
    public const string RootCatalogId = "/";

    internal ILogger? Logger { get; }

    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);

    private ResourceCatalog? _catalog;

    private CatalogContainer[]? _childCatalogContainers;

    private CatalogContainer? _resolvedLinkTarget;

    private readonly DateTime? _minBegin;

    private readonly DateTime? _maxEnd;

    private readonly string _backingSourceId;

    private readonly Guid _pipelineId;

    private readonly DataSourcePipeline _pipeline;

    private readonly Guid[] _packageReferenceIds;

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
        string? backingSourceId = default,
        ILogger? logger = default)
    {
        Logger = logger;

        Id = catalogRegistration.Path;
        Title = catalogRegistration.Title;
        IsTransient = catalogRegistration.IsTransient;
        LinkTarget = catalogRegistration.LinkTarget;
        _minBegin = NexusUtilities.NormalizeToUtc(catalogRegistration.MinBegin);
        _maxEnd = NexusUtilities.NormalizeToUtc(catalogRegistration.MaxEnd);
        _backingSourceId = backingSourceId ?? catalogRegistration.Path;
        _pipelineId = pipelineId;
        _pipeline = pipeline;
        _packageReferenceIds = packageReferenceIds;
        Metadata = metadata;

        if (_minBegin > _maxEnd)
        {
            logger?.LogWarning(
                "Catalog registration {CatalogId} has begin {Begin} after end {End}; it will expose an empty effective time range.",
                Id,
                _minBegin,
                _maxEnd);
        }

        _catalogManager = catalogManager;
        _databaseService = databaseService;
        _dataControllerService = dataControllerService;
    }

    public string Id { get; }

    public string? Title { get; }

    public bool IsTransient { get; }

    public DateTime? MinBegin
    {
        get
        {
            if (_resolvedLinkTarget is null)
                return _minBegin;

            return _minBegin is null || (_resolvedLinkTarget.MinBegin is not null && _resolvedLinkTarget.MinBegin.Value > _minBegin.Value)
                ? _resolvedLinkTarget.MinBegin
                : _minBegin;
        }
    }

    public DateTime? MaxEnd
    {
        get
        {
            if (_resolvedLinkTarget is null)
                return _maxEnd;

            return _maxEnd is null || (_resolvedLinkTarget.MaxEnd is not null && _resolvedLinkTarget.MaxEnd.Value < _maxEnd.Value)
                ? _resolvedLinkTarget.MaxEnd
                : _maxEnd;
        }
    }

    /// <summary>
    /// Gets the logical catalog id this catalog directly aliases, or <see langword="null" /> when this registration is not a direct alias.
    /// </summary>
    public string? LinkTarget { get; }

    /// <summary>
    /// Gets whether alias resolution has already populated the runtime target container for this direct alias.
    /// </summary>
    public bool HasResolvedLinkTarget => _resolvedLinkTarget is not null;

    /// <summary>
    /// Gets the physical/source catalog id to use for data-source calls, after collapsing any resolved alias chain.
    /// </summary>
    public string BackingSourceId
    {
        get
        {
            // For aliases, defer to the resolved target so alias chains collapse to the
            // final physical source. An unresolved alias must not silently fall back to
            // its own id, because that would make it behave like an independent source.
            if (_resolvedLinkTarget is not null)
                return _resolvedLinkTarget.BackingSourceId;

            if (LinkTarget is not null)
                throw new InvalidOperationException($"Catalog alias {Id} has not been resolved.");

            return _backingSourceId;
        }
    }

    /// <summary>
    /// Gets whether this catalog is a read-only alias view, either because it is a direct alias or because it is projected below one.
    /// </summary>
    /// <remarks>
    /// Projected children do not have <see cref="LinkTarget" />, but their public <see cref="Id" /> differs from the backing source id.
    /// </remarks>
    public bool IsAliasView => LinkTarget is not null || _backingSourceId != Id;

    public string PhysicalName => Id.TrimStart('/').Replace('/', '_');

    public Guid PipelineId => _resolvedLinkTarget?.PipelineId ?? _pipelineId;

    public DataSourcePipeline Pipeline => _resolvedLinkTarget?.Pipeline ?? _pipeline;

    public Guid[] PackageReferenceIds => _resolvedLinkTarget?.PackageReferenceIds ?? _packageReferenceIds;

    public CatalogMetadata Metadata { get; internal set; }

    internal bool ShouldResolveLinkTarget =>
        LinkTarget is not null &&
        (_resolvedLinkTarget is null || IsTransient || _resolvedLinkTarget.IsTransient);

    public static CatalogContainer CreateRoot(ICatalogManager catalogManager, IDatabaseService databaseService)
    {
        return new CatalogContainer(
            new CatalogRegistration(RootCatalogId, string.Empty),
            default!,
            default!,
            default!,
            default!,
            catalogManager,
            databaseService,
            default!);
    }

    internal void ResolveLinkTarget(CatalogContainer target)
    {
        _resolvedLinkTarget = target;
    }

    public async Task<CatalogTimeRange> GetTimeRangeAsync(CancellationToken cancellationToken)
    {
        using var controller = await _dataControllerService.GetDataSourceControllerAsync(Pipeline, cancellationToken);
        var timeRange = await controller.GetTimeRangeAsync(BackingSourceId, cancellationToken);

        var begin = MinBegin is null || MinBegin.Value < timeRange.Begin
            ? timeRange.Begin
            : MinBegin.Value;

        var end = MaxEnd is null || MaxEnd.Value > timeRange.End
            ? timeRange.End
            : MaxEnd.Value;

        if (end < begin)
            end = begin;

        return new CatalogTimeRange(begin, end);
    }

    public async Task<CatalogAvailability> GetAvailabilityAsync(
        DateTime begin,
        DateTime end,
        TimeSpan step,
        CancellationToken cancellationToken)
    {
        using var controller = await _dataControllerService.GetDataSourceControllerAsync(Pipeline, cancellationToken);

        if (MinBegin is null && MaxEnd is null)
            return await controller.GetAvailabilityAsync(BackingSourceId, begin, end, step, cancellationToken);

        var stepCount = (int)((end - begin).Ticks / step.Ticks);
        var data = new double[stepCount];

        if (!TryGetContainedBucketRange(begin, end, step, out var range))
            return new CatalogAvailability(data);

        var availability = await controller.GetAvailabilityAsync(BackingSourceId, range.Begin, range.End, step, cancellationToken);
        Array.Copy(availability.Data, 0, data, range.FirstIndex, Math.Min(availability.Data.Length, range.Count));

        return new CatalogAvailability(data);
    }

    public bool Contains(DateTime begin, DateTime end)
    {
        return (MinBegin is null || begin >= MinBegin.Value) &&
            (MaxEnd is null || end <= MaxEnd.Value);
    }

    /// <summary>
    /// Gets the sample-aligned portion of the requested range that is visible through this catalog container.
    /// </summary>
    /// <param name="begin">The requested range begin.</param>
    /// <param name="end">The requested range end.</param>
    /// <param name="samplePeriod">The sample period used to align the contained range.</param>
    /// <param name="range">The contained, sample-aligned range when this method returns <see langword="true" />.</param>
    /// <returns><see langword="true" /> when the requested range contains at least one visible, sample-aligned sample; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// When constrained by <see cref="MinBegin" />, the returned begin is rounded up to <paramref name="samplePeriod" />.
    /// When constrained by <see cref="MaxEnd" />, the returned end is rounded down to <paramref name="samplePeriod" />.
    /// Use this for data reads, where callers need a continuous range that can be read from the backing source.
    /// </remarks>
    public bool TryGetSampleAlignedContainedRange(
        DateTime begin,
        DateTime end,
        TimeSpan samplePeriod,
        out ContainedTimeRange range)
    {
        var containedBegin = MinBegin is null || MinBegin.Value < begin
            ? begin
            : MinBegin.Value.RoundUp(samplePeriod);

        var containedEnd = MaxEnd is null || MaxEnd.Value > end
            ? end
            : MaxEnd.Value.RoundDown(samplePeriod);

        range = new ContainedTimeRange(containedBegin, containedEnd);
        return containedBegin < containedEnd;
    }

    /// <summary>
    /// Gets the contiguous sequence of request-aligned buckets that is fully visible through this catalog container.
    /// </summary>
    /// <param name="begin">The requested range begin that defines bucket index zero.</param>
    /// <param name="end">The requested range end.</param>
    /// <param name="step">The bucket width.</param>
    /// <param name="range">The contained bucket range when this method returns <see langword="true" />.</param>
    /// <returns><see langword="true" /> when at least one full request-aligned bucket is visible; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Buckets are included only when the complete bucket interval is contained by <see cref="MinBegin" /> and <see cref="MaxEnd" />.
    /// Use this for availability, where values must be copied back into the original request-aligned result array.
    /// </remarks>
    public bool TryGetContainedBucketRange(
        DateTime begin,
        DateTime end,
        TimeSpan step,
        out ContainedBucketRange range)
    {
        var stepCount = (int)((end - begin).Ticks / step.Ticks);

        bool IsContained(int index)
        {
            var bucketBegin = begin + step * index;
            return Contains(bucketBegin, bucketBegin + step);
        }

        // Find the first fully contained bucket.
        var first = 0;

        while (first < stepCount && !IsContained(first))
            first++;

        if (first == stepCount)
        {
            range = default;
            return false;
        }

        // Find the exclusive end of the contiguous contained bucket run.
        var endIndex = first;

        while (endIndex < stepCount && IsContained(endIndex))
            endIndex++;

        var count = endIndex - first;
        var sourceBegin = begin + step * first;
        var sourceEnd = sourceBegin + step * count;

        range = new ContainedBucketRange(first, count, sourceBegin, sourceEnd);
        return true;
    }

    internal async Task<IEnumerable<CatalogContainer>> GetRegisteredChildCatalogContainersAsync(
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

    public async Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureCatalogAsync(cancellationToken);

            return _catalog!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UpdateMetadataAsync(CatalogMetadata metadata)
    {
        if (IsAliasView)
            throw new InvalidOperationException("Alias catalogs are read-only views and cannot be modified.");

        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            // persist
            using var stream = _databaseService.WriteCatalogMetadata(BackingSourceId);
            await JsonSerializerHelper.SerializeIndentedAsync(stream, metadata);

            // assign
            Metadata = metadata;

            // trigger merging of catalog and catalog overrides
            _catalog = default;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureCatalogAsync(CancellationToken cancellationToken)
    {
        if (IsTransient || _catalog is null)
        {
            using var controller = await _dataControllerService.GetDataSourceControllerAsync(Pipeline, cancellationToken);
            var catalog = await controller.GetCatalogAsync(BackingSourceId, cancellationToken);

            if (BackingSourceId != Id)
                catalog = catalog with { Id = Id };

            if (Metadata?.Overrides is not null)
                catalog = catalog.Merge(Metadata.Overrides);

            _catalog = catalog;
        }
    }
}
