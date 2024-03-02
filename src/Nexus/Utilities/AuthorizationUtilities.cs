using Nexus.Core;
using Nexus.Sources;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Utilities
{
    internal static class AuthorizationUtilities
    {
        public static bool IsCatalogReadable(string catalogId, CatalogMetadata catalogMetadata, ClaimsPrincipal? owner, ClaimsPrincipal user)
        {
            var identity = user.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                if (catalogId == CatalogContainer.RootCatalogId)
                    return true;

                var isAdmin = user.IsInRole(NexusRoles.ADMINISTRATOR);
                var isOwner = owner is not null && owner?.FindFirstValue(Claims.Subject) == user.FindFirstValue(Claims.Subject);

                var canReadCatalog = user.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_READ_CATALOG &&
                    Regex.IsMatch(catalogId, claim.Value));

                var canReadCatalogGroup = catalogMetadata.GroupMemberships is not null && user.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_READ_CATALOG_GROUP &&
                    catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value)));

                var implicitAccess =
                    catalogId == Sample.LocalCatalogId ||
                    catalogId == Sample.RemoteCatalogId;

                return isAdmin || isOwner || canReadCatalog || canReadCatalogGroup || implicitAccess;
            }

            return false;
        }

        public static bool IsCatalogWritable(string catalogId, CatalogMetadata catalogMetadata, ClaimsPrincipal user)
        {
            var identity = user.Identity;

            if (identity is not null && identity.IsAuthenticated)
            {
                var isAdmin = user.IsInRole(NexusRoles.ADMINISTRATOR);

                var canWriteCatalog = user.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_WRITE_CATALOG &&
                    Regex.IsMatch(catalogId, claim.Value));

                var canWriteCatalogGroup = catalogMetadata.GroupMemberships is not null && user.HasClaim(
                    claim => claim.Type == NexusClaims.CAN_WRITE_CATALOG_GROUP &&
                    catalogMetadata.GroupMemberships.Any(group => Regex.IsMatch(group, claim.Value)));

                return isAdmin || canWriteCatalog || canWriteCatalogGroup;
            }

            return false;
        }
    }
}
