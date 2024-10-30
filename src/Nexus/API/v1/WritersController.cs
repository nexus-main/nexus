// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Core.V1;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to extensions.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class WritersController(
    AppState appState) : ControllerBase
{
    // GET      /api/writers/descriptions

    private readonly AppState _appState = appState;

    /// <summary>
    /// Gets the list of writer descriptions.
    /// </summary>
    [HttpGet("descriptions")]
    public List<ExtensionDescription> GetDescriptions()
    {
        return _appState.DataWriterDescriptions;
    }
}
