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

        AuthUtilities.AddEnabledCatalogPatternClaim(context.Principal, context.Scheme.Name, _securityOptions);

        return base.ValidatePrincipal(context);
    }
}