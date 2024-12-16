// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.DataModel;

internal enum RepresentationKind
{
    Original = 0,
    Resampled = 10,
    Mean = 20,
    MeanPolarDeg = 30,
    Min = 40,
    Max = 50,
    Std = 60,
    Rms = 70,
    MinBitwise = 80,
    MaxBitwise = 90,
    Sum = 100
}

/// <summary>
/// Specifies the Nexus data type.
/// </summary>
public enum NexusDataType : ushort
{
    /// <summary>
    /// Unsigned 8-bit integer.
    /// </summary>
    UINT8 = 0x108,

    /// <summary>
    /// Signed 8-bit integer.
    /// </summary>
    INT8 = 0x208,

    /// <summary>
    /// Unsigned 16-bit integer.
    /// </summary>
    UINT16 = 0x110,

    /// <summary>
    /// Signed 16-bit integer.
    /// </summary>
    INT16 = 0x210,

    /// <summary>
    /// Unsigned 32-bit integer.
    /// </summary>
    UINT32 = 0x120,

    /// <summary>
    /// Signed 32-bit integer.
    /// </summary>
    INT32 = 0x220,

    /// <summary>
    /// Unsigned 64-bit integer.
    /// </summary>
    UINT64 = 0x140,

    /// <summary>
    /// Signed 64-bit integer.
    /// </summary>
    INT64 = 0x240,

    /// <summary>
    /// 32-bit floating-point number.
    /// </summary>
    FLOAT32 = 0x320,

    /// <summary>
    /// 64-bit floating-point number.
    /// </summary>
    FLOAT64 = 0x340
}

/// <summary>
/// A catalog item consists of a catalog, a resource and a representation.
/// </summary>
/// <param name="Catalog">The catalog.</param>
/// <param name="Resource">The resource.</param>
/// <param name="Representation">The representation.</param>
/// <param name="Parameters">The optional dictionary of representation parameters and its arguments.</param>
public record CatalogItem(ResourceCatalog Catalog, Resource Resource, Representation Representation, IReadOnlyDictionary<string, string>? Parameters)
{
    /// <summary>
    /// Construct a fully qualified path.
    /// </summary>
    /// <returns>The fully qualified path.</returns>
    public string ToPath()
    {
        var parametersString = DataModelUtilities.GetRepresentationParameterString(Parameters);
        return $"{Catalog.Id}/{Resource.Id}/{Representation.Id}{parametersString}";
    }
}

/// <summary>
/// A catalog registration.
/// </summary>
/// <param name="Path">The absolute or relative path of the catalog.</param>
/// <param name="Title">A nullable title.</param>
/// <param name="IsTransient">An optional boolean which indicates if the catalog and its children should be reloaded on each request.</param>
/// <param name="LinkTarget">An optional link target (i.e. another absolute catalog path) which makes this catalog a softlink.</param>
public record CatalogRegistration(string Path, string? Title, bool IsTransient = false, string? LinkTarget = default)
{
    /// <summary>
    /// Gets the absolute or relative path of the catalog.
    /// </summary>
    public string Path { get; init; } = IsValidPath(Path)
        ? Path
        : throw new ArgumentException($"The catalog path {Path} is not valid.");

    /// <summary>
    /// Gets the nullable title.
    /// </summary>
    public string? Title { get; } = Title;

    /// <summary>
    /// Gets a boolean which indicates if the catalog and its children should be reloaded on each request.
    /// </summary>
    public bool IsTransient { get; } = IsTransient;

    private static bool IsValidPath(string path)
    {
        if (path == "/")
            return true;

        if (!path.StartsWith("/"))
            path = "/" + path;

        var result = ResourceCatalog.ValidIdExpression.IsMatch(path);

        return result;
    }
}

// keep in sync with Nexus.UI.Utilities
internal record ResourcePathParseResult(
    string CatalogId,
    string ResourceId,
    TimeSpan SamplePeriod,
    RepresentationKind Kind,
    string? Parameters,
    TimeSpan? BasePeriod
);
