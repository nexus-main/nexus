// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Nexus.Services;
using Nexus.Utilities;

namespace Nexus.Core;

internal static class PersonalAccessTokenAuthenticationDefaults
{
    public const string AuthenticationScheme = "pat";
}

internal class PersonalAccessTokenAuthHandler(
    ITokenService tokenService,
    IOptions<SecurityOptions> securityOptions,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly SecurityOptions _securityOptions = securityOptions.Value;

    private readonly ITokenService _tokenService = tokenService;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValues = Request.Headers.Authorization;
        var principal = default(ClaimsPrincipal);

        foreach (var headerValue in headerValues)
        {
            if (headerValue is null)
                continue;

            if (headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = headerValue.Split(' ', count: 2);
                var (userId, secret) = AuthUtilities.TokenValueToComponents(parts[1]);

                if (_tokenService.TryGet(userId, secret, out var token))
                {
                    if (DateTime.UtcNow >= token.Expires)
                        return Task.FromResult(AuthenticateResult.NoResult());

                    /* The pat_user_ prefixed claims represent what the token creator could do. */
                    var userClaims = token.GrantClaims
                        .Select(claim => new Claim(NexusClaimsHelper.ToPatUserClaimType(claim.Type), claim.Value));

                    var tokenClaimsRead = token.Claims
                        .Where(claim => claim.Type == nameof(NexusClaims.CanReadCatalog))
                        .Select(claim => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanReadCatalog)), claim.Value));

                    var tokenClaimsWrite = token.Claims
                        .Where(claim => claim.Type == nameof(NexusClaims.CanWriteCatalog))
                        .Select(claim => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanWriteCatalog)), claim.Value));

                    var tokenClaimsRole = token.Claims
                        .Where(tokenClaim =>
                            tokenClaim.Type == NexusClaimTypes.Role &&
                            token.GrantClaims.Any(grantClaim => grantClaim.Type == NexusClaimTypes.Role && grantClaim.Value == tokenClaim.Value))
                        .Select(claim => new Claim(NexusClaimsHelper.ToPatClaimType(NexusClaimTypes.Role), claim.Value));

                    var name = token.GrantClaims
                        .FirstOrDefault(claim => claim.Type == NexusClaimTypes.Name)?.Value ?? userId;

                    var claims = Enumerable.Empty<Claim>()
                        .Append(new Claim(NexusClaimTypes.Subject, userId))
                        .Append(new Claim(NexusClaimTypes.Name, name))
                        .Concat(userClaims)
                        .Concat(tokenClaimsRead)
                        .Concat(tokenClaimsWrite)
                        .Concat(tokenClaimsRole);

                    var claimsToBeAdmin = token.Claims
                        .Any(claim => claim.Type == NexusClaimTypes.Role && claim.Value == nameof(NexusRoles.Administrator));

                    var isAdmin = token.GrantClaims
                        .Any(claim => claim.Type == NexusClaimTypes.Role && claim.Value == nameof(NexusRoles.Administrator));

                    /* Only act as admin if you claim to be one and you are one, otherwise the PAT would be too powerful */
                    if (claimsToBeAdmin && isAdmin)
                        claims = claims.Append(new Claim(NexusClaimTypes.Role, nameof(NexusRoles.Administrator)));

                    var identity = new ClaimsIdentity(
                        claims,
                        Scheme.Name,
                        nameType: NexusClaimTypes.Name,
                        roleType: NexusClaimTypes.Role
                    );

                    principal ??= new ClaimsPrincipal();
                    principal.AddIdentity(identity);

                    var enabledCatalogsPattern = token.GrantClaims
                        .FirstOrDefault(claim => claim.Type == NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM)?.Value
                        ?? _securityOptions.EnabledCatalogsPattern;

                    AuthUtilities.SetEnabledCatalogPatternClaim(principal, enabledCatalogsPattern);
                }
            }
        }

        AuthenticateResult result;

        if (principal is null)
        {
            result = AuthenticateResult.NoResult();
        }

        else
        {
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            result = AuthenticateResult.Success(ticket);
        }

        return Task.FromResult(result);
    }
}
