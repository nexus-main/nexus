// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Sources;
using OpenIddict.Abstractions;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Utilities;

internal static class AuthUtilities
{
    public static void AddEnabledCatalogPatternClaim(ClaimsPrincipal principal, string? scheme, SecurityOptions options)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (scheme is null)
        {
            principal.AddClaim(
                NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM,
                OpenIdConnectProvider.DEFAULT_ENABLED_CATALOGS_PATTERN
            );
        }

        else
        {
            var oidcProvider = environmentName == "Development" && !options.OidcProviders.Any()
                ? NexusAuthExtensions.DefaultProvider
                : options.OidcProviders.First(x => x.Scheme == scheme);

            principal.AddClaim(
                NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM,
                oidcProvider.EnabledCatalogsPattern
            );
        }
    }

    public static string ComponentsToTokenValue(string userId, string secret)
    {
        return $"{secret}_{userId}";
    }

    public static (string userId, string secret) TokenValueToComponents(string tokenValue)
    {
        var parts = tokenValue.Split('_', count: 2);

        return (parts[1], parts[0]);
    }

    public static bool IsCatalogReadable(
        string catalogId,
        CatalogMetadata catalogMetadata,
        ClaimsPrincipal? owner,
        ClaimsPrincipal user
    )
    {
        return InternalIsCatalogAccessible(
            catalogId,
            catalogMetadata,
            owner,
            user,
            singleClaimType: nameof(NexusClaims.CanReadCatalog),
            groupClaimType: nameof(NexusClaims.CanReadCatalogGroup),
            checkImplicitAccess: true
        );
    }

    public static bool IsCatalogWritable(
        string catalogId,
        CatalogMetadata catalogMetadata,
        ClaimsPrincipal user
    )
    {
        return InternalIsCatalogAccessible(
            catalogId,
            catalogMetadata,
            owner: default,
            user,
            singleClaimType: nameof(NexusClaims.CanWriteCatalog),
            groupClaimType: nameof(NexusClaims.CanWriteCatalogGroup),
            checkImplicitAccess: false
        );
    }

    public static bool IsCatalogEnabled(
        string catalogId,
        ClaimsPrincipal user
    )
    {
        var enabledCatalogsPattern = user
            .GetClaim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM);

        return enabledCatalogsPattern is not null && Regex.IsMatch(catalogId, enabledCatalogsPattern);
    }

    private static bool InternalIsCatalogAccessible(
        string catalogId,
        CatalogMetadata catalogMetadata,
        ClaimsPrincipal? owner,
        ClaimsPrincipal user,
        string singleClaimType,
        string groupClaimType,
        bool checkImplicitAccess
    )
    {
        var isCatalogEnabled = IsCatalogEnabled(catalogId, user);

        foreach (var identity in user.Identities)
        {
            if (identity is null || !identity.IsAuthenticated)
                continue;

            if (catalogId == CatalogContainer.RootCatalogId)
                return true;

            var implicitAccess =
                catalogId == Sample.LocalCatalogId ||
                catalogId == Sample.RemoteCatalogId;

            if (checkImplicitAccess && implicitAccess)
                return true;

            var result = false;

            /* PAT */
            if (identity.AuthenticationType == PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme)
            {
                /* The token alone can access the catalog ... */
                var claimsToBeAdmin = identity.Claims
                    .Any(claim => claim.Type == NexusClaimsHelper.ToPatClaimType(Claims.Role) && claim.Value == nameof(NexusRoles.Administrator));

                var canAccessCatalog = claimsToBeAdmin || identity.HasClaim(
                    claim =>
                        claim.Type == NexusClaimsHelper.ToPatClaimType(singleClaimType) &&
                        Regex.IsMatch(catalogId, claim.Value)
                );

                /* ... but it cannot be more powerful than the
                 * user itself, so next step is to ensure that
                 * the user can access that catalog as well. */
                if (canAccessCatalog)
                {
                    /* User.IsInRole() does not work here reliably because the corrsponding claim is only
                     * set when then token claims to be admin and user is admin. This is to avoid the token
                     * to become too powerful. However, it is OK here, for catalogs, to grant admin access
                     * if user is admin which requires to check for
                     * NexusClaimsHelper.ToPatUserClaimType(Claims.Role).
                     */
                    var isAdmin = identity.Claims
                        .Any(claim => claim.Type == NexusClaimsHelper.ToPatUserClaimType(Claims.Role) && claim.Value == nameof(NexusRoles.Administrator));

                    /* Admins are allowed to access everything */
                    if (isAdmin)
                        return true;

                    /* User is no admin and specific catalog is not enabled */
                    if (!isCatalogEnabled)
                        return false;

                    /* Ensure that user can access the catalog */
                    result = CanUserAccessCatalog(
                        catalogId,
                        catalogMetadata,
                        owner,
                        identity,
                        NexusClaimsHelper.ToPatUserClaimType(singleClaimType),
                        NexusClaimsHelper.ToPatUserClaimType(groupClaimType)
                    );
                }
            }

            /* Other auth schemes */
            else
            {
                var isAdmin = user.IsInRole(nameof(NexusRoles.Administrator));

                /* Admins are allowed to access everything */
                if (isAdmin)
                    return true;

                /* User is no admin and specific catalog is not enabled */
                if (!isCatalogEnabled)
                    return false;

                /* Ensure that user can access the catalog */
                result = CanUserAccessCatalog(
                    catalogId,
                    catalogMetadata,
                    owner,
                    identity,
                    singleClaimType,
                    groupClaimType
                );
            }

            /* Leave loop when access is granted */
            if (result)
                return true;
        }

        return false;
    }

    private static bool CanUserAccessCatalog(
        string catalogId,
        CatalogMetadata catalogMetadata,
        ClaimsPrincipal? owner,
        ClaimsIdentity identity,
        string singleClaimType,
        string groupClaimType
    )
    {
        var isOwner =
            owner is not null &&
            owner?.FindFirstValue(Claims.Subject) == identity.FindFirst(Claims.Subject)?.Value;

        var canAccessCatalog = identity.HasClaim(
            claim =>
                claim.Type == singleClaimType &&
                Regex.IsMatch(catalogId, claim.Value)
        );

        var canAccessCatalogGroup = catalogMetadata.GroupMemberships is not null && identity.HasClaim(
            claim =>
                claim.Type == groupClaimType &&
                catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value))
        );

        return isOwner || canAccessCatalog || canAccessCatalogGroup;
    }
}