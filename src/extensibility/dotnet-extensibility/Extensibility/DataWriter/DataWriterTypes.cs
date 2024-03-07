using Nexus.DataModel;
using System.Text.Json;

namespace Nexus.Extensibility;

/// <summary>
/// The starter package for a data writer.
/// </summary>
/// <param name="ResourceLocator">The resource locator.</param>
/// <param name="SystemConfiguration">The system configuration.</param>
/// <param name="RequestConfiguration">The writer configuration.</param>
public record DataWriterContext(
    Uri ResourceLocator,
    IReadOnlyDictionary<string, JsonElement>? SystemConfiguration,
    IReadOnlyDictionary<string, JsonElement>? RequestConfiguration);

/// <summary>
/// A write request.
/// </summary>
/// <param name="CatalogItem">The catalog item to be written.</param>
/// <param name="Data">The data to be written.</param>
public record WriteRequest(
    CatalogItem CatalogItem,
    ReadOnlyMemory<double> Data);

/// <summary>
/// An attribute to provide additional information about the data writer.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class DataWriterDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataWriterDescriptionAttribute"/>.
    /// </summary>
    /// <param name="description">The data writer description including the data writer format label and UI options.</param>
    public DataWriterDescriptionAttribute(string description)
    {
        Description = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(description);
    }

    internal IReadOnlyDictionary<string, JsonElement>? Description { get; }
}
