// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V2;
using Nexus.Core;
using Nexus.Extensibility;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Services;

internal interface IDataStreamSessionManager
{
    void Register(DataStreamSession session);

    DataStreamChannelLease? Attach(Guid sessionId, Guid channelId, ClaimsPrincipal principal);

    BatchStreamSessionStatus? GetStatus(Guid sessionId, ClaimsPrincipal principal);
}

internal sealed class DataStreamSessionManager(
    TimeProvider? timeProvider = null,
    TimeSpan? sessionTimeout = null,
    TimeSpan? statusGracePeriod = null) : IDataStreamSessionManager
{
    private readonly ConcurrentDictionary<Guid, DataStreamSession> _sessions = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _sessionTimeout = sessionTimeout ?? TimeSpan.FromMinutes(1);
    private readonly TimeSpan _statusGracePeriod = statusGracePeriod ?? TimeSpan.FromSeconds(30);

    public void Register(DataStreamSession session)
    {
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException($"A data stream session with id {session.Id} already exists.");

        session.StartLifetime(Remove, _timeProvider, _sessionTimeout, _statusGracePeriod);
    }

    public DataStreamChannelLease? Attach(Guid sessionId, Guid channelId, ClaimsPrincipal principal)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || !session.IsOwner(principal))
            return default;

        return session.Attach(channelId);
    }

    public BatchStreamSessionStatus? GetStatus(Guid sessionId, ClaimsPrincipal principal)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || !session.IsOwner(principal))
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
    string ownerSubject,
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
    private Task? _readingTask;
    private Task? _disposeTask;
    private Guid? _faultedChannelId;
    private string? _faultReason;
    private TimeProvider _timeProvider = TimeProvider.System;
    private TimeSpan _statusGracePeriod = TimeSpan.FromSeconds(30);

    public Guid Id { get; } = id;

    public DataStreamChannel[] Channels { get; } = channels;

    public bool IsOwner(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(Claims.Subject);
        return !string.IsNullOrWhiteSpace(subject) &&
            string.Equals(ownerSubject, subject.Trim().Normalize(), StringComparison.Ordinal);
    }

    public void StartLifetime(
        Action<Guid> remove,
        TimeProvider timeProvider,
        TimeSpan timeout,
        TimeSpan statusGracePeriod)
    {
        _remove = remove;
        _timeProvider = timeProvider;
        _statusGracePeriod = statusGracePeriod;

        _ = ExpireAsync();

        async Task ExpireAsync()
        {
            await Task.Delay(timeout, timeProvider).ConfigureAwait(false);

            var shouldExpire = false;

            lock (_gate)
            {
                if (_readingTask is null && !_isDisposed)
                {
                    _isCanceled = true;
                    _faultedChannelId = null;
                    _faultReason ??= "The session timed out before all channels attached.";
                    shouldExpire = true;
                }
            }

            if (shouldExpire)
            {
                try
                {
                    await CancelCoreAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Canceling timed-out batch data streaming failed");
                }

                try
                {
                    await DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Disposing timed-out batch data streaming failed");
                }
            }
        }
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

            shouldDispose = faulted || _completedChannelIds.Count == Channels.Length;
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

        await CancelCoreAsync().ConfigureAwait(false);
    }

    private async Task CancelCoreAsync()
    {
        List<Exception>? exceptions = null;

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (exceptions ??= []).Add(ex);
        }

        foreach (var channel in Channels)
        {
            try
            {
                channel.Reader.CancelPendingRead();
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }
        }

        foreach (var writer in readingGroups.SelectMany(group => group.CatalogItemRequestPipeWriters))
        {
            try
            {
                writer.DataWriter.CancelPendingFlush();
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }
        }

        ThrowIfAny(exceptions);
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
        var completedSuccessfully = false;
        Exception? sourceException = null;

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
                _cts.Token,
                DataSourceErrorHandling.Propagate).ConfigureAwait(false);

            completedSuccessfully = true;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // The session was canceled because a channel aborted or timed out.
        }
        catch (Exception ex)
        {
            sourceException = ex;
            logger.LogError(ex, "Batch data streaming failed");

            lock (_gate)
            {
                if (_faultReason is null)
                {
                    _faultedChannelId = null;
                    _faultReason = $"The data source read failed: {ex.Message}";
                }
            }

        }
        finally
        {
            if (!completedSuccessfully)
            {
                foreach (var writer in readingGroups.SelectMany(group => group.CatalogItemRequestPipeWriters))
                {
                    try
                    {
                        await writer.DataWriter.CompleteAsync(sourceException).ConfigureAwait(false);
                    }
                    catch (Exception completionException)
                    {
                        logger.LogError(completionException, "Completing a batch data pipe failed");
                    }
                }
            }
        }
    }

    private Task DisposeAsync()
    {
        TaskCompletionSource? completion = null;

        lock (_gate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _isDisposed = true;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(completion);
        return completion.Task;
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        List<Exception>? exceptions = null;

        try
        {
            var readingTask = _readingTask;

            if (readingTask is not null)
            {
                try
                {
                    await readingTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (exceptions ??= []).Add(ex);
                }
            }

            foreach (var controller in readingGroups.Select(group => group.Controller))
            {
                try
                {
                    controller.Dispose();
                }
                catch (Exception ex)
                {
                    (exceptions ??= []).Add(ex);
                }
            }

            if (readingTask is null)
            {
                foreach (var writer in readingGroups.SelectMany(group => group.CatalogItemRequestPipeWriters))
                {
                    try
                    {
                        await writer.DataWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        (exceptions ??= []).Add(ex);
                    }
                }
            }

            foreach (var channel in Channels)
            {
                try
                {
                    await channel.Reader.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (exceptions ??= []).Add(ex);
                }
            }

            try
            {
                _cts.Dispose();
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }

            var remove = _remove;
            if (remove is not null)
                _ = RemoveAfterGracePeriodAsync(remove);

            if (exceptions is null)
                completion.SetResult();
            else if (exceptions.Count == 1)
                completion.SetException(exceptions[0]);
            else
                completion.SetException(new AggregateException(exceptions));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        async Task RemoveAfterGracePeriodAsync(Action<Guid> removeSession)
        {
            try
            {
                await Task.Delay(_statusGracePeriod, _timeProvider).ConfigureAwait(false);
                removeSession(Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Removing batch data streaming session failed");
            }
        }
    }

    private static void ThrowIfAny(List<Exception>? exceptions)
    {
        if (exceptions is null)
            return;

        if (exceptions.Count == 1)
            throw exceptions[0];

        throw new AggregateException(exceptions);
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
