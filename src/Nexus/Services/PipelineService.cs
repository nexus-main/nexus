// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Nexus.Core;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IPipelineService
{
    Task<Guid> CreateAsync(
        string userId,
        DataSourceRegistration[] pipeline);

    bool TryGet(
        string userId,
        Guid pieplineId,
        [NotNullWhen(true)] out DataSourceRegistration[]? token);

    Task DeleteAsync(string userId, Guid pipelineId);

    Task<IReadOnlyDictionary<Guid, DataSourceRegistration[]>> GetAllAsync(string userId);
}

internal class PipelineService(IDatabaseService databaseService)
    : IPipelineService
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, DataSourceRegistration[]>> _cache = new();

    private readonly IDatabaseService _databaseService = databaseService;

    public Task<Guid> CreateAsync(
        string userId,
        DataSourceRegistration[] pipeline)
    {
        return InteractWithPipelineMapAsync(userId, pipelineMap =>
        {
            var id = Guid.NewGuid();

            pipelineMap.AddOrUpdate(
                id,
                pipeline,
                (key, _) => pipeline
            );

            return id;
        }, saveChanges: true);
    }

    public bool TryGet(
        string userId,
        Guid pieplineId,
        [NotNullWhen(true)] out DataSourceRegistration[]? pipeline)
    {
        var pipelineMap = GetPipelineMap(userId);

        return pipelineMap.TryGetValue(pieplineId, out pipeline);
    }

    public Task DeleteAsync(string userId, Guid pipelineId)
    {
        return InteractWithPipelineMapAsync<object?>(userId, pipelineMap =>
        {
            var pipelineEntry = pipelineMap
                .FirstOrDefault(entry => entry.Key == pipelineId);

            pipelineMap.TryRemove(pipelineEntry.Key, out _);
            return default;
        }, saveChanges: true);
    }

    public Task<IReadOnlyDictionary<Guid, DataSourceRegistration[]>> GetAllAsync(
        string userId)
    {
        return InteractWithPipelineMapAsync(
            userId,
            pipelineMap => (IReadOnlyDictionary<Guid, DataSourceRegistration[]>)pipelineMap,
            saveChanges: false);
    }

    private ConcurrentDictionary<Guid, DataSourceRegistration[]> GetPipelineMap(
        string userId)
    {
        return _cache.GetOrAdd(
            userId,
            key =>
            {
                if (_databaseService.TryReadPipelineMap(userId, out var jsonString))
                {
                    return JsonSerializer.Deserialize<ConcurrentDictionary<Guid, DataSourceRegistration[]>>(jsonString)
                        ?? throw new Exception("pipelineMap is null");
                }

                else
                {
                    return new ConcurrentDictionary<Guid, DataSourceRegistration[]>();
                }
            });
    }

    private async Task<T> InteractWithPipelineMapAsync<T>(
        string userId,
        Func<ConcurrentDictionary<Guid, DataSourceRegistration[]>, T> func,
        bool saveChanges)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            var pipelineMap = GetPipelineMap(userId);
            var result = func(pipelineMap);

            if (saveChanges)
            {
                using var stream = _databaseService.WritePipelineMap(userId);
                JsonSerializerHelper.SerializeIndented(stream, pipelineMap);
            }

            return result;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}
