// MIT License
// Copyright (c) [2024] [nexus-main]

using System.ComponentModel.DataAnnotations;

namespace Nexus.Core.V2;

/// <summary>
/// A request to stream multiple resources.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="ResourcePaths">The resource paths to stream.</param>
public record BatchStreamRequest(
    [property: Required] DateTime Begin,
    [property: Required] DateTime End,
    [property: Required] string[] ResourcePaths
);
