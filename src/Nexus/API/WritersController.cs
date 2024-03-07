using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to extensions.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class WritersController : ControllerBase
{
    // GET      /api/writers/descriptions

    #region Fields

    private readonly AppState _appState;

    #endregion

    #region Constructors

    public WritersController(
        AppState appState)
    {
        _appState = appState;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the list of writer descriptions.
    /// </summary>
    [HttpGet("descriptions")]
    public List<ExtensionDescription> GetDescriptions()
    {
        return _appState.DataWriterDescriptions;
    }

    #endregion
}
