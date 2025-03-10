// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Utilities;
using OpenIddict.Abstractions;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Other;

public class UtilitiesTests
{
    [Theory]

    [InlineData("Basic", true, "", new string[0], new string[0], true)]
    [InlineData("Basic", false, "", new string[] { "/D/E/F", "/A/B/C", "/G/H/I" }, new string[0], true)]
    [InlineData("Basic", false, "", new string[] { "^/A/B/.*" }, new string[0], true)]
    [InlineData("Basic", false, "", new string[0], new string[] { "A" }, true)]
    [InlineData("Basic", false, "/A", new string[] { "/A/B/C" }, new string[0], true)]

    [InlineData("Basic", false, "", new string[0], new string[0], false)]
    [InlineData("Basic", false, "", new string[] { "/D/E/F", "/A/B/C2", "/G/H/I" }, new string[0], false)]
    [InlineData("Basic", false, "", new string[0], new string[] { "A2" }, false)]
    [InlineData("Basic", false, "/A2", new string[] { "/A/B/C" }, new string[0], false)]
    [InlineData(null, true, "", new string[0], new string[0], false)]
    public void CanDetermineCatalogReadability(
        string? authenticationType,
        bool isAdmin,
        string enabledCatalogsPattern,
        string[] canReadCatalog,
        string[] canReadCatalogGroup,
        bool expected
    )
    {
        // Arrange
        var catalogId = "/A/B/C";
        var catalogMetadata = new CatalogMetadata(default, GroupMemberships: ["A"], default);

        var adminClaim = isAdmin
            ? [new Claim(Claims.Role, nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims: adminClaim
                    .Concat(canReadCatalog.Select(value => new Claim(nameof(NexusClaims.CanReadCatalog), value)))
                    .Concat(canReadCatalogGroup.Select(value => new Claim(nameof(NexusClaims.CanReadCatalogGroup), value))),
                authenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role
            )
        );

        principal.AddClaim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, enabledCatalogsPattern);

        // Act
        var actual = AuthUtilities.IsCatalogReadable(catalogId, catalogMetadata, default!, principal);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, true, "", new string[0], new string[0], new string[0], true)]
    [InlineData(false, false, "", new string[] { "/A/B/C" }, new string[0], new string[] { "/A/B/" }, true)]

    [InlineData(true, false, "", new string[0], new string[0], new string[0], false)]
    [InlineData(false, true, "", new string[0], new string[0], new string[0], false)]
    [InlineData(false, false, "", new string[] { "/A/B/C" }, new string[0], new string[] { "/D/E/" }, false)]
    public void CanDetermineCatalogReadability_PAT(
        bool isAdmin,
        bool claimsToBeAdmin,
        string enabledCatalogsPattern,
        string[] patCanReadCatalog,
        string[] patCanReadCatalogGroup,
        string[] patUserCanReadCatalog,
        bool expected
    )
    {
        // Arrange
        var catalogId = "/A/B/C";
        var catalogMetadata = new CatalogMetadata(default, GroupMemberships: ["A"], default);

        var adminClaim = isAdmin
            ? [new Claim(NexusClaimsHelper.ToPatUserClaimType(Claims.Role), nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var claimsToBeAdminClaim = claimsToBeAdmin
            ? [new Claim(NexusClaimsHelper.ToPatClaimType(Claims.Role), nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims: adminClaim
                    .Concat(claimsToBeAdminClaim)
                    .Concat(patCanReadCatalog.Select(value => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanReadCatalog)), value)))
                    .Concat(patCanReadCatalogGroup.Select(value => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanReadCatalogGroup)), value)))
                    .Concat(patUserCanReadCatalog.Select(value => new Claim(NexusClaimsHelper.ToPatUserClaimType(nameof(NexusClaims.CanReadCatalog)), value))),
                PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role
            )
        );

        principal.AddClaim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, enabledCatalogsPattern);

        // Act
        var actual = AuthUtilities.IsCatalogReadable(catalogId, catalogMetadata, default!, principal);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]

    [InlineData("Basic", true, "", new string[0], new string[0], true)]
    [InlineData("Basic", false, "", new string[] { "/D/E/F", "/A/B/C", "/G/H/I" }, new string[0], true)]
    [InlineData("Basic", false, "", new string[] { "^/A/B/.*" }, new string[0], true)]
    [InlineData("Basic", false, "", new string[0], new string[] { "A" }, true)]
    [InlineData("Basic", false, "/A", new string[] { "/A/B/C" }, new string[0], true)]

    [InlineData("Basic", false, "", new string[0], new string[0], false)]
    [InlineData("Basic", false, "", new string[] { "/D/E/F", "/A/B/C2", "/G/H/I" }, new string[0], false)]
    [InlineData("Basic", false, "", new string[0], new string[] { "A2" }, false)]
    [InlineData("Basic", false, "/A2", new string[] { "/A/B/C" }, new string[0], false)]
    [InlineData(null, true, "", new string[0], new string[0], false)]
    public void CanDetermineCatalogWritability(
        string? authenticationType,
        bool isAdmin,
        string enabledCatalogsPattern,
        string[] canWriteCatalog,
        string[] canWriteCatalogGroup,
        bool expected
    )
    {
        // Arrange
        var catalogId = "/A/B/C";
        var catalogMetadata = new CatalogMetadata(default, GroupMemberships: ["A"], default);

        var adminClaim = isAdmin
            ? [new Claim(Claims.Role, nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims: adminClaim
                    .Concat(canWriteCatalog.Select(value => new Claim(nameof(NexusClaims.CanWriteCatalog), value)))
                    .Concat(canWriteCatalogGroup.Select(value => new Claim(nameof(NexusClaims.CanWriteCatalogGroup), value))),
                authenticationType,
                nameType: Claims.Name,
                roleType: Claims.Role
            )
        );

        principal.AddClaim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, enabledCatalogsPattern);

        // Act
        var actual = AuthUtilities.IsCatalogWritable(catalogId, catalogMetadata, principal);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]

    [InlineData(true, true, "", new string[0], new string[0], new string[0], true)]
    [InlineData(false, false, "", new string[] { "/A/B/C" }, new string[0], new string[] { "/A/B/" }, true)]

    [InlineData(true, false, "", new string[0], new string[0], new string[0], false)]
    [InlineData(false, true, "", new string[0], new string[0], new string[0], false)]
    [InlineData(false, false, "", new string[] { "/A/B/C" }, new string[0], new string[] { "/D/E/" }, false)]
    public void CanDetermineCatalogWritability_PAT(
        bool isAdmin,
        bool claimsToBeAdmin,
        string enabledCatalogsPattern,
        string[] canWriteCatalog,
        string[] canWriteCatalogGroup,
        string[] patUserCanWriteCatalog,
        bool expected
    )
    {
        // Arrange
        var catalogId = "/A/B/C";
        var catalogMetadata = new CatalogMetadata(default, GroupMemberships: ["A"], default);

        var adminClaim = isAdmin
            ? [new Claim(NexusClaimsHelper.ToPatUserClaimType(Claims.Role), nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var claimsToBeAdminClaim = claimsToBeAdmin
            ? [new Claim(NexusClaimsHelper.ToPatClaimType(Claims.Role), nameof(NexusRoles.Administrator))]
            : Array.Empty<Claim>();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims: adminClaim
                    .Concat(claimsToBeAdminClaim)
                    .Concat(canWriteCatalog.Select(value => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanWriteCatalog)), value)))
                    .Concat(canWriteCatalogGroup.Select(value => new Claim(NexusClaimsHelper.ToPatClaimType(nameof(NexusClaims.CanWriteCatalogGroup)), value)))
                    .Concat(patUserCanWriteCatalog.Select(value => new Claim(NexusClaimsHelper.ToPatUserClaimType(nameof(NexusClaims.CanWriteCatalog)), value))),
                PersonalAccessTokenAuthenticationDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role
            )
        );

        principal.AddClaim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, enabledCatalogsPattern);

        // Act
        var actual = AuthUtilities.IsCatalogWritable(catalogId, catalogMetadata, principal);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CanApplyRepresentationStatus()
    {
        // Arrange
        var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new double[status.Length];
        var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatus<int>(data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    [Fact]
    public void CanApplyRepresentationStatusByType()
    {
        // Arrange
        var data = new CastMemoryManager<int, byte>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 }).Memory;
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new double[status.Length];
        var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatusByDataType(NexusDataType.INT32, data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    public static IList<object[]> ToDoubleData { get; } = new List<object[]>
    {
        new object[]{ (byte)99, (double)99 },
        new object[]{ (sbyte)-99, (double)-99 },
        new object[]{ (ushort)99, (double)99 },
        new object[]{ (short)-99, (double)-99 },
        new object[]{ (uint)99, (double)99 },
        new object[]{ (int)-99, (double)-99 },
        new object[]{ (ulong)99, (double)99 },
        new object[]{ (long)-99, (double)-99 },
        new object[]{ (float)-99.123, (double)-99.123 },
        new object[]{ (double)-99.123, (double)-99.123 },
    };

    [Theory]
    [MemberData(nameof(ToDoubleData))]
    public void CanGenericConvertToDouble<T>(T value, double expected)
        where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
    {
        // Act
        var actual = GenericToDouble<T>.ToDouble(value);

        // Assert
        Assert.Equal(expected, actual, precision: 3);
    }

    public static IList<object[]> BitOrData { get; } = new List<object[]>
    {
        new object[]{ (byte)3, (byte)4, (byte)7 },
        new object[]{ (sbyte)-2, (sbyte)-3, (sbyte)-1 },
        new object[]{ (ushort)3, (ushort)4, (ushort)7 },
        new object[]{ (short)-2, (short)-3, (short)-1 },
        new object[]{ (uint)3, (uint)4, (uint)7 },
        new object[]{ (int)-2, (int)-3, (int)-1 },
        new object[]{ (ulong)3, (ulong)4, (ulong)7 },
        new object[]{ (long)-2, (long)-3, (long)-1 },
    };

    [Theory]
    [MemberData(nameof(BitOrData))]
    public void CanGenericBitOr<T>(T a, T b, T expected)
       where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
    {
        // Act
        var actual = GenericBitOr<T>.BitOr(a, b);

        // Assert
        Assert.Equal(expected, actual);
    }

    public static IList<object[]> BitAndData { get; } = new List<object[]>
    {
        new object[]{ (byte)168, (byte)44, (byte)40 },
        new object[]{ (sbyte)-88, (sbyte)44, (sbyte)40 },
        new object[]{ (ushort)168, (ushort)44, (ushort)40 },
        new object[]{ (short)-88, (short)44, (short)40 },
        new object[]{ (uint)168, (uint)44, (uint)40 },
        new object[]{ (int)-88, (int)44, (int)40 },
        new object[]{ (ulong)168, (ulong)44, (ulong)40 },
        new object[]{ (long)-88, (long)44, (long)40 },
    };

    [Theory]
    [MemberData(nameof(BitAndData))]
    public void CanGenericBitAnd<T>(T a, T b, T expected)
       where T : unmanaged //, IEqualityComparer<T> (does not compile correctly)
    {
        // Act
        var actual = GenericBitAnd<T>.BitAnd(a, b);

        // Assert
        Assert.Equal(expected, actual);
    }

    record MyType(int A, string B, TimeSpan C);

    [Fact]
    public void CanSerializeAndDeserializeTimeSpan()
    {
        // Arrange
        var expected = new MyType(A: 1, B: "Two", C: TimeSpan.FromSeconds(1));

        // Act
        var jsonString = JsonSerializerHelper.SerializeIndented(expected);
        var actual = JsonSerializer.Deserialize<MyType>(jsonString, JsonSerializerOptions.Web);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CanCastMemory()
    {
        // Arrange
        var values = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var expected = new int[] { 67305985, 134678021 };

        // Act
        var actual = new CastMemoryManager<byte, int>(values).Memory;

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }


    [Fact]
    public void CanDetermineSizeOfNexusDataType()
    {
        // Arrange
        var values = NexusUtilities.GetEnumValues<NexusDataType>();
        var expected = new[] { 1, 2, 4, 8, 1, 2, 4, 8, 4, 8 };

        // Act
        var actual = values.Select(value => NexusUtilities.SizeOf(value));

        // Assert
        Assert.Equal(expected, actual);
    }
}