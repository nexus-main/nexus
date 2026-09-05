// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Services;
using Nexus.Utilities;
using System.ComponentModel.DataAnnotations;
using ExportParameters = Nexus.Core.V2.ExportParameters;

namespace Nexus.Controllers.V2;

/// <summary>
/// Provides access to jobs.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class JobsController(
    AppStateManager appStateManager,
    IJobService jobService,
    IServiceProvider serviceProvider,
    Serilog.IDiagnosticContext diagnosticContext,
    ILogger<JobsController> logger) : ControllerBase
{
    private readonly AppStateManager _appStateManager = appStateManager;
    private readonly ILogger _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly Serilog.IDiagnosticContext _diagnosticContext = diagnosticContext;
    private readonly IJobService _jobService = jobService;

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
                var catalogItemRequest = await root.TryFindAsync(root, resourcePath, cancellationToken)
                    ?? throw new ValidationException($"Could not find resource path {resourcePath}.");

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

    private string GetAcceptUrl(Guid jobId)
    {
        return $"{Request.Scheme}://{Request.Host}{Request.Path}/{jobId}/status";
    }
}
