// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.Core.V2;

/// <summary>
/// A request to stream multiple resources.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="ResourcePaths">The resource paths to stream.</param>
public record BatchStreamRequest(
    DateTime Begin,
    DateTime End,
    string[] ResourcePaths
);
