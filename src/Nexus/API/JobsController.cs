// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to jobs.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class JobsController(
    AppStateManager appStateManager,
    IJobService jobService,
    IServiceProvider serviceProvider,
    Serilog.IDiagnosticContext diagnosticContext,
    ILogger<JobsController> logger) : ControllerBase
{
    // GET      /jobs
    // DELETE   /jobs{jobId}
    // GET      /jobs{jobId}/status
    // POST     /jobs/export
    // POST     /jobs/load-packages
    // POST     /jobs/clear-cache

    private readonly AppStateManager _appStateManager = appStateManager;
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly Serilog.IDiagnosticContext _diagnosticContext = diagnosticContext;
    private readonly IJobService _jobService = jobService;

    #region Jobs Management

    /// <summary>
    /// Gets a list of jobs.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public ActionResult<List<Job>> GetJobs()
    {
        var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
        var username = (User.Identity?.Name) ?? throw new Exception("This should never happen.");
        var result = _jobService
            .GetJobs()
            .Select(jobControl => jobControl.Job)
            .Where(job => job.Owner == username || isAdmin)
            .ToList();

        return result;
    }

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    [HttpDelete("{jobId}")]
    public ActionResult CancelJob(Guid jobId)
    {
        if (_jobService.TryGetJob(jobId, out var jobControl))
        {
            var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
            var username = (User.Identity?.Name) ?? throw new Exception("This should never happen.");
            if (jobControl.Job.Owner == username || isAdmin)
            {
                jobControl.CancellationTokenSource.Cancel();
                return Accepted();
            }

            else
            {
                return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to cancel the job {jobControl.Job.Id}.");
            }
        }

        else
        {
            return NotFound(jobId);
        }
    }

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <returns></returns>
    [HttpGet("{jobId}/status")]
    public async Task<ActionResult<JobStatus>> GetJobStatusAsync(Guid jobId)
    {
        if (_jobService.TryGetJob(jobId, out var jobControl))
        {
            var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
            var username = (User.Identity?.Name) ?? throw new Exception("This should never happen.");

            if (jobControl.Job.Owner == username || isAdmin)
            {
                var status = new JobStatus(
                    Start: jobControl.Start,
                    Progress: jobControl.Progress,
                    Status: jobControl.Task.Status,
                    ExceptionMessage: jobControl.Task.Exception is not null
                        ? jobControl.Task.Exception.Message
                        : default,
                    Result: jobControl.Task.Status == TaskStatus.RanToCompletion && (await jobControl.Task) is not null
                        ? await jobControl.Task
                        : default);

                return status;
            }
            else
            {
                return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to access the status of job {jobControl.Job.Id}.");
            }
        }
        else
        {
            return NotFound(jobId);
        }
    }

    #endregion

    #region Jobs

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="parameters">Export parameters.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    /// <returns></returns>
    [HttpPost("export")]
    public async Task<ActionResult<Job>> ExportAsync(
        ExportParameters parameters,
        CancellationToken cancellationToken)
    {
        _diagnosticContext.Set("Body", JsonSerializerHelper.SerializeIndented(parameters));

        parameters = parameters with
        {
            Begin = parameters.Begin.ToUniversalTime(),
            End = parameters.End.ToUniversalTime()
        };

        var root = _appStateManager.AppState.CatalogState.Root;

        // check that there is anything to export
        if (!parameters.ResourcePaths.Any())
            return BadRequest("The list of resource paths is empty.");

        // translate resource paths to catalog item requests
        CatalogItemRequest[] catalogItemRequests;

        try
        {
            catalogItemRequests = await Task.WhenAll(parameters.ResourcePaths.Select(async resourcePath =>
            {
                var catalogItemRequest = await root.TryFindAsync(resourcePath, cancellationToken) ?? throw new ValidationException($"Could not find resource path {resourcePath}.");
                return catalogItemRequest;
            }));
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }

        // authorize
        try
        {
            foreach (var group in catalogItemRequests.GroupBy(current => current.Container.Id))
            {
                var catalogContainer = group.First().Container;

                if (!AuthUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
                    throw new UnauthorizedAccessException($"The current user is not permitted to access catalog {catalogContainer.Id}.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }

        //
        var username = User.Identity?.Name!;
        var job = new Job(Guid.NewGuid(), "export", username, parameters);
        var dataService = _serviceProvider.GetRequiredService<IDataService>();

        try
        {
            var jobControl = _jobService.AddJob(job, dataService.WriteProgress, async (jobControl, cts) =>
            {
                try
                {
                    var result = await dataService.ExportAsync(job.Id, catalogItemRequests, dataService.ReadAsDoubleAsync, parameters, cts.Token);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to export the requested data.");
                    throw;
                }
            });

            return Accepted(GetAcceptUrl(job.Id), job);
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }

    /// <summary>
    /// Creates a new job which reloads all extensions and resets the resource catalog.
    /// </summary>
    [HttpPost("refresh-database")]
    public ActionResult<Job> RefreshDatabase()
    {
        var username = User.Identity?.Name!;

        var job = new Job(Guid.NewGuid(), "refresh-database", username, default);
        var progress = new Progress<double>();

        var jobControl = _jobService.AddJob(job, progress, async (jobControl, cts) =>
        {
            try
            {
                await _appStateManager.RefreshDatabaseAsync(progress, cts.Token);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to reload extensions and reset the resource catalog.");
                throw;
            }
        });

        var response = (ActionResult<Job>)Accepted(GetAcceptUrl(job.Id), job);
        return response;
    }

    /// <summary>
    /// Clears the aggregation data cache for the specified period of time.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpPost("clear-cache")]
    public async Task<ActionResult<Job>> ClearCacheAsync(
        [BindRequired] string catalogId,
        [BindRequired] DateTime begin,
        [BindRequired] DateTime end,
        CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name!;
        var job = new Job(Guid.NewGuid(), "clear-cache", username, default);

        var response = await ProtectCatalogNonGenericAsync(catalogId, catalogContainer =>
        {
            var progress = new Progress<double>();
            var cacheService = _serviceProvider.GetRequiredService<ICacheService>();

            var jobControl = _jobService.AddJob(job, progress, async (jobControl, cts) =>
            {
                try
                {
                    await cacheService.ClearAsync(catalogId, begin, end, progress, cts.Token);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to clear the cache.");
                    throw;
                }
            });

            return Task.FromResult<ActionResult>(Accepted(GetAcceptUrl(job.Id), job));
        }, cancellationToken);

        return (ActionResult<Job>)response;
    }

    #endregion

    #region Methods

    private string GetAcceptUrl(Guid jobId)
    {
        return $"{Request.Scheme}://{Request.Host}{Request.Path}/{jobId}/status";
    }

    private async Task<ActionResult> ProtectCatalogNonGenericAsync(
        string catalogId,
        Func<CatalogContainer, Task<ActionResult>> action,
        CancellationToken cancellationToken)
    {
        var root = _appStateManager.AppState.CatalogState.Root;
        var catalogContainer = await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

        if (catalogContainer is not null)
        {
            if (!AuthUtilities.IsCatalogWritable(catalogContainer.Id, catalogContainer.Metadata, User))
                return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to modify the catalog {catalogId}.");

            return await action.Invoke(catalogContainer);
        }
        else
        {
            return NotFound(catalogId);
        }
    }

    #endregion
}
