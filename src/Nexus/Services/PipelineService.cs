// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Nexus.Core.V1;
using Nexus.Utilities;

namespace Nexus.Services;

internal interface IPipelineService
{
    Task<Guid> PutAsync(DataSourcePipeline pipeline);

    Task<DataSourcePipeline?> GetAsync(Guid pipelineId);

    Task<bool> TryUpdateAsync(Guid pipelineId, DataSourcePipeline pipeline);

    Task DeleteAsync(Guid pipelineId);

    Task<IReadOnlyDictionary<Guid, DataSourcePipeline>> GetAllAsync();
}

internal class PipelineService(IDatabaseService databaseService)
    : IPipelineService
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private Dictionary<Guid, DataSourcePipeline>? _cache;

    private readonly IDatabaseService _databaseService = databaseService;

    public Task<Guid> PutAsync(DataSourcePipeline pipeline)
    {
        return InteractWithPipelineMapAsync(pipelineMap =>
        {
            var id = Guid.NewGuid();

            pipelineMap[id] = pipeline;

            return id;
        }, saveChanges: true);
    }

    public Task<DataSourcePipeline?> GetAsync(Guid pipelineId)
    {
        return InteractWithPipelineMapAsync(pipelineMap =>
        {
            pipelineMap.TryGetValue(pipelineId, out var pipeline);
            return pipeline;
        }, saveChanges: false);
    }

    public Task<bool> TryUpdateAsync(Guid pipelineId, DataSourcePipeline pipeline)
    {
        return InteractWithPipelineMapAsync(pipelineMap =>
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

    public Task DeleteAsync(Guid pipelineId)
    {
        return InteractWithPipelineMapAsync<object?>(pipelineMap =>
        {
            pipelineMap.Remove(pipelineId);
            return default;
        }, saveChanges: true);
    }

    public Task<IReadOnlyDictionary<Guid, DataSourcePipeline>> GetAllAsync()
    {
        return InteractWithPipelineMapAsync(
            pipelineMap => (IReadOnlyDictionary<Guid, DataSourcePipeline>)pipelineMap,
            saveChanges: false
        );
    }

    private Dictionary<Guid, DataSourcePipeline> GetPipelineMap()
    {
        if (_cache is null)
        {
            if (_databaseService.TryReadPipelineMap(out var jsonString))
            {
                _cache = JsonSerializer.Deserialize<Dictionary<Guid, DataSourcePipeline>>(jsonString, JsonSerializerOptions.Web)
                    ?? throw new Exception("pipelineMap is null");
            }

            else
            {
                _cache = new();
            }
        }

        return _cache;
    }

    private async Task<T> InteractWithPipelineMapAsync<T>(
        Func<Dictionary<Guid, DataSourcePipeline>, T> func,
        bool saveChanges)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            var pipelineMap = GetPipelineMap();
            var result = func(pipelineMap);

            if (saveChanges)
            {
                using var stream = _databaseService.WritePipelineMap();
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
