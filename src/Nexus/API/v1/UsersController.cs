// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Services;
using Nexus.Utilities;
using System.Security.Claims;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to users.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class UsersController(
    ITokenService tokenService
) : ControllerBase
{
    // [anonymous]
    // POST     /api/users/tokens/delete

    // [authenticated]
    // GET      /api/users/me
    // GET      /api/users/tokens
    // POST     /api/users/tokens/create
    // DELETE   /api/users/tokens/{tokenId}

    private readonly ITokenService _tokenService = tokenService;

    #region Anonymous

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="value">The personal access token to delete.</param>
    [AllowAnonymous]
    [HttpDelete("tokens/delete")]
    public async Task<ActionResult> DeleteTokenByValueAsync(
        [BindRequired] string value)
    {
        var (userId, secret) = AuthUtilities.TokenValueToComponents(value);
        await _tokenService.DeleteAsync(userId, secret);

        return Ok();
    }

    #endregion

    #region Authenticated

    /// <summary>
    /// Gets the current user.
    /// </summary>
    [HttpGet("me")]
    public ActionResult<MeResponse> GetMe()
    {
        var userId = User.FindFirst(NexusClaimTypes.Subject)!.Value;
        var name = User.FindFirst(NexusClaimTypes.Name)?.Value ?? userId;

        var claims = User.Claims
            .Select(claim => new TokenClaim(claim.Type, claim.Value))
            .ToList();

        return new MeResponse(userId, name, claims);
    }

    /// <summary>
    /// Gets all personal access tokens.
    /// </summary>
    [HttpGet("tokens")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, PersonalAccessToken>>> GetTokensAsync()
    {
        var userId = User.FindFirst(NexusClaimTypes.Subject)!.Value;

        var tokenMap = await _tokenService.GetAllAsync(userId);

        var translatedTokenMap = tokenMap
            .ToDictionary(entry => entry.Value.Id, entry => new PersonalAccessToken(
                entry.Value.Description,
                entry.Value.Expires,
                entry.Value.Claims,
                entry.Value.GrantClaims
            ));

        return translatedTokenMap;
    }

    /// <summary>
    /// Creates a personal access token.
    /// </summary>
    /// <param name="token">The personal access token to create.</param>
    [HttpPost("tokens/create")]
    public async Task<ActionResult<string>> CreateTokenAsync(
        PersonalAccessToken token)
    {
        var userId = User.FindFirst(NexusClaimTypes.Subject)!.Value;

        var grantClaims = User.Claims
            .Select(claim => new TokenClaim(claim.Type, claim.Value))
            .ToList();

        var utcExpires = token.Expires.ToUniversalTime();

        var secret = await _tokenService
            .CreateAsync(
                userId,
                token.Description,
                utcExpires,
                token.Claims,
                grantClaims
            );

        var tokenValue = AuthUtilities.ComponentsToTokenValue(userId, secret);

        return Ok(tokenValue);
    }

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="tokenId">The identifier of the personal access token.</param>
    [HttpDelete("tokens/{tokenId}")]
    public async Task<ActionResult> DeleteTokenAsync(
        Guid tokenId)
    {
        var userId = User.FindFirst(NexusClaimTypes.Subject)!.Value;

        await _tokenService.DeleteAsync(userId, tokenId);

        return Ok();
    }

    #endregion
}
