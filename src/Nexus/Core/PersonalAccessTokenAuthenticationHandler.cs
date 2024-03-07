using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Nexus.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Core;

internal static class PersonalAccessTokenAuthenticationDefaults
{
    public const string AuthenticationScheme = "pat";
}

internal class PersonalAccessTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITokenService _tokenService;

    public PersonalAccessTokenAuthHandler(
        ITokenService tokenService,
        IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder) : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValues = Request.Headers.Authorization;
        var principal = default(ClaimsPrincipal);

        foreach (var headerValue in headerValues)
        {
            if (headerValue is null)
                continue;

            if (headerValue.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = headerValue.Split(' ', count: 2);
                var tokenValue = parts[1];

                if (_tokenService.TryGet(tokenValue, out var userId, out var token))
                {
                    var claims = token.Claims

                        .Where(claim => 
                            claim.Type == NexusClaims.CAN_READ_CATALOG || 
                            claim.Type == NexusClaims.CAN_WRITE_CATALOG)

                        /* Prefix PAT claims with "pat_" so they are distinguishable from the 
                         * more powerful user claims. It will be checked for in the catalogs 
                         * controller.
                         */
                        .Select(claim => new Claim($"pat_{claim.Type}", claim.Value));

                    claims = claims.Append(new Claim(Claims.Subject, userId));
                    claims = claims.Append(new Claim(Claims.Role, NexusRoles.USER));

                    var identity = new ClaimsIdentity(
                        claims, 
                        Scheme.Name, 
                        nameType: Claims.Name,
                        roleType: Claims.Role);

                    if (principal is null)
                        principal = new ClaimsPrincipal();

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

        return Task.FromResult(result);
    }
}