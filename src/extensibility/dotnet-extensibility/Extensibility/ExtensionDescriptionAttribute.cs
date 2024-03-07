namespace Nexus.Extensibility;

/// <summary>
/// An attribute to identify the extension.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
public class ExtensionDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionDescriptionAttribute"/>.
    /// </summary>
    /// <param name="description">The extension description.</param>
    /// <param name="projectUrl">An optional project website URL.</param>
    /// <param name="repositoryUrl">An optional source repository URL.</param>
    public ExtensionDescriptionAttribute(string description, string projectUrl, string repositoryUrl)
    {
        Description = description;
        ProjectUrl = projectUrl;
        RepositoryUrl = repositoryUrl;
    }

    /// <summary>
    /// Gets the extension description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the project website URL.
    /// </summary>
    public string ProjectUrl { get; }

    /// <summary>
    /// Gets the source repository URL.
    /// </summary>
    public string RepositoryUrl { get; }
}