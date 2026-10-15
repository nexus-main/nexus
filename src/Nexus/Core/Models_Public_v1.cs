// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Nexus.DataModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Core.V1;

/// <summary>
/// A personal access token.
/// </summary>
/// <param name="Description">The token description.</param>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Claims">The claims that will be part of the token.</param>
/// <param name="GrantClaims">A snapshot of the creator's claims at the time of token creation, used to validate that the token is not more powerful than its creator.</param>
public record PersonalAccessToken(
    string Description,
    DateTime Expires,
    IReadOnlyList<TokenClaim> Claims,
    IReadOnlyList<TokenClaim> GrantClaims
);

/// <summary>
/// A request to create a personal access token.
/// </summary>
/// <param name="Description">The token description.</param>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Claims">The claims that will be part of the token.</param>
public record CreateTokenRequest(
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
/// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
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
    bool IsVisible,
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
/// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
/// <param name="Disabled">An optional flag which indicates if the pipeline is disabled. By default, pipelines are enabled.</param>
public record DataSourcePipeline(
    IReadOnlyList<DataSourceRegistration> Registrations,
    string? VisibilityPattern = default,
    bool Disabled = false
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
/// <param name="Name">The user name.</param>
/// <param name="Claims">The user claims.</param>
public record MeResponse(
    string UserId,
    string Name,
    IReadOnlyList<TokenClaim> Claims
);

/// <summary>
/// A system response.
/// </summary>
/// <param name="Version">The Nexus version.</param>
/// <param name="ApplicationName">The application name.</param>
/// <param name="HelpLink">The help link.</param>
/// <param name="LogoutUrl">The logout URL.</param>
public record SystemResponse(
    string Version,
    string? ApplicationName,
    string? HelpLink,
    string? LogoutUrl
);

/// <summary>
/// The result of pushing configuration history to a remote Git repository.
/// </summary>
public enum GitPushStatus
{
    /// <summary>
    /// No remote Git repository is configured.
    /// </summary>
    NotConfigured,

    /// <summary>
    /// The push completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The push failed.
    /// </summary>
    Failed
}

/// <summary>
/// The effective Git configuration without secret values.
/// </summary>
/// <param name="Branch">The configured Git branch.</param>
/// <param name="CommitThrottleSeconds">The number of seconds Nexus waits before committing configuration changes.</param>
/// <param name="RemoteUrl">The configured remote Git repository URL.</param>
/// <param name="Username">The configured HTTPS username.</param>
/// <param name="HasToken">A value indicating whether an HTTPS token is configured.</param>
/// <param name="HasSshPrivateKey">A value indicating whether an SSH private key is configured.</param>
/// <param name="AuthMode">The authentication mode inferred from the remote URL.</param>
/// <param name="CommitAuthorName">The Git commit author name.</param>
/// <param name="CommitAuthorEmail">The Git commit author email.</param>
/// <param name="IsRemoteConfigured">A value indicating whether remote backup has enough configuration to push.</param>
public record GitConfigResponse(
    string Branch,
    int CommitThrottleSeconds,
    string? RemoteUrl,
    string? Username,
    bool HasToken,
    bool HasSshPrivateKey,
    string AuthMode,
    string CommitAuthorName,
    string CommitAuthorEmail,
    bool IsRemoteConfigured
);

/// <summary>
/// The current Git repository and push status required by the admin UI.
/// </summary>
/// <param name="GitAvailable">A value indicating whether the Git executable is available.</param>
/// <param name="SshAvailable">A value indicating whether the SSH executable is available.</param>
/// <param name="HasUncommittedChanges">A value indicating whether the local repository has uncommitted changes.</param>
/// <param name="CurrentCommitSha">The current commit SHA.</param>
/// <param name="LastPushedCommitSha">The last commit SHA successfully pushed by this process.</param>
/// <param name="LastSuccessfulPushAt">The last successful push time.</param>
/// <param name="LastPushStatus">The last push status.</param>
/// <param name="LastPushError">The last push error.</param>
public record GitStatusResponse(
    bool GitAvailable,
    bool SshAvailable,
    bool HasUncommittedChanges,
    string? CurrentCommitSha,
    string? LastPushedCommitSha,
    DateTimeOffset? LastSuccessfulPushAt,
    GitPushStatus LastPushStatus,
    string? LastPushError
);

/// <summary>
/// A Git commit in the configuration history.
/// </summary>
/// <param name="Sha">The full commit SHA.</param>
/// <param name="ShortSha">The abbreviated commit SHA.</param>
/// <param name="Date">The commit date.</param>
/// <param name="AuthorName">The commit author name.</param>
/// <param name="AuthorEmail">The commit author email.</param>
/// <param name="Message">The commit message.</param>
public record GitHistoryEntry(
    string Sha,
    string ShortSha,
    DateTimeOffset Date,
    string AuthorName,
    string AuthorEmail,
    string Message
);

/// <summary>
/// A changed file in a Git commit.
/// </summary>
/// <param name="Path">The repository-relative file path.</param>
/// <param name="Status">The Git file status.</param>
/// <param name="OriginalText">The file text before the commit.</param>
/// <param name="ModifiedText">The file text after the commit.</param>
public record GitDiffFile(
    string Path,
    string Status,
    string? OriginalText,
    string? ModifiedText
);

/// <summary>
/// A request to restore configuration from a commit.
/// </summary>
/// <param name="CommitSha">The commit SHA to restore.</param>
public record GitRestoreRequest(
    string CommitSha
);

/// <summary>
/// The result of restoring configuration from a commit.
/// </summary>
/// <param name="CommitSha">The commit SHA created by the restore operation.</param>
/// <param name="Message">A human-readable result message.</param>
public record GitRestoreResponse(
    string CommitSha,
    string Message
);

/// <summary>
/// A request to synchronize local configuration history with the configured remote.
/// </summary>
/// <param name="Force">A value indicating whether to force-push once.</param>
public record GitSyncRequest(
    bool Force
);

/// <summary>
/// The result of synchronizing local configuration history with the configured remote.
/// </summary>
/// <param name="CommitCreated">A value indicating whether a local commit was created.</param>
/// <param name="CommitSha">The current commit SHA.</param>
/// <param name="PushStatus">The remote push status.</param>
/// <param name="CompletedAt">The completion time.</param>
/// <param name="Message">A human-readable result message.</param>
/// <param name="Error">The push error, if any.</param>
public record GitSyncResult(
    bool CommitCreated,
    string? CommitSha,
    GitPushStatus PushStatus,
    DateTimeOffset CompletedAt,
    string Message,
    string? Error
);
