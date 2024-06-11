// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.Extensibility;

/// <summary>
/// An attribute to identify the extension.
/// </summary>
/// <param name="description">The extension description.</param>
/// <param name="projectUrl">An optional project website URL.</param>
/// <param name="repositoryUrl">An optional source repository URL.</param>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
public class ExtensionDescriptionAttribute(string description, string projectUrl, string repositoryUrl) : Attribute
{

    /// <summary>
    /// Gets the extension description.
    /// </summary>
    public string Description { get; } = description;

    /// <summary>
    /// Gets the project website URL.
    /// </summary>
    public string ProjectUrl { get; } = projectUrl;

    /// <summary>
    /// Gets the source repository URL.
    /// </summary>
    public string RepositoryUrl { get; } = repositoryUrl;
}