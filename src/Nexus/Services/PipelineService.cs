// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Nexus.Core.V1;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IPipelineService
{
    Task<Guid> PutAsync(string userId, DataSourcePipeline pipeline);

    public Task<DataSourcePipeline?> GetAsync(string userId, Guid pipelineId);

    Task<bool> TryUpdateAsync(string userId, Guid pipelineId, DataSourcePipeline pipeline);

    Task DeleteAsync(string userId, Guid pipelineId);

    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, DataSourcePipeline>>> GetAllAsync();

    Task<IReadOnlyDictionary<Guid, DataSourcePipeline>> GetAllForUserAsync(string userId);
}

internal class PipelineService(IDatabaseService databaseService)
    : IPipelineService
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly Dictionary<string, Dictionary<Guid, DataSourcePipeline>> _cache = new();

    private readonly IDatabaseService _databaseService = databaseService;

    public Task<Guid> PutAsync(string userId, DataSourcePipeline pipeline)
    {
        return InteractWithPipelineMapAsync(userId, pipelineMap =>
        {
            var id = Guid.NewGuid();

            pipelineMap[id] = pipeline;

            return id;
        }, saveChanges: true);
    }

    public Task<DataSourcePipeline?> GetAsync(string userId, Guid pipelineId)
    {
        return InteractWithPipelineMapAsync(userId, pipelineMap =>
        {
            pipelineMap.TryGetValue(pipelineId, out var pipeline);
            return pipeline;
        }, saveChanges: false);
    }

    public Task<bool> TryUpdateAsync(string userId, Guid pipelineId, DataSourcePipeline pipeline)
    {
        return InteractWithPipelineMapAsync(userId, pipelineMap =>
        {
            /* Proceed only if pipeline already exists! 
             * We do not want pipeline IDs being set from
             * outside.
             */
            if (pipelineMap.ContainsKey(pipelineId))
            {
                pipelineMap[pipelineId] = pipeline;
                return true;
            }

            else
            {
                return false;
            }
        }, saveChanges: true);
    }

    public Task DeleteAsync(string userId, Guid pipelineId)
    {
        return InteractWithPipelineMapAsync<object?>(userId, pipelineMap =>
        {
            var pipelineEntry = pipelineMap
                .FirstOrDefault(entry => entry.Key == pipelineId);

            pipelineMap.Remove(pipelineEntry.Key);

            return default;
        }, saveChanges: true);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, DataSourcePipeline>>> GetAllAsync()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<Guid, DataSourcePipeline>>();

        foreach (var userId in _databaseService.EnumerateUsers())
        {
            var pipelines = await GetAllForUserAsync(userId);
            result[userId] = pipelines;
        }

        return result;
    }

    public Task<IReadOnlyDictionary<Guid, DataSourcePipeline>> GetAllForUserAsync(
        string userId)
    {
        return InteractWithPipelineMapAsync(
            userId,
            pipelineMap => (IReadOnlyDictionary<Guid, DataSourcePipeline>)pipelineMap,
            saveChanges: false
        );
    }

    private Dictionary<Guid, DataSourcePipeline> GetPipelineMap(
        string userId)
    {
        if (!_cache.TryGetValue(userId, out var pipelineMap))
        {
            if (_databaseService.TryReadPipelineMap(userId, out var jsonString))
            {
                pipelineMap = JsonSerializer.Deserialize<Dictionary<Guid, DataSourcePipeline>>(jsonString, JsonSerializerOptions.Web)
                    ?? throw new Exception("pipelineMap is null");
            }

            else
            {
                pipelineMap = new();
            }
        }

        _cache[userId] = pipelineMap;

        return pipelineMap;
    }

    private async Task<T> InteractWithPipelineMapAsync<T>(
        string userId,
        Func<Dictionary<Guid, DataSourcePipeline>, T> func,
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
