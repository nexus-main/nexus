// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.Core;

internal static class NexusClaimsConstants
{
    public const string ENABLED_CATALOGS_PATTERN_CLAIM = "EnabledCatalogsPattern";
}

internal enum NexusClaims
{
    CanReadCatalog,

    CanWriteCatalog,

    CanReadCatalogGroup,

    CanWriteCatalogGroup,

    CanUseResourceLocator
}

internal static class NexusClaimsHelper
{
    public static string ToPatUserClaimType(string claimType)
    {
        return $"pat_user_{claimType}";
    }

    public static string ToPatClaimType(string claimType)
    {
        return $"pat_{claimType}";
    }
}