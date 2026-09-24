// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.Core.V1;

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
