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
    IDataService dataService) : ControllerBase
{
    private readonly IDataService _dataService = dataService;

    /// <summary>
    /// Streams multiple resources in a framed binary response.
    /// </summary>
    /// <param name="request">The batch stream request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The framed data stream.</returns>
    [HttpPost]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
            return File(stream, "application/octet-stream", "data.bin");
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
