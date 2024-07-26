// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexus.Extensibility;

namespace Nexus.DataModel;

/// <summary>
/// Contains extension methods to make life easier working with the data model types.
/// </summary>
public static partial class DataModelExtensions
{
    #region Fluent API

    /// <summary>
    /// A constant with the key for a readme property.
    /// </summary>
    public const string ReadmeKey = "readme";

    /// <summary>
    /// A constant with the key for a license property.
    /// </summary>
    public const string LicenseKey = "license";

    /// <summary>
    /// A constant with the key for a description property.
    /// </summary>
    public const string DescriptionKey = "description";

    /// <summary>
    /// A constant with the key for a warning property.
    /// </summary>
    public const string WarningKey = "warning";

    /// <summary>
    /// A constant with the key for a unit property.
    /// </summary>
    public const string UnitKey = "unit";

    /// <summary>
    /// A constant with the key for a groups property.
    /// </summary>
    public const string GroupsKey = "groups";

    /// <summary>
    /// A constant with the key for a original name property.
    /// </summary>
    public const string OriginalNameKey = "original-name";

    internal const string BasePathKey = "base-path";

    /// <summary>
    /// Adds a readme.
    /// </summary>
    /// <param name="catalogBuilder">The catalog builder.</param>
    /// <param name="readme">The markdown readme to add.</param>
    /// <returns>A resource catalog builder.</returns>
    public static ResourceCatalogBuilder WithReadme(this ResourceCatalogBuilder catalogBuilder, string readme)
    {
        return catalogBuilder.WithProperty(ReadmeKey, readme);
    }

    /// <summary>
    /// Adds a license.
    /// </summary>
    /// <param name="catalogBuilder">The catalog builder.</param>
    /// <param name="license">The markdown license to add.</param>
    /// <returns>A resource catalog builder.</returns>
    public static ResourceCatalogBuilder WithLicense(this ResourceCatalogBuilder catalogBuilder, string license)
    {
        return catalogBuilder.WithProperty(LicenseKey, license);
    }

    /// <summary>
    /// Adds a unit.
    /// </summary>
    /// <param name="resourceBuilder">The resource builder.</param>
    /// <param name="unit">The unit to add.</param>
    /// <returns>A resource builder.</returns>
    public static ResourceBuilder WithUnit(this ResourceBuilder resourceBuilder, string unit)
    {
        return resourceBuilder.WithProperty(UnitKey, unit);
    }

    /// <summary>
    /// Adds a description.
    /// </summary>
    /// <param name="resourceBuilder">The resource builder.</param>
    /// <param name="description">The description to add.</param>
    /// <returns>A resource builder.</returns>
    public static ResourceBuilder WithDescription(this ResourceBuilder resourceBuilder, string description)
    {
        return resourceBuilder.WithProperty(DescriptionKey, description);
    }

    /// <summary>
    /// Adds a warning.
    /// </summary>
    /// <param name="resourceBuilder">The resource builder.</param>
    /// <param name="warning">The warning to add.</param>
    /// <returns>A resource builder.</returns>
    public static ResourceBuilder WithWarning(this ResourceBuilder resourceBuilder, string warning)
    {
        return resourceBuilder.WithProperty(WarningKey, warning);
    }

    /// <summary>
    /// Adds groups.
    /// </summary>
    /// <param name="resourceBuilder">The resource builder.</param>
    /// <param name="groups">The groups to add.</param>
    /// <returns>A resource builder.</returns>
    public static ResourceBuilder WithGroups(this ResourceBuilder resourceBuilder, params string[] groups)
    {
        return resourceBuilder.WithProperty(GroupsKey, new JsonArray(groups.Select(group => (JsonNode)group!).ToArray()));
    }

    /// <summary>
    /// Adds an original name property.
    /// </summary>
    /// <param name="resourceBuilder">The resource builder.</param>
    /// <param name="originalName">The original name to add.</param>
    /// <returns>A resource catalog builder.</returns>
    public static ResourceBuilder WithOriginalName(this ResourceBuilder resourceBuilder, string originalName)
    {
        return resourceBuilder.WithProperty(OriginalNameKey, originalName);
    }

    #endregion

    #region Misc

    /// <summary>
    /// Converts a url into a local file path.
    /// </summary>
    /// <param name="url">The url to convert.</param>
    /// <returns>The local file path.</returns>
    public static string ToPath(this Uri url)
    {
        var isRelativeUri = !url.IsAbsoluteUri;

        if (isRelativeUri)
            return url.ToString();

        else if (url.IsFile)
            return url.LocalPath.Replace('\\', '/');

        else
            throw new Exception("Only a file URI can be converted to a path.");
    }

    // keep in sync with Nexus.UI.Utilities ...
    private const int NS_PER_TICK = 100;

    private static readonly long[] _nanoseconds = [(long)1e0, (long)1e3, (long)1e6, (long)1e9, (long)60e9, (long)3600e9, (long)86400e9];

    private static readonly int[] _quotients = [1000, 1000, 1000, 60, 60, 24, 1];

    private static readonly string[] _postFixes = ["ns", "us", "ms", "s", "min", "h", "d"];

    // ... except these lines

    [GeneratedRegex(@"^([0-9]+)_([a-z]+)$")]
    private static partial Regex _unitStringEvaluator { get; }

    internal const string NEXUS_KEY = "nexus";

    internal const string PIPELINE_POSITION_KEY = "pipeline-position";

    /// <summary>
    /// Converts period into a human readable number string with unit.
    /// </summary>
    /// <param name="samplePeriod">The period to convert.</param>
    /// <returns>The human readable number string with unit.</returns>
    public static string ToUnitString(this TimeSpan samplePeriod)
    {
        var currentValue = samplePeriod.Ticks * NS_PER_TICK;

        for (int i = 0; i < _postFixes.Length; i++)
        {
            var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

            if (remainder != 0)
                return $"{currentValue}_{_postFixes[i]}";

            else
                currentValue = quotient;
        }

        return $"{(int)currentValue}_{_postFixes.Last()}";
    }

    // this method is placed here because it requires access to _postFixes and _nanoseconds
    internal static TimeSpan ToSamplePeriod(string unitString)
    {
        var match = _unitStringEvaluator.Match(unitString);

        if (!match.Success)
            throw new Exception("The provided unit string is invalid.");

        var unitIndex = Array.IndexOf(_postFixes, match.Groups[2].Value);

        if (unitIndex == -1)
            throw new Exception("The provided unit is invalid.");

        var totalNanoSeconds = long.Parse(match.Groups[1].Value) * _nanoseconds[unitIndex];

        if (totalNanoSeconds % NS_PER_TICK != 0)
            throw new Exception("The sample period must be a multiple of 100 ns.");

        var ticks = totalNanoSeconds / NS_PER_TICK;

        return new TimeSpan(ticks);
    }

    internal static ResourceCatalog EnsureAndSanitizeMandatoryProperties(
        this ResourceCatalog catalog,
        int pipelinePosition,
        IDataSource[] dataSources
    )
    {
        // catalog resources
        if (catalog.Resources is not null)
        {
            var isModified = false;
            var newResources = new List<Resource>();

            string[] originalNamePath = [OriginalNameKey];
            string[] pipelinePositionPath = [NEXUS_KEY, PIPELINE_POSITION_KEY];
            string[] groupsPath = [GroupsKey];

            foreach (var resource in catalog.Resources)
            {
                var resourceProperties = resource.Properties;
                var newResource = resource;

                var originalName = resourceProperties?.GetStringValue(originalNamePath);
                var currentPipelinePosition = resourceProperties?.GetIntValue(pipelinePositionPath);
                var groups = resourceProperties?.GetStringArray(groupsPath);

                var distinctGroups = groups is null
                    ? default
                    : groups
                        .Where(group => group is not null)
                        .Distinct()
                        .ToList();

                if (originalName is null ||
                    currentPipelinePosition is null ||
                    (distinctGroups is not null && distinctGroups.Count != groups!.Length))
                {
                    var newResourceProperties = resourceProperties is null
                        ? []
                        : resourceProperties!.ToDictionary(entry => entry.Key, entry => entry.Value);

                    // original name
                    if (originalName is null)
                        newResourceProperties[OriginalNameKey] = JsonSerializer.SerializeToElement(resource.Id);

                    // pipeline position
                    if (currentPipelinePosition is null)
                    {
                        var nexusJsonObject = new JsonObject()
                        {
                            [PIPELINE_POSITION_KEY] = pipelinePosition,
                        };

                        newResourceProperties[NEXUS_KEY] = JsonSerializer.SerializeToElement(nexusJsonObject);
                    }

                    // groups
                    if (distinctGroups is not null && distinctGroups.Count != groups!.Length)
                    {
                        var groupJsonArray = new JsonArray();

                        foreach (var group in distinctGroups)
                        {
                            groupJsonArray.Add(group);
                        }

                        newResourceProperties[GroupsKey] = JsonSerializer.SerializeToElement(groupJsonArray);
                    }

                    newResource = resource with
                    {
                        Properties = newResourceProperties
                    };

                    isModified = true;
                }

                newResources.Add(newResource);
            }

            if (isModified)
            {
                catalog = catalog with
                {
                    Resources = newResources
                };
            }
        }

        // catalog
        if (catalog.Properties is null ||
            !catalog.Properties.TryGetValue(NEXUS_KEY, out var _)
        )
        {
            var nexusVersion = typeof(DataModelExtensions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            var jsonPipeline = new JsonArray();

            foreach (var dataSource in dataSources)
            {
                var type = dataSource.GetType();

                var dataSourceVersion = type.Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                    .InformationalVersion;

                var repositoryUrl = type
                    .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false)!
                    .RepositoryUrl;

                var jsonPipelineElement = new JsonObject()
                {
                    ["repository-url"] = repositoryUrl,
                    ["version"] = dataSourceVersion
                };

                jsonPipeline.Add(jsonPipelineElement);
            }

            var nexusJsonObject = new JsonObject()
            {
                ["version"] = nexusVersion,
                ["pipeline"] = jsonPipeline
            };

            var newResourceProperties = catalog.Properties is null
                ? []
                : catalog.Properties.ToDictionary(entry => entry.Key, entry => entry.Value);

            newResourceProperties[NEXUS_KEY] = JsonSerializer.SerializeToElement(nexusJsonObject);

            catalog = catalog with
            {
                Properties = newResourceProperties
            };
        }

        return catalog;
    }

    #endregion
}
