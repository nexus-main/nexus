// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Services;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to Git-backed Nexus configuration history.
/// </summary>
[Authorize(Policy = NexusPolicies.RequireAdmin)]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/git")]
internal class GitController(IGitService gitService) : ControllerBase
{
    private readonly IGitService _gitService = gitService;

    /// <summary>
    /// Gets the effective Git configuration without secrets.
    /// </summary>
    [HttpGet("config")]
    public Task<GitConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
    {
        return _gitService.GetConfigAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the current Git status.
    /// </summary>
    [HttpGet("status")]
    public Task<GitStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        return _gitService.GetStatusAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the configuration history.
    /// </summary>
    [HttpGet("history")]
    public Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        return _gitService.GetHistoryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets file-level changes for a commit.
    /// </summary>
    [HttpGet("diff/{commitSha}")]
    public Task<IReadOnlyList<GitDiffFile>> GetDiffAsync(string commitSha, CancellationToken cancellationToken)
    {
        return _gitService.GetDiffAsync(commitSha, cancellationToken);
    }

    /// <summary>
    /// Restores the configuration from a commit by creating a new commit.
    /// </summary>
    [HttpPost("restore")]
    public Task<GitRestoreResponse> RestoreAsync(GitRestoreRequest request, CancellationToken cancellationToken)
    {
        return _gitService.RestoreAsync(request.CommitSha, cancellationToken);
    }
}
