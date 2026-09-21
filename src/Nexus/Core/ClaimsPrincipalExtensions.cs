// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Claims;

namespace Nexus.Core;

internal static class ClaimsPrincipalExtensions
{
    public static string? GetClaim(this ClaimsPrincipal principal, string type)
    {
        return principal.FindFirst(type)?.Value;
    }

    public static void AddClaim(this ClaimsPrincipal principal, string type, string value)
    {
        var identity = principal.Identities.FirstOrDefault(identity => identity.IsAuthenticated)
            ?? principal.Identities.First();

        identity.AddClaim(new Claim(type, value));
    }

    public static void RemoveClaims(this ClaimsPrincipal principal, string type)
    {
        foreach (var identity in principal.Identities)
        {
            var claims = identity.FindAll(type).ToList();

            foreach (var claim in claims)
                identity.RemoveClaim(claim);
        }
    }
}
