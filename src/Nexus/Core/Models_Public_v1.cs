// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Nexus.DataModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core.V1;

/// <summary>
/// Represents a user.
/// </summary>
public class NexusUser(
    string id,
    string name)
{
    /// <inheritdoc/>
    [JsonIgnore]
    [ValidateNever]
    public string Id { get; set; } = id;

    /// <summary>
    /// The user name.
    /// </summary>
    public string Name { get; set; } = name;

#pragma warning disable CS1591

    [JsonIgnore]
    public List<NexusClaim> Claims { get; set; } = [];

#pragma warning restore CS1591

}

/// <summary>
/// Represents a claim.
/// </summary>
public class NexusClaim(Guid id, string type, string value)
{
    /// <inheritdoc/>
    [JsonIgnore]
    [ValidateNever]
    public Guid Id { get; set; } = id;

    /// <summary>
    /// The claim type.
    /// </summary>
    public string Type { get; init; } = type;

    /// <summary>
    /// The claim value.
    /// </summary>
    public string Value { get; init; } = value;

#pragma warning disable CS1591

    // https://learn.microsoft.com/en-us/ef/core/modeling/relationships?tabs=fluent-api%2Cfluent-api-simple-key%2Csimple-key#no-foreign-key-property
    [JsonIgnore]
    [ValidateNever]
    public NexusUser Owner { get; set; } = default!;

#pragma warning restore CS1591
}

/// <summary>
/// A personal access token.
/// </summary>
/// <param name="Description">The token description.</param>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Claims">The claims that will be part of the token.</param>
public record PersonalAccessToken(
    string Description,
    DateTime Expires,
    IReadOnlyList<TokenClaim> Claims
);

/// <summary>
/// A revoke token request.
/// </summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public record TokenClaim(
    string Type,
    string Value
);

/// <summary>
/// Describes an OpenID connect provider.
/// </summary>
/// <param name="Scheme">The scheme.</param>
/// <param name="DisplayName">The display name.</param>
public record AuthenticationSchemeDescription(
    string Scheme,
    string DisplayName
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
public record ExportParameters(
    DateTime Begin,
    DateTime End,
    TimeSpan FilePeriod,
    string? Type,
    string[] ResourcePaths,
    IReadOnlyDictionary<string, JsonElement>? Configuration
);

/// <summary>
/// An extension description.
/// </summary>
/// <param name="Type">The extension type.</param>
/// <param name="Version">The extension version.</param>
/// <param name="Description">A nullable description.</param>
/// <param name="ProjectUrl">A nullable project website URL.</param>
/// <param name="RepositoryUrl">A nullable source repository URL.</param>
/// <param name="AdditionalInformation">Additional information about the extension.</param>
public record ExtensionDescription(
    string Type,
    string Version,
    string? Description,
    string? ProjectUrl,
    string? RepositoryUrl,
    IReadOnlyDictionary<string, JsonElement> AdditionalInformation
);

/// <summary>
/// A structure for catalog information.
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="Title">A nullable title.</param>
/// <param name="Contact">A nullable contact.</param>
/// <param name="Readme">A nullable readme.</param>
/// <param name="License">A nullable license.</param>
/// <param name="IsReadable">A boolean which indicates if the catalog is accessible.</param>
/// <param name="IsWritable">A boolean which indicates if the catalog is editable.</param>
/// <param name="IsReleased">A boolean which indicates if the catalog is released.</param>
/// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
/// <param name="IsOwner">A boolean which indicates if the catalog is owned by the current user.</param>
/// <param name="PackageReferenceIds">The package reference identifiers.</param>
/// <param name="PipelineInfo">A structure for pipeline info.</param>
public record CatalogInfo(
    string Id,
    string? Title,
    string? Contact,
    string? Readme,
    string? License,
    bool IsReadable,
    bool IsWritable,
    bool IsReleased,
    bool IsVisible,
    bool IsOwner,
    Guid[] PackageReferenceIds,
    PipelineInfo PipelineInfo
);

/// <summary>
/// A structure for pipeline information.
/// </summary>
/// <param name="Id">The pipeline identifier.</param>
/// <param name="Types">An array of data source types.</param>
/// <param name="InfoUrls">An array of data source info URLs.</param>
public record PipelineInfo(
    Guid Id,
    string[] Types,
    string?[] InfoUrls
);

/// <summary>
/// A structure for catalog metadata.
/// </summary>
/// <param name="Contact">The contact.</param>
/// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
/// <param name="Overrides">Overrides for the catalog.</param>
public record CatalogMetadata(
    string? Contact,
    string[]? GroupMemberships,
    ResourceCatalog? Overrides
);

/// <summary>
/// The catalog availability.
/// </summary>
/// <param name="Data">The actual availability data.</param>
public record CatalogAvailability(
    double[] Data
);

/// <summary>
/// A data source pipeline.
/// </summary>
/// <param name="Registrations">The list of pipeline elements (data source registrations).</param>
/// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
/// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
public record DataSourcePipeline(
    IReadOnlyList<DataSourceRegistration> Registrations,
    string? ReleasePattern = default,
    string? VisibilityPattern = default
);

/// <summary>
/// A data source registration.
/// </summary>
/// <param name="Type">The type of the data source.</param>
/// <param name="ResourceLocator">An optional URL which points to the data.</param>
/// <param name="Configuration">Configuration parameters for the instantiated source.</param>
/// <param name="InfoUrl">An optional info URL.</param>
public record DataSourceRegistration(
    string Type,
    Uri? ResourceLocator,
    JsonElement Configuration,
    string? InfoUrl = default
);

/// <summary>
/// Description of a job.
/// </summary>
/// <param name="Id">The global unique identifier.</param>
/// <param name="Owner">The owner of the job.</param>
/// <param name="Type">The job type.</param>
/// <param name="Parameters">The job parameters.</param>
public record Job(
    Guid Id,
    string Type,
    string Owner,
    object? Parameters
);

/// <summary>
/// Describes the status of the job.
/// </summary>
/// <param name="Start">The start date/time.</param>
/// <param name="Status">The status.</param>
/// <param name="Progress">The progress from 0 to 1.</param>
/// <param name="ExceptionMessage">The nullable exception message.</param>
/// <param name="Result">The nullable result.</param>
public record JobStatus(
    DateTime Start,
    TaskStatus Status,
    double Progress,
    string? ExceptionMessage,
    object? Result
);

/// <summary>
/// A me response.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="User">The user.</param>
/// <param name="IsAdmin">A boolean which indicates if the user is an administrator.</param>
/// <param name="PersonalAccessTokens">A list of personal access tokens.</param>
public record MeResponse(
    string UserId,
    NexusUser User,
    bool IsAdmin,
    IReadOnlyDictionary<Guid, PersonalAccessToken> PersonalAccessTokens
);
