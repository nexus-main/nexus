// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Core.V1;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to the system.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class SystemController(
    IOptions<GeneralOptions> generalOptions,
    IOptions<SecurityOptions> securityOptions
) : ControllerBase
{
    // [authenticated]
    // GET      /api/system

    private readonly GeneralOptions _generalOptions = generalOptions.Value;

    private readonly SecurityOptions _securityOptions = securityOptions.Value;

    /// <summary>
    /// Gets the system configuration.
    /// </summary>
    [HttpGet]
    public SystemResponse Get()
    {
        return new SystemResponse(
            _generalOptions.DefaultFileType,
            _generalOptions.HelpLink,
            _securityOptions.LogoutUrl
        );
    }
}
