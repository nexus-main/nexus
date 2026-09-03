// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using System.Buffers;
using System.Text.Json;

namespace Nexus.Extensibility;

/// <summary>
/// The starter package for a data source.
/// </summary>
/// <param name="ResourceLocator">An optional URL which points to the data.</param>
/// <param name="SourceConfiguration">The source configuration.</param>
/// <param name="RequestConfiguration">The request configuration.</param>
public record DataSourceContext<T>(
    Uri? ResourceLocator,
    T SourceConfiguration,
    IReadOnlyDictionary<string, JsonElement>? RequestConfiguration
);

/// <summary>
/// A catalog time range.
/// </summary>
/// <param name="Begin">The date/time of the first data in the catalog.</param>
/// <param name="End">The date/time of the last data in the catalog.</param>
public record CatalogTimeRange(
    DateTime Begin,
    DateTime End
);

/// <summary>
/// A runtime read request created by Nexus or a compatible host.
/// </summary>
/// <remarks>
/// This type carries runtime buffers and completion callbacks and is not a serialization contract.
/// Use a separate DTO at process or wire boundaries.
/// </remarks>
public sealed class ReadRequest
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _onCompleted;
    private readonly CancellationToken _cancellationToken;
    private Task? _completionTask;

    /// <summary>
    /// Initializes a new runtime read request.
    /// </summary>
    /// <param name="originalResourceName">The original resource name.</param>
    /// <param name="catalogItem">The catalog item to be read.</param>
    /// <param name="data">The data buffer.</param>
    /// <param name="status">The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as <see cref="double.NaN"/>.</param>
    /// <param name="onCompleted">The host callback to run once when the request is completed.</param>
    /// <param name="cancellationToken">The host-owned cancellation token for completion.</param>
    public ReadRequest(
        string originalResourceName,
        CatalogItem catalogItem,
        Memory<byte> data,
        Memory<byte> status,
        Func<CancellationToken, Task> onCompleted,
        CancellationToken cancellationToken)
    {
        OriginalResourceName = originalResourceName;
        CatalogItem = catalogItem;
        Data = data;
        Status = status;
        _onCompleted = onCompleted;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// The original resource name.
    /// </summary>
    public string OriginalResourceName { get; }

    /// <summary>
    /// The catalog item to be read.
    /// </summary>
    public CatalogItem CatalogItem { get; }

    /// <summary>
    /// The data buffer.
    /// </summary>
    public Memory<byte> Data { get; }

    /// <summary>
    /// The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as <see cref="double.NaN"/>.
    /// </summary>
    public Memory<byte> Status { get; }

    /// <summary>
    /// Called by the data source when <see cref="Data"/> and <see cref="Status"/> are fully populated.
    /// The framework flushes the resource to its pipe immediately.
    /// </summary>
    public Task CompleteAsync()
    {
        lock (_gate)
        {
            if (_completionTask is null)
                _completionTask = CompleteCoreAsync();

            return _completionTask;
        }
    }

    private async Task CompleteCoreAsync()
    {
        await _onCompleted(_cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Reads the requested data.
/// </summary>
/// <param name="resourcePath">The path to the resource data to stream.</param>
/// <param name="begin">Start date/time.</param>
/// <param name="end">End date/time.</param>
/// <param name="buffer">The buffer to read to the data into.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns></returns>
public delegate Task ReadDataHandler(
    string resourcePath,
    DateTime begin,
    DateTime end,
    Memory<double> buffer,
    CancellationToken cancellationToken);

internal class ReadRequestManager : IDisposable
{
    private readonly IMemoryOwner<byte> _dataOwner;
    private readonly IMemoryOwner<byte> _statusOwner;

    public ReadRequestManager(
        CatalogItem catalogItem,
        int elementCount,
        Func<CancellationToken, Task>? onCompleted,
        CancellationToken cancellationToken)
    {
        var byteCount = elementCount * catalogItem.Representation.ElementSize;
        var originalResourceName = catalogItem.Resource.Properties!.GetStringValue(DataModelExtensions.OriginalNameKey)!;

        /* data memory */
        var dataOwner = MemoryPool<byte>.Shared.Rent(byteCount);
        var dataMemory = dataOwner.Memory[..byteCount];
        dataMemory.Span.Clear();
        _dataOwner = dataOwner;

        /* status memory */
        var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
        var statusMemory = statusOwner.Memory[..elementCount];
        statusMemory.Span.Clear();
        _statusOwner = statusOwner;

        Request = new ReadRequest(
            originalResourceName,
            catalogItem,
            dataMemory,
            statusMemory,
            onCompleted ?? (_ => Task.CompletedTask),
            cancellationToken);
    }

    public ReadRequest Request { get; }

    #region IDisposable

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _dataOwner.Dispose();
                _statusOwner.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
