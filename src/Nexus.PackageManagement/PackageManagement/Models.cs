// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.PackageManagement;

/// <summary>
/// A package reference.
/// </summary>
/// <param name="Provider">The provider which loads the package.</param>
/// <param name="Configuration">The configuration of the package reference.</param>
public record PackageReference(
    string Provider,
    Dictionary<string, string> Configuration);