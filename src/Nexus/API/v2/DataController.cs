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
    IDataStreamSessionManager streamSessionManager) : ControllerBase
{
    private const string Http2RequiredMessage = "The v2 batch streaming API requires HTTP/2. Configure the client or reverse proxy to use HTTP/2 upstream to Nexus.";

    private readonly IDataService _dataService = dataService;
    private readonly IDataStreamSessionManager _streamSessionManager = streamSessionManager;

    /// <summary>
    /// Registers a batch data stream session.
    /// </summary>
    /// <param name="request">The batch stream request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registered batch stream session.</returns>
    [HttpPost("streams/batch")]
    [ProducesResponseType(typeof(BatchStreamResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(string), StatusCodes.Status426UpgradeRequired)]
    public async Task<ActionResult<BatchStreamResponse>> RegisterBatchStreamAsync(
        [FromBody] BatchStreamRequest request,
        CancellationToken cancellationToken)
    {
        var http2RequiredResult = RequireHttp2();

        if (http2RequiredResult is not null)
            return http2RequiredResult;

        try
        {
            return await _dataService.RegisterBatchStreamAsync(request, cancellationToken);
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

    /// <summary>
    /// Gets a single channel of a registered batch data stream session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The channel data stream.</returns>
    [HttpGet("streams/batch/{sessionId:guid}/channel/{channelId:guid}")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status426UpgradeRequired)]
    public async Task<ActionResult> GetBatchStreamChannelAsync(
        Guid sessionId,
        Guid channelId,
        CancellationToken cancellationToken)
    {
        var http2RequiredResult = RequireHttp2();

        if (http2RequiredResult is not null)
            return http2RequiredResult;

        var lease = _streamSessionManager.Attach(sessionId, channelId, User);

        if (lease is null)
            return NotFound();

        Exception? faultException = null;

        try
        {
            Response.ContentType = "application/octet-stream";
            Response.Headers.ContentLength = lease.Channel.ContentLength;
            await Response.StartAsync(cancellationToken);

            await lease.Channel.Reader.CopyToAsync(Response.BodyWriter, cancellationToken);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            faultException = ex;
            throw;
        }
        finally
        {
            var faulted = faultException is not null || cancellationToken.IsCancellationRequested;
            await lease.Session.CompleteChannelAsync(channelId, faulted, faultException);
        }
    }

    /// <summary>
    /// Gets the status of a batch data stream session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The session status.</returns>
    [HttpGet("streams/batch/{sessionId:guid}/status")]
    [ProducesResponseType(typeof(BatchStreamSessionStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BatchStreamSessionStatus> GetBatchStreamSessionStatus(Guid sessionId)
    {
        var status = _streamSessionManager.GetStatus(sessionId, User);

        if (status is null)
            return NotFound();

        return status;
    }

    private ActionResult? RequireHttp2()
    {
        if (string.Equals(Request.Protocol, "HTTP/2", StringComparison.OrdinalIgnoreCase))
            return default;

        return StatusCode(StatusCodes.Status426UpgradeRequired, Http2RequiredMessage);
    }
}
