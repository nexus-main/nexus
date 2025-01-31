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
/// A read request.
/// </summary>
/// <param name="OriginalResourceName">The original resource name.</param>
/// <param name="CatalogItem">The <paramref name="CatalogItem"/> to be read.</param>
/// <param name="Data">The data buffer.</param>
/// <param name="Status">The status buffer. A value of 0x01 ('1') indicates that the corresponding value in the data buffer is valid, otherwise it is treated as <see cref="double.NaN"/>.</param>
public record ReadRequest(
    string OriginalResourceName,
    CatalogItem CatalogItem,
    Memory<byte> Data,
    Memory<byte> Status
);

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

    public ReadRequestManager(CatalogItem catalogItem, int elementCount)
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
            statusMemory
        );
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
