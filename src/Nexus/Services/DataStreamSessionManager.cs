// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V2;
using Nexus.Extensibility;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace Nexus.Services;

internal interface IDataStreamSessionManager
{
    void Register(DataStreamSession session);

    DataStreamChannelLease? Attach(Guid sessionId, Guid channelId);

    BatchStreamSessionStatus? GetStatus(Guid sessionId);
}

internal sealed class DataStreamSessionManager : IDataStreamSessionManager
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DataStreamSession> _sessions = new();

    public void Register(DataStreamSession session)
    {
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException($"A data stream session with id {session.Id} already exists.");

        session.StartLifetime(Remove, SessionTimeout);
    }

    public DataStreamChannelLease? Attach(Guid sessionId, Guid channelId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return default;

        return session.Attach(channelId);
    }

    public BatchStreamSessionStatus? GetStatus(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return default;

        return session.GetStatus();
    }

    private void Remove(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }
}

internal sealed class DataStreamSession(
    Guid id,
    DateTime begin,
    DateTime end,
    TimeSpan samplePeriod,
    DataReadingGroup[] readingGroups,
    DataStreamChannel[] channels,
    ReadDataHandler readDataHandler,
    IMemoryTracker memoryTracker,
    IProgress<double>? progress,
    ILogger<DataSourceController> logger)
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<Guid> _attachedChannelIds = [];
    private readonly HashSet<Guid> _completedChannelIds = [];
    private Action<Guid>? _remove;
    private bool _isCanceled;
    private bool _isDisposed;
    private bool _controllersDisposed;
    private Task? _readingTask;
    private Guid? _faultedChannelId;
    private string? _faultReason;

    private static readonly TimeSpan StatusGracePeriod = TimeSpan.FromSeconds(30);

    public Guid Id { get; } = id;

    public DataStreamChannel[] Channels { get; } = channels;

    public void StartLifetime(Action<Guid> remove, TimeSpan timeout)
    {
        _remove = remove;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, _cts.Token).ConfigureAwait(false);

                var shouldCancel = false;

                lock (_gate)
                {
                    shouldCancel = _readingTask is null;
                }

                if (shouldCancel)
                {
                    lock (_gate)
                    {
                        if (_faultReason is null)
                        {
                            _faultedChannelId = null;
                            _faultReason = "The session timed out before all channels attached.";
                        }
                    }

                    await CancelAsync().ConfigureAwait(false);
                    await DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The session started or completed before the attach timeout expired.
            }
        });
    }

    public DataStreamChannelLease? Attach(Guid channelId)
    {
        DataStreamChannel? channel;
        var startReading = false;

        lock (_gate)
        {
            if (_isCanceled || _isDisposed)
                return default;

            channel = Channels.FirstOrDefault(current => current.Id == channelId);

            if (channel is null || !_attachedChannelIds.Add(channelId))
                return default;

            startReading = _attachedChannelIds.Count == Channels.Length && _readingTask is null;

            if (startReading)
                _readingTask = ReadAsync();
        }

        return new DataStreamChannelLease(this, channel);
    }

    public async Task CompleteChannelAsync(Guid channelId, bool faulted, Exception? faultException = null)
    {
        var shouldCancel = false;
        var shouldDispose = false;

        lock (_gate)
        {
            if (faulted && _faultReason is null)
            {
                _faultedChannelId = channelId;
                _faultReason = faultException?.Message ?? "The channel failed.";
            }

            _completedChannelIds.Add(channelId);

            if (faulted && !_isCanceled)
                shouldCancel = true;

            shouldDispose = _completedChannelIds.Count == Channels.Length;
        }

        if (shouldCancel)
            await CancelAsync().ConfigureAwait(false);

        if (shouldDispose)
            await DisposeAsync().ConfigureAwait(false);
    }

    public async Task CancelAsync()
    {
        var cancel = false;

        lock (_gate)
        {
            if (!_isCanceled)
            {
                _isCanceled = true;
                cancel = true;
            }
        }

        if (!cancel)
            return;

        await _cts.CancelAsync().ConfigureAwait(false);

        foreach (var channel in Channels)
            channel.Reader.CancelPendingRead();

        foreach (var writer in readingGroups.SelectMany(group => group.CatalogItemRequestPipeWriters))
            writer.DataWriter.CancelPendingFlush();
    }

    public BatchStreamSessionStatus GetStatus()
    {
        lock (_gate)
        {
            var state = _faultReason is not null
                ? BatchStreamSessionState.Faulted
                : _isDisposed
                    ? BatchStreamSessionState.Completed
                    : BatchStreamSessionState.Active;

            var faultedChannelResourcePath = _faultedChannelId is null
                ? null
                : Channels.FirstOrDefault(channel => channel.Id == _faultedChannelId.Value)?.ResourcePath;

            return new BatchStreamSessionStatus(state, _faultedChannelId, faultedChannelResourcePath, _faultReason);
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            await DataSourceController.ReadAsync(
                begin,
                end,
                samplePeriod,
                readingGroups,
                readDataHandler,
                memoryTracker,
                progress,
                logger,
                _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // The session was canceled because a channel aborted or timed out.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch data streaming failed");

            lock (_gate)
            {
                if (_faultReason is null)
                {
                    _faultedChannelId = null;
                    _faultReason = $"The data source read failed: {ex.Message}";
                }
            }

            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            DisposeControllers();
        }
    }

    private async Task DisposeAsync()
    {
        var dispose = false;

        lock (_gate)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                dispose = true;
            }
        }

        if (!dispose)
            return;

        var remove = _remove;
        DisposeControllers();

        foreach (var channel in Channels)
            await channel.Reader.CompleteAsync().ConfigureAwait(false);

        var readingTask = _readingTask;

        if (readingTask is null || readingTask.IsCompleted)
            _cts.Dispose();

        else
            _ = readingTask.ContinueWith(_ => _cts.Dispose(), TaskScheduler.Default);

        if (remove is not null)
            _ = Task.Delay(StatusGracePeriod).ContinueWith(_ => remove(Id), TaskScheduler.Default);
    }

    private void DisposeControllers()
    {
        var dispose = false;

        lock (_gate)
        {
            if (!_controllersDisposed)
            {
                _controllersDisposed = true;
                dispose = true;
            }
        }

        if (!dispose)
            return;

        foreach (var controller in readingGroups.Select(group => group.Controller))
            controller.Dispose();
    }
}

internal sealed record DataStreamChannel(
    Guid Id,
    string ResourcePath,
    PipeReader Reader,
    long ContentLength);

internal sealed record DataStreamChannelLease(
    DataStreamSession Session,
    DataStreamChannel Channel);
