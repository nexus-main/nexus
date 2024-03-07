using Nexus.Core;
using Nexus.Sources;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Utilities
{
    internal static class AuthUtilities
    {
        public static string ComponentsToTokenValue(string secret, string userId)
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
            ClaimsPrincipal user)
        {
            return InternalIsCatalogAccessible(
                catalogId,
                catalogMetadata,
                owner,
                user,
                singleClaimValue: NexusClaims.CAN_READ_CATALOG,
                groupClaimValue: NexusClaims.CAN_READ_CATALOG,
                checkImplicitAccess: true
            );
        }

        public static bool IsCatalogWritable(
            string catalogId, 
            CatalogMetadata catalogMetadata, 
            ClaimsPrincipal user)
        {
            return InternalIsCatalogAccessible(
                catalogId,
                catalogMetadata,
                owner: default,
                user,
                singleClaimValue: NexusClaims.CAN_READ_CATALOG,
                groupClaimValue: NexusClaims.CAN_WRITE_CATALOG,
                checkImplicitAccess: false
            );
        }

        private static bool InternalIsCatalogAccessible(
            string catalogId, 
            CatalogMetadata catalogMetadata, 
            ClaimsPrincipal? owner, 
            ClaimsPrincipal user,
            string singleClaimValue,
            string groupClaimValue,
            bool checkImplicitAccess)
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
                        NexusClaims.ToPatUserClaimType(Claims.Role), 
                        NexusRoles.ADMINISTRATOR);

                    if (isAdmin)
                        return true;

                    /* The token alone can access the catalog ... */
                    var canAccessCatalog = identity.HasClaim(
                        claim => 
                            claim.Type == singleClaimValue &&
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
                            NexusClaims.ToPatUserClaimType(singleClaimValue), 
                            NexusClaims.ToPatUserClaimType(groupClaimValue));
                    }
                }

                /* cookie */
                else
                {
                    var isAdmin = identity.HasClaim(
                        Claims.Role, 
                        NexusRoles.ADMINISTRATOR);

                    if (isAdmin)
                        return true;

                    /* ensure that user can read that catalog */
                    result = CanUserAccessCatalog(
                        catalogId, 
                        catalogMetadata, 
                        owner, 
                        identity, 
                        singleClaimValue, 
                        groupClaimValue);
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
            string singleClaimValue,
            string groupClaimValue
        )
        {
            var isOwner = 
                owner is not null && 
                owner?.FindFirstValue(Claims.Subject) == identity.FindFirst(Claims.Subject)?.Value;

            var canReadCatalog = identity.HasClaim(
                claim => 
                    claim.Type == singleClaimValue &&
                    Regex.IsMatch(catalogId, claim.Value)
            );

            var canReadCatalogGroup = catalogMetadata.GroupMemberships is not null && identity.HasClaim(
                claim => 
                    claim.Type == groupClaimValue &&
                    catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value))
            );

            return isOwner || canReadCatalog || canReadCatalogGroup;
        }
    }
}
