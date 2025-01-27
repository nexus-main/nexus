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
    IOptions<GeneralOptions> generalOptions
) : ControllerBase
{
    // [authenticated]
    // GET      /api/system/configuration
    // GET      /api/system/file-type
    // GET      /api/system/help-link

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
}
