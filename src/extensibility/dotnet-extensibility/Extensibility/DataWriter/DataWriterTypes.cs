// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using System.Text.Json;

namespace Nexus.Extensibility;

/// <summary>
/// The starter package for a data writer.
/// </summary>
/// <param name="ResourceLocator">The resource locator.</param>
/// <param name="RequestConfiguration">The writer configuration.</param>
public record DataWriterContext(
    Uri ResourceLocator,
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
/// <param name="description">The data writer description including the data writer format label and UI options.</param>
[AttributeUsage(AttributeTargets.Class)]
public class DataWriterDescriptionAttribute(string description) : Attribute
{
    internal IReadOnlyDictionary<string, JsonElement>? Description { get; }
        = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(description, JsonSerializerOptions.Web);
}
