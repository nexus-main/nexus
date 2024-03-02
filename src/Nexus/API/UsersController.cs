using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to users.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class UsersController : ControllerBase
    {
        // [anonymous]
        // GET      /api/users/authentication-schemes
        // GET      /api/users/authenticate
        // GET      /api/users/signout
        // POST     /api/users/tokens/refresh
        // POST     /api/users/tokens/revoke

        // [authenticated]
        // GET      /api/users/me
        // GET      /api/users/accept-license?catalogId=X
        // POST     /api/users/tokens/generate
        // DELETE   /api/users/tokens/{tokenId}

        // [privileged]
        // GET      /api/users
        // POST     /api/users
        // DELETE   /api/users/{userId}

        // GET      /api/users/{userId}/claims
        // POST     /api/users/{userId}/claims
        // DELETE   /api/users/claims/{claimId}

        // GET      /api/users/{userId}/tokens

        #region Fields

        private readonly IDBService _dbService;
        private readonly INexusAuthenticationService _authService;
        private readonly SecurityOptions _securityOptions;
        private readonly ILogger<UsersController> _logger;

        #endregion

        #region Constructors

        public UsersController(
            IDBService dBService,
            INexusAuthenticationService authService,
            IOptions<SecurityOptions> securityOptions,
            ILogger<UsersController> logger)
        {
            _dbService = dBService;
            _authService = authService;
            _securityOptions = securityOptions.Value;
            _logger = logger;
        }

        #endregion

        #region Anonymous

        /// <summary>
        /// Returns a list of available authentication schemes.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("authentication-schemes")]
        public List<AuthenticationSchemeDescription> GetAuthenticationSchemes()
        {
            var providers = _securityOptions.OidcProviders.Any()
                ? _securityOptions.OidcProviders
                : new List<OpenIdConnectProvider>() { NexusAuthExtensions.DefaultProvider };

            return providers
                .Select(provider => new AuthenticationSchemeDescription(provider.Scheme, provider.DisplayName))
                .ToList();
        }

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
        /// Refreshes the JWT token.
        /// </summary>
        /// <param name="request">The refresh token request.</param>
        /// <returns>A new pair of JWT and refresh token.</returns>
        [AllowAnonymous]
        [HttpPost("tokens/refresh")]
        public async Task<ActionResult<TokenPair>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            // get token
            var internalRefreshToken = InternalRefreshToken.Deserialize(request.RefreshToken);
            var token = await _dbService.FindRefreshTokenAsync(internalRefreshToken.Id, includeUserClaims: true);

            if (token is null)
                return NotFound("Token not found.");

            // check token
            if (token.Token != request.RefreshToken)
            {
                _logger.LogWarning($"Attempted reuse of revoked token of user {token.Owner.Id} ({token.Owner.Name}).");

                # warning Temporarily disabled
                // await _authService.RevokeTokenAsync(token);
            }

            if (token.IsExpired)
                return UnprocessableEntity("Invalid token.");

            // refresh token
            var tokenPair = await _authService
                .RefreshTokenAsync(token);

            return tokenPair;
        }

        /// <summary>
        /// Revokes a refresh token.
        /// </summary>
        /// <param name="request">The revoke token request.</param>
        [AllowAnonymous]
        [HttpPost("tokens/revoke")]
        public async Task<ActionResult> RevokeTokenAsync(RevokeTokenRequest request)
        {
            // get token
            var internalRefreshToken = InternalRefreshToken.Deserialize(request.RefreshToken);
            var token = await _dbService.FindRefreshTokenAsync(internalRefreshToken.Id, includeUserClaims: false);

            if (token is null)
                return NotFound("Token not found.");

            // revoke token
            await _authService
                .RevokeTokenAsync(token);

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

            var isAdmin = user.Claims.Any(
                claim => claim.Type == Claims.Role &&
                         claim.Value == NexusRoles.ADMINISTRATOR);

            return new MeResponse(
                user.Id,
                user,
                isAdmin,
                user.RefreshTokens.ToDictionary(entry => entry.Id, entry => entry));
        }

        /// <summary>
        /// Generates a refresh token.
        /// </summary>
        /// <param name="description">The refresh token description.</param>
        /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
        [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
        [HttpPost("tokens/generate")]
        public async Task<ActionResult<string>> GenerateRefreshTokenAsync(
            [BindRequired] string description,
            [FromQuery] string? userId = default
        )
        {
            if (TryAuthenticate(userId, out var actualUserId, out var response))
            {
                var user = await _dbService.FindUserAsync(actualUserId);

                if (user is null)
                    return NotFound($"Could not find user {userId}.");

                var refreshToken = await _authService.GenerateRefreshTokenAsync(user, description);

                return Ok(refreshToken);
            }

            else
            {
                return response;
            }
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

            var claim = new NexusClaim(Guid.NewGuid(), NexusClaims.CAN_READ_CATALOG, catalogId);
            user.Claims.Add(claim);

            /* When the primary key is != Guid.Empty, EF thinks the entity
             * already exists and tries to update it. Adding it explicitly
             * will correctly mark the entity as "added".
             */
            await _dbService.AddOrUpdateClaimAsync(claim);

            foreach (var identity in User.Identities)
            {
                identity?.AddClaim(new Claim(NexusClaims.CAN_READ_CATALOG, catalogId));
            }

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, User);

            var redirectUrl = "/catalogs/" + WebUtility.UrlEncode(catalogId);

            return Redirect(redirectUrl);
        }

        /// <summary>
        /// Deletes a refresh token.
        /// </summary>
        /// <param name="tokenId">The identifier of the refresh token.</param>
        [HttpDelete("tokens/{tokenId}")]
        public async Task<ActionResult> DeleteRefreshTokenAsync(
            Guid tokenId)
        {
            // TODO: Is this thread safe? Maybe yes, because of scoped EF context.

            var token = await _dbService.FindRefreshTokenAsync(tokenId, includeUserClaims: true);

            if (token is null)
                return NotFound($"Could not find refresh token {tokenId}.");

            token.Owner.RefreshTokens.Remove(token);

            await _dbService.SaveChangesAsync();

            return Ok();
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
        /// Gets all refresh tokens.
        /// </summary>
        /// <param name="userId">The identifier of the user.</param>
        [Authorize(Policy = NexusPolicies.RequireAdmin)]
        [HttpGet("{userId}/tokens")]
        public async Task<ActionResult<IReadOnlyDictionary<Guid, RefreshToken>>> GetRefreshTokensAsync(
            string userId)
        {
            var user = await _dbService.FindUserAsync(userId);

            if (user is null)
                return NotFound($"Could not find user {userId}.");

            return Ok(user.RefreshTokens.ToDictionary(token => token.Id, token => token));
        }

        private bool TryAuthenticate(
            string? requestedId,
            out string userId,
            [NotNullWhen(returnValue: false)] out ActionResult? response)
        {
            var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
            var currentId = User.FindFirstValue(Claims.Subject) ?? throw new Exception("The sub claim is null.");

            if (isAdmin || requestedId is null || requestedId == currentId)
                response = null;

            else
                response = StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to get source registrations of user {requestedId}.");

            userId = requestedId is null
                ? currentId
                : requestedId;

            return response is null;
        }

        #endregion
    }
}
