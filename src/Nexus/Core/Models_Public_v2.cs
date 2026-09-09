// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Nexus.DataModel;

namespace Nexus.Core.V2;

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
