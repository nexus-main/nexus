// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Utilities;
using OpenIddict.Abstractions;
using System.Runtime.InteropServices;
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
    public void CanApplyRepresentationStatusFloat64()
    {
        // Arrange
        var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new double[status.Length];
        var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatusFloat64<int>(data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    [Fact]
    public void CanApplyRepresentationStatusFloat32()
    {
        // Arrange
        var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new float[status.Length];
        var expected = new float[] { 1, float.NaN, 3, float.NaN, 5, float.NaN, 7, float.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatusFloat32<int>(data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    [Fact]
    public void CanApplyRepresentationStatusFloat64ByType()
    {
        // Arrange
        var data = new CastMemoryManager<int, byte>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 }).Memory;
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new double[status.Length];
        var expected = new double[] { 1, double.NaN, 3, double.NaN, 5, double.NaN, 7, double.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatusFloat64ByDataType(NexusDataType.INT32, data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    [Fact]
    public void CanApplyRepresentationStatusByTypeFloat32()
    {
        // Arrange
        var data = new CastMemoryManager<int, byte>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8 }).Memory;
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        var actual = new float[status.Length];
        var expected = new float[] { 1, float.NaN, 3, float.NaN, 5, float.NaN, 7, float.NaN };

        // Act
        BufferUtilities.ApplyRepresentationStatusFloat32ByDataType(NexusDataType.INT32, data, status, actual);

        // Assert
        Assert.True(expected.SequenceEqual(actual.ToArray()));
    }

    // ===== Comprehensive BufferUtilities tests for all 10 NexusDataType types =====

    private static float[] ComputeExpectedFloat32<T>(T[] data, byte[] status) where T : unmanaged
    {
        var result = new float[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = status[i] != 0 ? GenericToFloat32<T>.ToFloat32(data[i]) : float.NaN;
        return result;
    }

    private static double[] ComputeExpectedFloat64<T>(T[] data, byte[] status) where T : unmanaged
    {
        var result = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = status[i] != 0 ? GenericToFloat64<T>.ToFloat64(data[i]) : double.NaN;
        return result;
    }

    private static void VerifyGenericFloat32<T>(T[] data, byte[] status) where T : unmanaged
    {
        var expected = ComputeExpectedFloat32(data, status);
        var actual = new float[data.Length];
        BufferUtilities.ApplyRepresentationStatusFloat32<T>(data, status, actual);
        Assert.True(expected.SequenceEqual(actual),
            $"Float32 generic mismatch for {typeof(T).Name}");
    }

    private static void VerifyGenericFloat64<T>(T[] data, byte[] status) where T : unmanaged
    {
        var expected = ComputeExpectedFloat64(data, status);
        var actual = new double[data.Length];
        BufferUtilities.ApplyRepresentationStatusFloat64<T>(data, status, actual);
        Assert.True(expected.SequenceEqual(actual),
            $"Float64 generic mismatch for {typeof(T).Name}");
    }

    private static void VerifyByDataTypeFloat32<T>(NexusDataType dataType, T[] data, byte[] status) where T : unmanaged
    {
        var expected = ComputeExpectedFloat32(data, status);
        var dataBytes = new CastMemoryManager<T, byte>(data).Memory;
        var actual = new float[data.Length];
        BufferUtilities.ApplyRepresentationStatusFloat32ByDataType(dataType, dataBytes, status, actual);
        Assert.True(expected.SequenceEqual(actual),
            $"Float32 ByDataType mismatch for {dataType}");
    }

    private static void VerifyByDataTypeFloat64<T>(NexusDataType dataType, T[] data, byte[] status) where T : unmanaged
    {
        var expected = ComputeExpectedFloat64(data, status);
        var dataBytes = new CastMemoryManager<T, byte>(data).Memory;
        var actual = new double[data.Length];
        BufferUtilities.ApplyRepresentationStatusFloat64ByDataType(dataType, dataBytes, status, actual);
        Assert.True(expected.SequenceEqual(actual),
            $"Float64 ByDataType mismatch for {dataType}");
    }

    private static void VerifyAll<T>(NexusDataType dataType, T[] data, byte[] status) where T : unmanaged
    {
        VerifyGenericFloat32(data, status);
        VerifyGenericFloat64(data, status);
        VerifyByDataTypeFloat32(dataType, data, status);
        VerifyByDataTypeFloat64(dataType, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Byte()
    {
        var data = new byte[] { 0, 1, 127, 128, 255, 0, 100, 200 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.UINT8, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_SByte()
    {
        var data = new sbyte[] { -128, -1, 0, 1, 127, -127, 100, -100 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.INT8, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_UShort()
    {
        var data = new ushort[] { 0, 1, 32767, 32768, 65535, 100, 200, 300 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.UINT16, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Short()
    {
        var data = new short[] { -32768, -1, 0, 1, 32767, -100, 100, 200 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.INT16, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_UInt()
    {
        var data = new uint[] { 0u, 1u, 2147483647u, 2147483648u, 4294967295u, 100u, 200u, 300u };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.UINT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Int()
    {
        var data = new int[] { -2147483648, -1, 0, 1, 2147483647, -100, 100, 200 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_ULong()
    {
        var data = new ulong[] { 0ul, 1ul, 9223372036854775807ul, 9223372036854775808ul, 18446744073709551615ul, 100ul, 200ul, 300ul };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.UINT64, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Long()
    {
        var data = new long[] { -9223372036854775808L, -1L, 0L, 1L, 9223372036854775807L, -100L, 100L, 200L };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.INT64, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Float()
    {
        var data = new float[] { -1.5f, 0f, 1.5f, -3.14f, 3.14f, 100.5f, -100.5f, 200.25f };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.FLOAT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_Double()
    {
        var data = new double[] { -1.5, 0.0, 1.5, -3.14, 3.14, 100.5, -100.5, 200.25 };
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };
        VerifyAll(NexusDataType.FLOAT64, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_LargeArray()
    {
        var count = 100;
        var data = new int[count];
        var status = new byte[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = i - 50;
            status[i] = (byte)((i & 1) == 0 ? 1 : 0);
        }
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_AllGoodStatus()
    {
        var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var status = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_AllBadStatus()
    {
        var data = new int[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var status = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_SmallArray()
    {
        var data = new int[] { 1, 2, 3 };
        var status = new byte[] { 1, 0, 1 };
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void CanApplyRepresentationStatus_EmptyArray()
    {
        var data = Array.Empty<int>();
        var status = Array.Empty<byte>();
        VerifyAll(NexusDataType.INT32, data, status);
    }

    [Fact]
    public void ScalarAndVectorizedProduceSameResult_AllTypes()
    {
        var status = new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 };

        CompareScalarVectorized(new byte[] { 0, 1, 127, 128, 255, 0, 100, 200 }, status);
        CompareScalarVectorized(new sbyte[] { -128, -1, 0, 1, 127, -127, 100, -100 }, status);
        CompareScalarVectorized(new ushort[] { 0, 1, 32767, 32768, 65535, 100, 200, 300 }, status);
        CompareScalarVectorized(new short[] { -32768, -1, 0, 1, 32767, -100, 100, 200 }, status);
        CompareScalarVectorized(new uint[] { 0u, 1u, 2147483647u, 2147483648u, 4294967295u, 100u, 200u, 300u }, status);
        CompareScalarVectorized(new int[] { -2147483648, -1, 0, 1, 2147483647, -100, 100, 200 }, status);
        CompareScalarVectorized(new ulong[] { 0ul, 1ul, 9223372036854775807ul, 9223372036854775808ul, 18446744073709551615ul, 100ul, 200ul, 300ul }, status);
        CompareScalarVectorized(new long[] { -9223372036854775808L, -1L, 0L, 1L, 9223372036854775807L, -100L, 100L, 200L }, status);
        CompareScalarVectorized(new float[] { -1.5f, 0f, 1.5f, -3.14f, 3.14f, 100.5f, -100.5f, 200.25f }, status);
        CompareScalarVectorized(new double[] { -1.5, 0.0, 1.5, -3.14, 3.14, 100.5, -100.5, 200.25 }, status);
    }

    private static void CompareScalarVectorized<T>(T[] data, byte[] status) where T : unmanaged
    {
        var scalar32 = new float[data.Length];
        var vectorized32 = new float[data.Length];
        BufferUtilities.ScalarApplyRepresentationStatusFloat32<T>(data, status, scalar32);
        BufferUtilities.ApplyRepresentationStatusFloat32<T>(data, status, vectorized32);
        Assert.True(scalar32.SequenceEqual(vectorized32),
            $"Float32 scalar vs vectorized mismatch for {typeof(T).Name}");

        var scalar64 = new double[data.Length];
        var vectorized64 = new double[data.Length];
        BufferUtilities.ScalarApplyRepresentationStatusFloat64<T>(data, status, scalar64);
        BufferUtilities.ApplyRepresentationStatusFloat64<T>(data, status, vectorized64);
        Assert.True(scalar64.SequenceEqual(vectorized64),
            $"Float64 scalar vs vectorized mismatch for {typeof(T).Name}");
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
        var actual = GenericToFloat64<T>.ToFloat64(value);

        // Assert
        Assert.Equal(expected, actual, precision: 3);
    }

    public static IList<object[]> ToFloat32Data { get; } = new List<object[]>
    {
        new object[]{ (byte)99, (float)99 },
        new object[]{ (sbyte)-99, (float)-99 },
        new object[]{ (ushort)99, (float)99 },
        new object[]{ (short)-99, (float)-99 },
        new object[]{ (uint)99, (float)99 },
        new object[]{ (int)-99, (float)-99 },
        new object[]{ (ulong)99, (float)99 },
        new object[]{ (long)-99, (float)-99 },
        new object[]{ (float)-99.123, (float)-99.123 },
        new object[]{ (double)-99.123, (float)-99.123 },
    };

    [Theory]
    [MemberData(nameof(ToFloat32Data))]
    public void CanGenericConvertToFloat32<T>(T value, float expected)
        where T : unmanaged
    {
        // Act
        var actual = GenericToFloat32<T>.ToFloat32(value);

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