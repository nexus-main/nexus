// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Services;
using Nexus.Utilities;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to users.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class UsersController(
    IDBService dBService,
    ITokenService tokenService
) : ControllerBase
{
    // [anonymous]
    // GET      /api/users/authenticate
    // GET      /api/users/signout
    // POST     /api/users/tokens/delete

    // [authenticated]
    // GET      /api/users/me
    // GET      /api/users/reauthenticate
    // GET      /api/users/accept-license?catalogId=X
    // POST     /api/users/tokens/create
    // DELETE   /api/users/tokens/{tokenId}

    // [privileged]
    // GET      /api/users
    // POST     /api/users
    // DELETE   /api/users/{userId}

    // GET      /api/users/{userId}/claims
    // POST     /api/users/{userId}/claims
    // DELETE   /api/users/claims/{claimId}

    // GET      /api/users/{userId}/tokens

    private readonly IDBService _dbService = dBService;

    private readonly ITokenService _tokenService = tokenService;

    #region Anonymous

    /// <summary>
    /// Authenticates the user.
    /// </summary>
    /// <param name="scheme">The authentication scheme to challenge.</param>
    /// <param name="returnUrl">The URL to return after successful authentication.</param>
    [AllowAnonymous]
    [HttpPost("authenticate")]
    public ChallengeResult Authenticate(
        [BindRequired] string scheme,
        [BindRequired] string returnUrl)
    {
        var properties = new AuthenticationProperties()
        {
            RedirectUri = returnUrl
        };

        return Challenge(properties, scheme);
    }

    /// <summary>
    /// Logs out the user.
    /// </summary>
    /// <param name="returnUrl">The URL to return after logout.</param>
    [AllowAnonymous]
    [HttpPost("signout")]
    public async Task SignOutAsync(
        [BindRequired] string returnUrl)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties() { RedirectUri = returnUrl };
        var scheme = User.Identity!.AuthenticationType!;

        await HttpContext.SignOutAsync(scheme, properties);
    }

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
    public async Task<ActionResult<MeResponse>> GetMeAsync()
    {
        var userId = User.FindFirst(Claims.Subject)!.Value;
        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        var translatedClaimsMap = user.Claims
            .ToDictionary(entry => entry.Id, entry => new NexusClaim(
                id: default,
                type: entry.Type,
                value: entry.Value
            ));

        var tokenMap = await _tokenService.GetAllAsync(userId);

        var translatedTokenMap = tokenMap
            .ToDictionary(entry => entry.Value.Id, entry => new PersonalAccessToken(
                entry.Value.Description,
                entry.Value.Expires,
                entry.Value.Claims
            ));

        return new MeResponse(
            user.Id,
            user.Name,
            translatedClaimsMap,
            translatedTokenMap
        );
    }

    /// <summary>
    /// Allows the user to reauthenticate in case of modified claims.
    /// </summary>
    [HttpGet("reauthenticate")]
    public async Task<ActionResult> ReAuthenticateAsync()
    {
        var userId = User.FindFirst(Claims.Subject)!.Value;
        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        string[] nexusClaimTypes =
        [
            .. Enum.GetNames<NexusClaims>(),
            "role"
        ];

        foreach (var identity in User.Identities)
        {
            if (identity is null)
                continue;

            /* clear all */
            foreach (var claim in identity.Claims.ToList())
            {
                if (nexusClaimTypes.Contains(claim.Type))
                    identity.RemoveClaim(claim);
            }

            /* add current */
            foreach (var claim in user.Claims)
            {
                identity.AddClaim(new Claim(claim.Type, claim.Value));
            }
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, User);

        return Redirect("/");
    }

    /// <summary>
    /// Creates a personal access token.
    /// </summary>
    /// <param name="token">The personal access token to create.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    [HttpPost("tokens/create")]
    public async Task<ActionResult<string>> CreateTokenAsync(
        PersonalAccessToken token,
        [FromQuery] string? userId = default
    )
    {
        if (TryAuthenticate(userId, out var actualUserId, out var response))
        {
            var user = await _dbService.FindUserAsync(actualUserId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            var utcExpires = token.Expires.ToUniversalTime();

            var secret = await _tokenService
                .CreateAsync(
                    actualUserId,
                    token.Description,
                    utcExpires,
                    token.Claims
                );

            var tokenValue = AuthUtilities.ComponentsToTokenValue(actualUserId, secret);

            return Ok(tokenValue);
        }

        else
        {
            return response;
        }
    }

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="tokenId">The identifier of the personal access token.</param>
    [HttpDelete("tokens/{tokenId}")]
    public async Task<ActionResult> DeleteTokenAsync(
        Guid tokenId)
    {
        var userId = User.FindFirst(Claims.Subject)!.Value;

        await _tokenService.DeleteAsync(userId, tokenId);

        return Ok();
    }

    /// <summary>
    /// Accepts the license of the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    [HttpGet("accept-license")]
    public async Task<ActionResult> AcceptLicenseAsync(
        [BindRequired] string catalogId)
    {
        // TODO: Is this thread safe? Maybe yes, because of scoped EF context.
        catalogId = WebUtility.UrlDecode(catalogId);

        var userId = User.FindFirst(Claims.Subject)!.Value;
        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        var claim = new NexusClaim(Guid.NewGuid(), nameof(NexusClaims.CanReadCatalog), catalogId);
        user.Claims.Add(claim);

        /* When the primary key is != Guid.Empty, EF thinks the entity
         * already exists and tries to update it. Adding it explicitly
         * will correctly mark the entity as "added".
         */
        await _dbService.AddOrUpdateClaimAsync(claim);

        foreach (var identity in User.Identities)
        {
            identity?.AddClaim(new Claim(nameof(NexusClaims.CanReadCatalog), catalogId));
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, User);

        var redirectUrl = "/catalogs/" + WebUtility.UrlEncode(catalogId);

        return Redirect(redirectUrl);
    }

    #endregion

    #region Privileged

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    /// <returns></returns>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpGet]
    public async Task<ActionResult<IDictionary<string, NexusUser>>> GetUsersAsync()
    {
        var users = await _dbService.GetUsers()
            .ToListAsync();

        return users.ToDictionary(user => user.Id, user => user);
    }

    /// <summary>
    /// Creates a user.
    /// </summary>
    /// <param name="user">The user to create.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpPost]
    public async Task<string> CreateUserAsync(
        [FromBody] NexusUser user)
    {
        user.Id = Guid.NewGuid().ToString();
        await _dbService.AddOrUpdateUserAsync(user);

        return user.Id;
    }

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpDelete("{userId}")]
    public async Task<ActionResult> DeleteUserAsync(
        string userId)
    {
        await _dbService.DeleteUserAsync(userId);
        return Ok();
    }

    /// <summary>
    /// Gets all claims.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpGet("{userId}/claims")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, NexusClaim>>> GetClaimsAsync(
        string userId)
    {
        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        return Ok(user.Claims.ToDictionary(claim => claim.Id, claim => claim));
    }

    /// <summary>
    /// Creates a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claim">The claim to create.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpPost("{userId}/claims")]
    public async Task<ActionResult<Guid>> CreateClaimAsync(
        string userId,
        [FromBody] NexusClaim claim)
    {
        // TODO: Is this thread safe? Maybe yes, because of scoped EF context.

        claim.Id = Guid.NewGuid();

        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        user.Claims.Add(claim);

        /* When the primary key is != Guid.Empty, EF thinks the entity
         * already exists and tries to update it. Adding it explicitly
         * will correctly mark the entity as "added".
         */
        await _dbService.AddOrUpdateClaimAsync(claim);

        return Ok(claim.Id);
    }

    /// <summary>
    /// Deletes a claim.
    /// </summary>
    /// <param name="claimId">The identifier of the claim.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpDelete("claims/{claimId}")]
    public async Task<ActionResult> DeleteClaimAsync(
        Guid claimId)
    {
        // TODO: Is this thread safe? Maybe yes, because of scoped EF context.

        var claim = await _dbService.FindClaimAsync(claimId);

        if (claim is null)
            return NotFound($"Could not find claim {claimId}.");

        claim.Owner.Claims.Remove(claim);

        await _dbService.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Gets all personal access tokens.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    [Authorize(Policy = NexusPolicies.RequireAdmin)]
    [HttpGet("{userId}/tokens")]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, PersonalAccessToken>>> GetTokensAsync(
        string userId)
    {
        var user = await _dbService.FindUserAsync(userId);

        if (user is null)
            return NotFound($"Could not find user {userId}.");

        var tokenMap = await _tokenService.GetAllAsync(userId);

        var translatedTokenMap = tokenMap
            .ToDictionary(entry => entry.Value.Id, entry => new PersonalAccessToken(
                entry.Value.Description,
                entry.Value.Expires,
                entry.Value.Claims
            ));

        return translatedTokenMap;
    }

    private bool TryAuthenticate(
        string? requestedId,
        out string userId,
        [NotNullWhen(returnValue: false)] out ActionResult? response)
    {
        var isAdmin = User.IsInRole(nameof(NexusRoles.Administrator));
        var currentId = User.FindFirstValue(Claims.Subject) ?? throw new Exception("The sub claim is null.");

        if (isAdmin || requestedId is null || requestedId == currentId)
            response = null;

        else
            response = StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to perform the operation for user {requestedId}.");

        userId = requestedId is null
            ? currentId
            : requestedId;

        return response is null;
    }

    #endregion
}
