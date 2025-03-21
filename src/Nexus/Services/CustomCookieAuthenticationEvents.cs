// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Nexus.Utilities;

namespace Nexus.Core;

internal class CustomCookieAuthenticationEvents(IOptions<SecurityOptions> options) : CookieAuthenticationEvents
{
    private readonly SecurityOptions _securityOptions = options.Value;

    public override Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null)
            return Task.CompletedTask;

        if (context.Principal.Identities.Count() != 2)
            context.RejectPrincipal();

        var scheme = context.Principal.Identities.First().AuthenticationType;

        if (scheme is null)
            context.RejectPrincipal();

        AuthUtilities.AddEnabledCatalogPatternClaim(context.Principal, scheme, _securityOptions);

        return base.ValidatePrincipal(context);
    }
}