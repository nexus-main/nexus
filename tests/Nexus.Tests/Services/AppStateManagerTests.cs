// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement;
using Apollo3zehn.PackageManagement.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using Xunit;

namespace Services;

public class AppStateManagerTests
{
    private readonly AppState _appState = new();
    private readonly Mock<IPackageService> _packages = new();
    private readonly Mock<IExtensionHive<IDataSource>> _sources = new();
    private readonly Mock<IExtensionHive<IDataWriter>> _writers = new();
    private readonly Mock<IUpgradeConfigurationService> _upgrade = new();
    private readonly IProgress<double> _progress = new Progress<double>();
    private readonly IReadOnlyDictionary<Guid, PackageReference> _packageMap = new Dictionary<Guid, PackageReference>();
    private readonly AppStateManager _manager;

    public AppStateManagerTests()
    {
        _packages.Setup(service => service.GetAllAsync()).ReturnsAsync(_packageMap);
        _writers.Setup(hive => hive.GetExtensions()).Returns([]);

        _manager = new AppStateManager(
            _appState, _packages.Object, _upgrade.Object, _sources.Object, _writers.Object,
            Mock.Of<ICatalogManager>(), Mock.Of<IDatabaseService>(), NullLogger<AppStateManager>.Instance);
    }

    [Fact]
    public async Task ConcurrentRefreshesAwaitEveryStageAndCanRefreshAgain()
    {
        var packages = new TaskCompletionSource<IReadOnlyDictionary<Guid, PackageReference>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sources = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var upgrade = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredSources = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredUpgrade = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredWriters = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _packages.Setup(service => service.GetAllAsync()).Returns(packages.Task);
        _sources.Setup(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None))
            .Callback(() => enteredSources.SetResult()).Returns(sources.Task);
        _upgrade.Setup(service => service.UpgradeAsync())
            .Callback(() => enteredUpgrade.SetResult()).Returns(upgrade.Task);
        _writers.Setup(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None))
            .Callback(() => enteredWriters.SetResult()).Returns(writers.Task);

        var first = _manager.RefreshDatabaseAsync(_progress, CancellationToken.None);
        var shared = _appState.ReloadPackagesTask;
        var catalogState = _appState.CatalogState;
        var second = _manager.RefreshDatabaseAsync(_progress, CancellationToken.None);

        Assert.NotNull(shared);
        Assert.Same(shared, _appState.ReloadPackagesTask);
        Assert.Same(catalogState, _appState.CatalogState);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        packages.SetResult(_packageMap);
        await enteredSources.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _upgrade.Verify(service => service.UpgradeAsync(), Times.Never);
        sources.SetResult();
        await enteredUpgrade.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _writers.Verify(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None), Times.Never);
        upgrade.SetResult();
        await enteredWriters.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Null(_appState.DataWriterDescriptions);
        writers.SetResult();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(shared.IsCompletedSuccessfully);
        Assert.Null(_appState.ReloadPackagesTask);
        Assert.NotNull(_appState.DataWriterDescriptions);
        Assert.Empty(_appState.DataWriterDescriptions);
        _packages.Verify(service => service.GetAllAsync(), Times.Once);
        _sources.Verify(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None), Times.Once);
        _upgrade.Verify(service => service.UpgradeAsync(), Times.Once);
        _writers.Verify(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None), Times.Once);

        _sources.Reset();
        _upgrade.Reset();
        _writers.Reset();
        _writers.Setup(hive => hive.GetExtensions()).Returns([]);
        await _manager.RefreshDatabaseAsync(_progress, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotSame(catalogState, _appState.CatalogState);
        Assert.Null(_appState.ReloadPackagesTask);
        _packages.Verify(service => service.GetAllAsync(), Times.Exactly(2));
    }

    [Theory]
    [InlineData("packages")]
    [InlineData("sources")]
    [InlineData("upgrade")]
    [InlineData("writers")]
    [InlineData("descriptions")]
    public async Task FailureReachesAllCallersAndAllowsRetry(string stage)
    {
        var gate = new TaskCompletionSource<IReadOnlyDictionary<Guid, PackageReference>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException(stage);
        _packages.Setup(service => service.GetAllAsync()).Returns(gate.Task);

        if (stage == "sources")
            _sources.Setup(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None)).ThrowsAsync(error);
        if (stage == "upgrade")
            _upgrade.Setup(service => service.UpgradeAsync()).ThrowsAsync(error);
        if (stage == "writers")
            _writers.Setup(hive => hive.LoadPackagesAsync(_packageMap, _progress, CancellationToken.None)).ThrowsAsync(error);
        if (stage == "descriptions")
            _writers.Setup(hive => hive.GetExtensions()).Throws(error);

        var first = _manager.RefreshDatabaseAsync(_progress, CancellationToken.None);
        var second = _manager.RefreshDatabaseAsync(_progress, CancellationToken.None);

        if (stage == "packages")
            gate.SetException(error);
        else
            gate.SetResult(_packageMap);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Null(_appState.ReloadPackagesTask);
        _packages.Verify(service => service.GetAllAsync(), Times.Once);

        _packages.Setup(service => service.GetAllAsync()).ReturnsAsync(_packageMap);
        _sources.Reset();
        _upgrade.Reset();
        _writers.Reset();
        _writers.Setup(hive => hive.GetExtensions()).Returns([]);
        await _manager.RefreshDatabaseAsync(_progress, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(_appState.ReloadPackagesTask);
    }

    [Fact]
    public async Task AlreadyCanceledCallDoesNotStartRefresh()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _manager.RefreshDatabaseAsync(_progress, cancellation.Token));

        Assert.Null(_appState.ReloadPackagesTask);
        _packages.Verify(service => service.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task CancellationReachesAllCallersAndClearsSharedTask()
    {
        using var cancellation = new CancellationTokenSource();
        var enteredSources = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sources.Setup(hive => hive.LoadPackagesAsync(_packageMap, _progress, cancellation.Token))
            .Callback(() => enteredSources.SetResult())
            .Returns(() => Task.Delay(Timeout.Infinite, cancellation.Token));

        var first = _manager.RefreshDatabaseAsync(_progress, cancellation.Token);
        await enteredSources.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = _manager.RefreshDatabaseAsync(_progress, CancellationToken.None);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(_appState.ReloadPackagesTask);
        _upgrade.Verify(service => service.UpgradeAsync(), Times.Never);

        await _manager.RefreshDatabaseAsync(_progress, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(_appState.ReloadPackagesTask);
    }
}
