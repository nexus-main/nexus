// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;
using Xunit;

namespace Extensibility;

public class ReadRequestTests
{
    [Fact]
    public void SupportsFourPositionalComponents()
    {
        var item = new CatalogItem(new ResourceCatalog("/A"), new Resource("B"), new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1)), null);
        var request = new ReadRequest("B", item, Memory<byte>.Empty, Memory<byte>.Empty);

        var (name, actualItem, data, status) = request;

        Assert.Equal("B", name);
        Assert.Same(item, actualItem);
        Assert.True(data.IsEmpty);
        Assert.True(status.IsEmpty);
    }

    [Fact]
    public async Task ConcurrentCompletionRunsOnce()
    {
        var callCount = 0;
        var request = CreateRequest(_ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => request.CompleteAsync()));

        Assert.True(request.IsCompleted);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task FailedCompletionIsNotRetried()
    {
        var callCount = 0;
        var request = CreateRequest(_ =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
                throw new InvalidOperationException("failed");

            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(request.CompleteAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(request.CompleteAsync);
        Assert.False(request.IsCompleted);
        Assert.Equal(1, callCount);
    }

    private static ReadRequest CreateRequest(Func<CancellationToken, Task>? onCompleted = null)
    {
        var item = new CatalogItem(new ResourceCatalog("/A"), new Resource("B"), new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1)), null);
        var request = new ReadRequest("B", item, Memory<byte>.Empty, Memory<byte>.Empty);
        request.ConfigureCompletion(onCompleted, CancellationToken.None);
        return request;
    }
}
