// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Core.V1;

namespace Nexus.Services;

internal interface IGitService
{
    Task<GitConfigResponse> GetConfigAsync(CancellationToken cancellationToken);

    Task<GitStatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GitDiffFile>> GetDiffAsync(string commitSha, CancellationToken cancellationToken);

    Task<GitRestoreResponse> RestoreAsync(string commitSha, CancellationToken cancellationToken);

    Task<GitSyncResult> SyncAsync(bool force, IProgress<double> progress, CancellationToken cancellationToken);
}

internal sealed class GitService(
    IOptions<PathsOptions> pathsOptions,
    IOptionsMonitor<GitOptions> gitOptions,
    ILogger<GitService> logger) : BackgroundService, IGitService
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;
    private readonly IOptionsMonitor<GitOptions> _gitOptions = gitOptions;
    private readonly ILogger<GitService> _logger = logger;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly object _timerLock = new();

    private FileSystemWatcher? _watcher;
    private Timer? _commitTimer;
    private bool _repositoryReady;
    private string? _lastPushedCommitSha;
    private DateTimeOffset? _lastPushAttemptAt;
    private DateTimeOffset? _lastSuccessfulPushAt;
    private GitPushStatus _lastPushStatus = GitPushStatus.NotConfigured;
    private string? _lastPushError;

    public Task<GitConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
    {
        var options = _gitOptions.CurrentValue;

        return Task.FromResult(new GitConfigResponse(
            Branch: options.Branch,
            CommitThrottleSeconds: options.CommitThrottleSeconds,
            RemoteUrl: options.RemoteUrl,
            Username: options.Username,
            HasToken: !string.IsNullOrWhiteSpace(options.Token),
            HasSshPrivateKey: !string.IsNullOrWhiteSpace(options.SshPrivateKey),
            AuthMode: GetAuthMode(options),
            CommitAuthorName: options.CommitAuthorName,
            CommitAuthorEmail: options.CommitAuthorEmail,
            IsRemoteConfigured: IsRemoteConfigured(options)
        ));
    }

    public async Task<GitStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var gitAvailable = await IsGitAvailableAsync(cancellationToken).ConfigureAwait(false);
        var sshAvailable = await IsSshAvailableAsync(cancellationToken).ConfigureAwait(false);

        if (gitAvailable)
            await EnsureRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var hasUncommittedChanges = gitAvailable && _repositoryReady && await HasChangesAsync(cancellationToken).ConfigureAwait(false);
        var currentSha = gitAvailable && _repositoryReady ? await TryRunGitTextAsync("rev-parse HEAD", cancellationToken).ConfigureAwait(false) : null;

        return new GitStatusResponse(
            GitAvailable: gitAvailable,
            SshAvailable: sshAvailable,
            HasUncommittedChanges: hasUncommittedChanges,
            CurrentCommitSha: string.IsNullOrWhiteSpace(currentSha) ? null : currentSha,
            LastPushedCommitSha: _lastPushedCommitSha,
            LastSuccessfulPushAt: _lastSuccessfulPushAt,
            LastPushStatus: _lastPushStatus,
            LastPushError: _lastPushError
        );
    }

    public async Task<IReadOnlyList<GitHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var result = await RunGitAsync("log --date=iso-strict --format=%H%x1f%h%x1f%cI%x1f%an%x1f%ae%x1f%s", cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            return Array.Empty<GitHistoryEntry>();

        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\u001f'))
            .Where(parts => parts.Length == 6 && DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            .Select(parts => new GitHistoryEntry(
                Sha: parts[0],
                ShortSha: parts[1],
                Date: DateTimeOffset.Parse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                AuthorName: parts[3],
                AuthorEmail: parts[4],
                Message: parts[5]
            ))
            .ToArray();
    }

    public async Task<IReadOnlyList<GitDiffFile>> GetDiffAsync(string commitSha, CancellationToken cancellationToken)
    {
        await EnsureRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var parentResult = await RunGitAsync($"rev-parse {QuoteArg(commitSha)}^", cancellationToken).ConfigureAwait(false);
        var hasParent = parentResult.ExitCode == 0;
        var diffCommand = hasParent
            ? $"diff-tree --no-commit-id --name-status -r {QuoteArg(commitSha)}"
            : $"diff-tree --root --no-commit-id --name-status -r {QuoteArg(commitSha)}";
        var diffResult = await RunGitAsync(diffCommand, cancellationToken).ConfigureAwait(false);

        if (diffResult.ExitCode != 0)
            throw new InvalidOperationException(diffResult.StdErr);

        var files = new List<GitDiffFile>();

        foreach (var line in diffResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
                continue;

            var status = parts[0];
            var path = parts[^1];
            var oldSpec = hasParent ? $"{commitSha}^:{path}" : null;
            var newSpec = $"{commitSha}:{path}";
            var oldText = status.StartsWith('A') || oldSpec is null ? null : await TryShowTextAsync(oldSpec, cancellationToken).ConfigureAwait(false);
            var newText = status.StartsWith('D') ? null : await TryShowTextAsync(newSpec, cancellationToken).ConfigureAwait(false);

            files.Add(new GitDiffFile(path, status, oldText, newText));
        }

        return files;
    }

    public async Task<GitRestoreResponse> RestoreAsync(string commitSha, CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureRepositoryCoreAsync(cancellationToken).ConfigureAwait(false);

            foreach (var entry in Directory.EnumerateFileSystemEntries(_pathsOptions.Config))
            {
                if (Path.GetFileName(entry).Equals(".git", StringComparison.Ordinal))
                    continue;

                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }

            await RunGitRequiredAsync($"checkout {QuoteArg(commitSha)} -- .", cancellationToken).ConfigureAwait(false);
            await RunGitRequiredAsync("add --all", cancellationToken).ConfigureAwait(false);

            if (!await HasChangesAsync(cancellationToken).ConfigureAwait(false))
                return new GitRestoreResponse(commitSha, "The selected version already matches the current configuration.");

            var restoreCommit = await CommitAsync($"Restore Nexus configuration from {commitSha}", cancellationToken).ConfigureAwait(false);

            return new GitRestoreResponse(restoreCommit ?? commitSha, $"Restored configuration from {commitSha}.");
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public async Task<GitSyncResult> SyncAsync(bool force, IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            progress.Report(0.1);
            await EnsureRepositoryCoreAsync(cancellationToken).ConfigureAwait(false);

            progress.Report(0.35);
            var commitSha = await CommitCurrentStateIfChangedAsync("Update Nexus configuration", cancellationToken).ConfigureAwait(false);
            var commitCreated = commitSha is not null;
            commitSha ??= await TryRunGitTextAsync("rev-parse HEAD", cancellationToken).ConfigureAwait(false);

            progress.Report(0.65);
            var pushStatus = await PushIfConfiguredAsync(force, cancellationToken).ConfigureAwait(false);

            progress.Report(1);
            return new GitSyncResult(
                CommitCreated: commitCreated,
                CommitSha: commitSha,
                PushStatus: pushStatus,
                CompletedAt: DateTimeOffset.UtcNow,
                Message: GetSyncMessage(commitCreated, pushStatus),
                Error: pushStatus == GitPushStatus.Failed ? _lastPushError : null
            );
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await IsGitAvailableAsync(stoppingToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Git executable not found. Nexus configuration history is disabled.");
            return;
        }

        try
        {
            await EnsureRepositoryAsync(stoppingToken).ConfigureAwait(false);
            StartWatcher();
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to start Git service.");
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _commitTimer?.Dispose();
        _semaphoreSlim.Dispose();
        base.Dispose();
    }

    private async Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await EnsureRepositoryCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task EnsureRepositoryCoreAsync(CancellationToken cancellationToken)
    {
        if (_repositoryReady)
            return;

        Directory.CreateDirectory(_pathsOptions.Config);

        var isNewRepository = !Directory.Exists(Path.Combine(_pathsOptions.Config, ".git"));

        if (isNewRepository)
            await RunGitRequiredAsync("init", cancellationToken).ConfigureAwait(false);

        var options = _gitOptions.CurrentValue;
        await RunGitRequiredAsync($"config user.name {QuoteArg(options.CommitAuthorName)}", cancellationToken).ConfigureAwait(false);
        await RunGitRequiredAsync($"config user.email {QuoteArg(options.CommitAuthorEmail)}", cancellationToken).ConfigureAwait(false);
        await RunGitRequiredAsync($"checkout -B {QuoteArg(options.Branch)}", cancellationToken).ConfigureAwait(false);

        if (isNewRepository)
            await CommitCurrentStateIfChangedAsync("Initial commit", cancellationToken).ConfigureAwait(false);

        _repositoryReady = true;
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_pathsOptions.Config)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnConfigChanged;
        _watcher.Created += OnConfigChanged;
        _watcher.Deleted += OnConfigChanged;
        _watcher.Renamed += OnConfigChanged;
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        if (IsGitInternalPath(e.FullPath))
            return;

        lock (_timerLock)
        {
            var dueTime = TimeSpan.FromSeconds(Math.Max(1, _gitOptions.CurrentValue.CommitThrottleSeconds));
            _commitTimer ??= new Timer(OnCommitTimerElapsed);
            _commitTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnCommitTimerElapsed(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SyncAsync(force: false, new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to commit Nexus configuration changes.");
            }
        });
    }

    private async Task<string?> CommitCurrentStateIfChangedAsync(string message, CancellationToken cancellationToken)
    {
        await RunGitRequiredAsync("add --all", cancellationToken).ConfigureAwait(false);

        if (!await HasChangesAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return await CommitAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> CommitAsync(string message, CancellationToken cancellationToken)
    {
        var options = _gitOptions.CurrentValue;
        var result = await RunGitAsync(
            $"-c user.name={QuoteArg(options.CommitAuthorName)} -c user.email={QuoteArg(options.CommitAuthorEmail)} commit -m {QuoteArg(message)}",
            cancellationToken
        ).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.StdErr);

        return await TryRunGitTextAsync("rev-parse HEAD", cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitPushStatus> PushIfConfiguredAsync(bool force, CancellationToken cancellationToken)
    {
        var options = _gitOptions.CurrentValue;

        if (!IsRemoteConfigured(options))
        {
            _lastPushStatus = GitPushStatus.NotConfigured;
            _lastPushError = null;
            return GitPushStatus.NotConfigured;
        }

        _lastPushAttemptAt = DateTimeOffset.UtcNow;

        using var sshKey = CreateSshKeyFileIfNeeded(options);
        var remoteUrl = BuildRemoteUrl(options);
        var forceArgument = force ? " --force" : string.Empty;
        var result = await RunGitAsync($"push{forceArgument} {QuoteArg(remoteUrl)} HEAD:{QuoteArg(options.Branch)}", cancellationToken, sshKey).ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            _lastPushedCommitSha = await TryRunGitTextAsync("rev-parse HEAD", cancellationToken).ConfigureAwait(false);
            _lastSuccessfulPushAt = DateTimeOffset.UtcNow;
            _lastPushStatus = GitPushStatus.Succeeded;
            _lastPushError = null;
            return GitPushStatus.Succeeded;
        }

        _lastPushStatus = GitPushStatus.Failed;
        _lastPushError = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return GitPushStatus.Failed;
    }

    private async Task<bool> HasChangesAsync(CancellationToken cancellationToken)
    {
        var result = await RunGitAsync("status --porcelain", cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(result.StdOut);
    }

    private async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_pathsOptions.Config);
            var result = await RunProcessAsync("git", "--version", _pathsOptions.Config, null, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsSshAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_pathsOptions.Config);
            var result = await RunProcessAsync("ssh", "-V", _pathsOptions.Config, null, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private Task<ProcessResult> RunGitAsync(string arguments, CancellationToken cancellationToken, TempFile? sshKey = null)
    {
        var environment = sshKey is null
            ? null
            : new Dictionary<string, string?>
            {
                ["GIT_SSH_COMMAND"] = $"ssh -i {QuoteShellPath(sshKey.Path)} -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new"
            };

        return RunProcessAsync("git", arguments, _pathsOptions.Config, environment, cancellationToken);
    }

    private async Task RunGitRequiredAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(arguments, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.StdErr);
    }

    private async Task<string?> TryRunGitTextAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    private async Task<string?> TryShowTextAsync(string spec, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync($"show {QuoteArg(spec)}", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut : null;
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDirectory, IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        if (environment is not null)
        {
            foreach (var item in environment)
                startInfo.Environment[item.Key] = item.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start process {fileName}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static string GetAuthMode(GitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RemoteUrl))
            return "None";

        if (IsSshUrl(options.RemoteUrl))
            return "SshPrivateKey";

        if (options.RemoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "HttpsToken";

        return "Unsupported";
    }

    private static bool IsRemoteConfigured(GitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RemoteUrl) || string.IsNullOrWhiteSpace(options.Branch))
            return false;

        if (IsSshUrl(options.RemoteUrl))
            return !string.IsNullOrWhiteSpace(options.SshPrivateKey);

        if (options.RemoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(options.Token);

        return false;
    }

    private static bool IsSshUrl(string remoteUrl)
    {
        return remoteUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) || remoteUrl.Contains('@') && remoteUrl.Contains(':');
    }

    private static string BuildRemoteUrl(GitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RemoteUrl))
            throw new InvalidOperationException("Remote URL is not configured.");

        if (!options.RemoteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(options.Token))
            return options.RemoteUrl;

        var builder = new UriBuilder(options.RemoteUrl)
        {
            UserName = Uri.EscapeDataString(string.IsNullOrWhiteSpace(options.Username) ? "x-access-token" : options.Username),
            Password = Uri.EscapeDataString(options.Token)
        };

        return builder.Uri.AbsoluteUri;
    }

    private static TempFile? CreateSshKeyFileIfNeeded(GitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SshPrivateKey) || string.IsNullOrWhiteSpace(options.RemoteUrl) || !IsSshUrl(options.RemoteUrl))
            return null;

        var path = Path.Combine(Path.GetTempPath(), $"nexus-git-{Guid.NewGuid():N}");
        File.WriteAllText(path, options.SshPrivateKey);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return new TempFile(path);
    }

    private static bool IsGitInternalPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".git", StringComparer.Ordinal);
    }

    private static string GetSyncMessage(bool commitCreated, GitPushStatus pushStatus)
    {
        return pushStatus switch
        {
            GitPushStatus.NotConfigured => commitCreated ? "Committed local configuration changes." : "No local configuration changes to commit.",
            GitPushStatus.Succeeded => commitCreated ? "Committed and pushed configuration changes." : "Pushed current configuration commit.",
            GitPushStatus.Failed => commitCreated ? "Committed configuration changes, but push failed." : "Push failed.",
            _ => "Git sync completed."
        };
    }

    private static string QuoteArg(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteShellPath(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed class TempFile(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
            }
        }
    }
}
