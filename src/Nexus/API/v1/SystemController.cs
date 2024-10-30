// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using System.Text.Json;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to the system.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class SystemController(
    AppState appState,
    AppStateManager appStateManager,
    IOptions<GeneralOptions> generalOptions) : ControllerBase
{
    // [authenticated]
    // GET      /api/system/configuration
    // GET      /api/system/file-type
    // GET      /api/system/help-link

    // [privileged]
    // PUT      /api/system/configuration

    private readonly AppState _appState = appState;
    private readonly AppStateManager _appStateManager = appStateManager;
    private readonly GeneralOptions _generalOptions = generalOptions.Value;

    /// <summary>
    /// Gets the default file type.
    /// </summary>
    [HttpGet("file-type")]
    public string? GetDefaultFileType()
    {
        return _generalOptions.DefaultFileType;
    }

    /// <summary>
    /// Gets the configured help link.
    /// </summary>
    [HttpGet("help-link")]
    public string? GetHelpLink()
    {
        return _generalOptions.HelpLink;
    }

    /// <summary>
    /// Gets the system configuration.
    /// </summary>
    [HttpGet("configuration")]
    public IReadOnlyDictionary<string, JsonElement>? GetConfiguration()
    {
        return _appState.Project.SystemConfiguration;
    }

    /// <summary>
    /// Sets the system configuration.
    /// </summary>
    [HttpPut("configuration")]
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    public Task SetConfigurationAsync(IReadOnlyDictionary<string, JsonElement>? configuration)
    {
        return _appStateManager.PutSystemConfigurationAsync(configuration);
    }
}
