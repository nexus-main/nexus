// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Nexus.Services;
using Nexus.Utilities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Core;

internal static class PersonalAccessTokenAuthenticationDefaults
{
    public const string AuthenticationScheme = "pat";
}

internal class PersonalAccessTokenAuthHandler(
    ITokenService tokenService,
    IDBService dbService,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private readonly ITokenService _tokenService = tokenService;

    private readonly IDBService _dbService = dbService;

    protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
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

                var user = await _dbService.FindUserAsync(userId);

                if (user is null)
                    continue;

                if (_tokenService.TryGet(userId, secret, out var token))
                {
                    if (DateTime.UtcNow >= token.Expires)
                        return AuthenticateResult.NoResult();

                    var userClaims = user.Claims
                        .Select(claim => new Claim(NexusClaimsHelper.ToPatUserClaimType(claim.Type), claim.Value));

                    var tokenClaimsRead = token.Claims
                        .Where(claim => claim.Type == NexusClaims.CanReadCatalog.ToString())
                        .Select(claim => new Claim(NexusClaims.CanReadCatalog.ToString(), claim.Value));

                    var tokenClaimsWrite = token.Claims
                        .Where(claim => claim.Type == NexusClaims.CanWriteCatalog.ToString())
                        .Select(claim => new Claim(NexusClaims.CanWriteCatalog.ToString(), claim.Value));

                    var tokenClaimsRole = token.Claims
                        .Where(tokenClaim =>
                            tokenClaim.Type == Claims.Role &&
                            user.Claims.Any(userClaim => userClaim.Type == Claims.Role && userClaim.Value == tokenClaim.Value))
                        .Select(claim => new Claim(Claims.Role, claim.Value));

                    var claims = Enumerable.Empty<Claim>()
                        .Append(new Claim(Claims.Subject, userId))
                        .Append(new Claim(Claims.Name, user.Name))
                        .Append(new Claim(Claims.Role, NexusRoles.User.ToString()))
                        .Concat(userClaims)
                        .Concat(tokenClaimsRead)
                        .Concat(tokenClaimsWrite)
                        .Concat(tokenClaimsRole);

                    var identity = new ClaimsIdentity(
                        claims,
                        Scheme.Name,
                        nameType: Claims.Name,
                        roleType: Claims.Role);

                    principal ??= new ClaimsPrincipal();

                    principal.AddIdentity(identity);
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

        return result;
    }
}