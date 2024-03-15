namespace Nexus.Core;

internal static class NexusClaims
{
    public const string CAN_READ_CATALOG = "CanReadCatalog";
    public const string CAN_WRITE_CATALOG = "CanWriteCatalog";
    public const string CAN_READ_CATALOG_GROUP = "CanReadCatalogGroup";
    public const string CAN_WRITE_CATALOG_GROUP = "CanWriteCatalogGroup";

    public static string ToPatUserClaimType(string claimType)
    {
        return $"pat_user_{claimType}";
    }
}