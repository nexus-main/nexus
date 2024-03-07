using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Services;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to artifacts.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class ArtifactsController : ControllerBase
{
    // GET      /api/artifacts/{artifactId}

    #region Fields

    public IDatabaseService _databaseService;

    #endregion

    #region Constructors

    public ArtifactsController(
        IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the specified artifact.
    /// </summary>
    /// <param name="artifactId">The artifact identifier.</param>
    [HttpGet("{artifactId}")]
    public ActionResult
        Download(
            string artifactId)
    {
        if (_databaseService.TryReadArtifact(artifactId, out var artifactStream))
        {
            Response.Headers.ContentLength = artifactStream.Length;
            return File(artifactStream, "application/octet-stream"); // do not set filname here, otherwise <a download="abc.pdf" /> will not work!
        }

        else
        {
            return NotFound($"Could not find artifact {artifactId}.");
        }
    }

    #endregion
}
