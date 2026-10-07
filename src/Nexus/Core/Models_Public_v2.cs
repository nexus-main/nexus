// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Nexus.DataModel;

namespace Nexus.Core.V2;

/// <summary>A Float32 visualization request over a common, half-open sample domain.</summary>
/// <param name="Begin">The domain start.</param>
/// <param name="End">The exclusive domain end.</param>
/// <param name="ResourcePaths">The resources, all with the same sample period.</param>
/// <param name="Views">The requested replacement views.</param>
public record VisualizationRequest(DateTime Begin, DateTime End, string[] ResourcePaths, VisualizationView[] Views);

/// <summary>A half-open viewport with a hard output point budget, including endpoints.</summary>
/// <param name="Id">The unique view identifier.</param>
/// <param name="Begin">The viewport start.</param>
/// <param name="End">The exclusive viewport end.</param>
/// <param name="MaxPoints">The maximum number of points (5 through 32768).</param>
public record VisualizationView(string Id, DateTime Begin, DateTime End, int MaxPoints);

/// <summary>
/// A request to stream multiple resources.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="ResourcePaths">The resource paths to stream.</param>
/// <param name="Precision">The floating point precision used for streamed sample values.</param>
public record BatchStreamRequest(
    DateTime Begin,
    DateTime End,
    string[] ResourcePaths,
    Precision Precision
);

/// <summary>
/// A structure for export parameters.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="FilePeriod">The file period.</param>
/// <param name="Type">The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.</param>
/// <param name="ResourcePaths">The resource paths to export.</param>
/// <param name="Configuration">The configuration.</param>
/// <param name="Precision">The floating point precision used for exported sample values.</param>
public record ExportParameters(
    DateTime Begin,
    DateTime End,
    TimeSpan FilePeriod,
    string? Type,
    string[] ResourcePaths,
    IReadOnlyDictionary<string, JsonElement>? Configuration,
    Precision Precision
);
