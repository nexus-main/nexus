// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Sources;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Utilities;

internal static class AuthUtilities
{
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
                var isAdmin = identity.HasClaim(
                    NexusClaimsHelper.ToPatUserClaimType(Claims.Role),
                    nameof(NexusRoles.Administrator));

                if (isAdmin)
                    return true;

                /* The token alone can access the catalog ... */
                var canAccessCatalog = identity.HasClaim(
                    claim =>
                        claim.Type == singleClaimType &&
                        Regex.IsMatch(catalogId, claim.Value)
                );

                /* ... but it cannot be more powerful than the
                 * user itself, so next step is to ensure that
                 * the user can access that catalog as well. */
                if (canAccessCatalog)
                {
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

            /* cookie */
            else
            {
                var isAdmin = identity.HasClaim(
                    Claims.Role,
                    nameof(NexusRoles.Administrator));

                if (isAdmin)
                    return true;

                /* ensure that user can read that catalog */
                result = CanUserAccessCatalog(
                    catalogId,
                    catalogMetadata,
                    owner,
                    identity,
                    singleClaimType,
                    groupClaimType
                );
            }

            /* leave loop when access is granted */
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
        var oidcProvider = identity.BootstrapContext as OpenIdConnectProvider;

        var enabledCatalogsPattern = oidcProvider is null /* This is the case for Nexus internal users */
            ? ""
            : oidcProvider.EnabledCatalogsPattern;

        var isCatalogEnabledForScheme = Regex.IsMatch(catalogId, enabledCatalogsPattern);

        if (!isCatalogEnabledForScheme)
            return false;

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