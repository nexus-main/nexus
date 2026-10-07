// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core.V2;
using Nexus.Services;
using System.ComponentModel.DataAnnotations;

namespace Nexus.Controllers.V2;

/// <summary>
/// Provides access to data.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class DataController(
    IDataService dataService,
    ILoggerFactory loggerFactory) : ControllerBase
{
    private readonly IDataService _dataService = dataService;

    /// <summary>Streams bounded Float32 visualization points and progress in a versioned Arrow contract.</summary>
    /// <param name="request">The domain, resources and up to three views.</param>
    /// <param name="visualization">The visualization service.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The visualization Arrow stream.</returns>
    [HttpPost("visualization")]
    [Produces("application/vnd.apache.arrow.stream")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> GetVisualizationAsync(
        [FromBody] VisualizationRequest request,
        [FromServices] IVisualizationService visualization,
        CancellationToken cancellationToken)
    {
        VisualizationTiming? timing = null;
        var header = Request.Headers[VisualizationTiming.Header];

        if (header.Count == 1 && header[0] is { Length: 36 } value && Guid.TryParseExact(value, "D", out var id) &&
            string.Equals(value, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            timing = new VisualizationTiming(loggerFactory.CreateLogger(VisualizationTiming.Category), id);
            Response.Headers[VisualizationTiming.Header] = timing.RequestId;
        }

        try
        {
            Func<Stream, CancellationToken, Task> write;

            using (var prepare = timing?.Measure("prepare", cancellationToken))
            {
                write = await visualization.PrepareAsync(request, cancellationToken, timing);
                prepare?.Complete();
            }

            Response.ContentType = "application/vnd.apache.arrow.stream";
            Response.Headers.CacheControl = "no-store";
            await write(Response.Body, cancellationToken);
            if (timing is not null && timing.Outcome == "error")
                timing.Outcome = "success";
            return new EmptyResult();
        }
        catch (ValidationException ex) when (!Response.HasStarted)
        {
            if (timing is not null)
                timing.Outcome = "rejected";
            return new ContentResult { StatusCode = StatusCodes.Status422UnprocessableEntity, ContentType = "text/plain", Content = ex.Message };
        }
        catch (Exception ex) when (!Response.HasStarted && ex.Message.StartsWith("Could not find resource path"))
        {
            if (timing is not null)
                timing.Outcome = "not-found";
            return new ContentResult { StatusCode = StatusCodes.Status404NotFound, ContentType = "text/plain", Content = ex.Message };
        }
        catch (Exception ex) when (!Response.HasStarted && ex.Message.StartsWith("The current user is not permitted to access the catalog"))
        {
            if (timing is not null)
                timing.Outcome = "forbidden";
            return new ContentResult { StatusCode = StatusCodes.Status403Forbidden, ContentType = "text/plain", Content = ex.Message };
        }
        finally
        {
            if (timing is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                    timing.Outcome = "cancelled";
                timing.EndRequest();
            }
        }
    }

    /// <summary>
    /// Streams multiple resources in an Apache Arrow IPC response.
    /// </summary>
    /// <param name="request">The batch stream request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Arrow IPC data stream.</returns>
    [HttpPost]
    [Produces("application/vnd.apache.arrow.stream")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> GetStreamAsync(
        [FromBody] BatchStreamRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stream = await _dataService.ReadBatchAsStreamAsync(request, cancellationToken);
            return File(stream, "application/vnd.apache.arrow.stream", "data.arrows");
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
        catch (Exception ex) when (ex.Message.StartsWith("Could not find resource path"))
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex) when (ex.Message.StartsWith("The current user is not permitted to access the catalog"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
    }
}
