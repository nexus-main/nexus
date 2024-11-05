// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Nexus.PackageManagement.Core;

namespace Nexus.PackageManagement;

internal partial class PackageController(
    PackageReference packageReference,
    ILogger<PackageController> logger
)
{
    public static Guid BUILTIN_ID = new("97d297d2-df6f-4c85-9d07-86bc64a041a6");

    public const string BUILTIN_PROVIDER = "nexus";

    private readonly ILogger _logger = logger;

    private PackageLoadContext? _loadContext;

    public PackageReference PackageReference { get; } = packageReference;

    public async Task<string[]> DiscoverAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Discover package versions using provider {Provider}", PackageReference.Provider);

        var result = PackageReference.Provider switch
        {
            BUILTIN_PROVIDER => ["current"],
            "local" => await DiscoverLocalAsync(cancellationToken),
            "git-tag" => await DiscoverGitTagsAsync(cancellationToken),
            _ => throw new ArgumentException($"The provider {PackageReference.Provider} is not supported."),
        };

        return result;
    }

    public async Task<Assembly> LoadAsync(string restoreRoot, CancellationToken cancellationToken)
    {
        if (_loadContext is not null)
            throw new Exception("The extension is already loaded.");

        Assembly assembly;

        if (PackageReference.Provider == BUILTIN_PROVIDER)
        {
            assembly = Assembly.GetExecutingAssembly();
            _loadContext = new PackageLoadContext(assembly.Location);
        }

        else
        {
            var restoreFolderPath = await RestoreAsync(restoreRoot, cancellationToken);
            var depsJsonExtension = ".deps.json";

            var depsJsonFilePath = Directory
                .EnumerateFiles(restoreFolderPath, $"*{depsJsonExtension}", SearchOption.AllDirectories)
                .SingleOrDefault() ?? throw new Exception($"Could not determine the location of the .deps.json file in folder {restoreFolderPath}.");
            var entryDllPath = depsJsonFilePath[..^depsJsonExtension.Length] + ".dll" ?? throw new Exception($"Could not determine the location of the entry DLL file in folder {restoreFolderPath}.");
            _loadContext = new PackageLoadContext(entryDllPath);

            var assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(entryDllPath));
            assembly = _loadContext.LoadFromAssemblyName(assemblyName);
        }

        return assembly;
    }

    public WeakReference Unload()
    {
        if (_loadContext is null)
            throw new Exception("The extension is not yet loaded.");

        _loadContext.Unload();
        var weakReference = new WeakReference(_loadContext, trackResurrection: true);
        _loadContext = default;

        return weakReference;
    }

    internal async Task<string> RestoreAsync(string restoreRoot, CancellationToken cancellationToken)
    {
        var actualRestoreRoot = Path.Combine(restoreRoot, PackageReference.Provider);

        _logger.LogDebug("Restore package to {RestoreRoot} using provider {Provider}", actualRestoreRoot, PackageReference.Provider);
        var restoreFolderPath = PackageReference.Provider switch
        {
            "local" => await RestoreLocalAsync(actualRestoreRoot, cancellationToken),
            "git-tag" => await RestoreGitTagAsync(actualRestoreRoot, cancellationToken),
            _ => throw new ArgumentException($"The provider {PackageReference.Provider} is not supported."),
        };
        return restoreFolderPath;
    }

    private static void CloneFolder(string source, string target)
    {
        if (!Directory.Exists(source))
            throw new Exception("The source directory does not exist.");

        Directory.CreateDirectory(target);

        var sourceInfo = new DirectoryInfo(source);
        var targetInfo = new DirectoryInfo(target);

        if (sourceInfo.FullName == targetInfo.FullName)
            throw new Exception("Source and destination are the same.");

        foreach (var folderPath in Directory.GetDirectories(source))
        {
            var folderName = Path.GetFileName(folderPath);

            Directory.CreateDirectory(Path.Combine(target, folderName));
            CloneFolder(folderPath, Path.Combine(target, folderName));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
    }

    private static async Task PublishProjectAsync(
        string csprojFilePath,
        string targetFolderPath,
        string publishFolderPath,
        string repository,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(csprojFilePath))
            throw new Exception($"The .csproj file {csprojFilePath} does not exist.");

        Directory.CreateDirectory(targetFolderPath);

        var startInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            FileName = "dotnet",
            Arguments = $"publish {csprojFilePath} -c Release -o {publishFolderPath}",
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo) ?? throw new Exception("Process is null.");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = process is null
                ? default :
                $" Reason: {await process.StandardError.ReadToEndAsync(cancellationToken)}";

            throw new Exception($"Unable to publish project {repository}.{error}");
        }
    }

    #region local

    private Task<string[]> DiscoverLocalAsync(CancellationToken cancellationToken)
    {
        var rawResult = new List<string>();
        var configuration = PackageReference.Configuration;

        if (!configuration.TryGetValue("path", out var path))
            throw new ArgumentException("The 'path' parameter is missing in the source registration.");

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"The extension path {path} does not exist.");

        foreach (var folderPath in Directory.EnumerateDirectories(path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folderPath);
            rawResult.Add(folderName);
            _logger.LogDebug("Discovered package version {PackageVersion}", folderName);
        }

        var result = rawResult.OrderBy(value => value).Reverse();

        return Task.FromResult(result.ToArray());
    }

    private async Task<string> RestoreLocalAsync(string restoreRoot, CancellationToken cancellationToken)
    {
        var configuration = PackageReference.Configuration;

        if (!configuration.TryGetValue("path", out var path))
            throw new ArgumentException("The 'path' parameter is missing in the source registration.");

        if (!configuration.TryGetValue("version", out var version))
            throw new ArgumentException("The 'version' parameter is missing in the source registration.");

        if (!configuration.TryGetValue("csproj", out var csproj))
            throw new ArgumentException("The 'csproj' parameter is missing in the source registration.");

        var sourceFolderPath = Path.Combine(path, version);

        if (!Directory.Exists(sourceFolderPath))
            throw new DirectoryNotFoundException($"The source path {sourceFolderPath} does not exist.");

        var pathHash = new Guid(path.Hash()).ToString();
        var targetFolderPath = Path.Combine(restoreRoot, pathHash, version);

        if (!Directory.Exists(targetFolderPath) || !Directory.EnumerateFileSystemEntries(targetFolderPath).Any())
        {
            var publishFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                // Publish project
                var csprojFilePath = Path.Combine(sourceFolderPath, csproj);

                await PublishProjectAsync(
                    csprojFilePath,
                    targetFolderPath,
                    publishFolderPath,
                    path,
                    cancellationToken
                );

                // Clone folder
                CloneFolder(publishFolderPath, targetFolderPath);
            }
            catch
            {
                // try delete restore folder
                try
                {
                    if (Directory.Exists(targetFolderPath))
                        Directory.Delete(targetFolderPath, recursive: true);
                }
                catch { }

                throw;
            }
            finally
            {
                // try delete publish folder
                try
                {
                    if (Directory.Exists(publishFolderPath))
                        Directory.Delete(publishFolderPath, recursive: true);
                }
                catch { }
            }
        }
        else
        {
            _logger.LogDebug("Package is already restored");
        }

        return targetFolderPath;
    }

    #endregion

    #region git-tag

    private async Task<string[]> DiscoverGitTagsAsync(CancellationToken cancellationToken)
    {
        const string refPrefix = "refs/tags/";

        var result = new List<string>();
        var configuration = PackageReference.Configuration;

        if (!configuration.TryGetValue("repository", out var repository))
            throw new ArgumentException("The 'repository' parameter is missing in the source registration.");

        var startInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            FileName = "git",
            Arguments = $"ls-remote --tags --sort=v:refname --refs {repository}",
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo) ?? throw new Exception("Process is null.");
        while (!process.StandardOutput.EndOfStream)
        {
            var refLine = await process.StandardOutput.ReadLineAsync(cancellationToken);

            try
            {
                var refString = refLine!.Split('\t')[1];

                if (refString.StartsWith(refPrefix))
                {
                    var tag = refString[refPrefix.Length..];
                    result.Add(tag);
                }

                else
                {
                    _logger.LogDebug("Unable to extract tag from ref {Ref}", refLine);
                }
            }
            catch
            {
                _logger.LogDebug("Unable to extract tag from ref {Ref}", refLine);
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var escapedUriWithoutUserInfo = new Uri(repository)
                .GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);

            var error = process is null
                ? default :
                $" Reason: {await process.StandardError.ReadToEndAsync(cancellationToken)}";

            throw new Exception($"Unable to discover tags for repository {escapedUriWithoutUserInfo}.{error}");
        }

        result.Reverse();

        return [.. result];
    }

    private async Task<string> RestoreGitTagAsync(string restoreRoot, CancellationToken cancellationToken)
    {
        var configuration = PackageReference.Configuration;

        if (!configuration.TryGetValue("repository", out var repository))
            throw new ArgumentException("The 'repository' parameter is missing in the source registration.");

        if (!configuration.TryGetValue("tag", out var tag))
            throw new ArgumentException("The 'tag' parameter is missing in the source registration.");

        if (!configuration.TryGetValue("csproj", out var csproj))
            throw new ArgumentException("The 'csproj' parameter is missing in the source registration.");

        var escapedUriWithoutSchemeAndUserInfo = new Uri(repository)
            .GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Scheme & ~UriComponents.UserInfo, UriFormat.UriEscaped);

        var targetFolderPath = Path.Combine(restoreRoot, escapedUriWithoutSchemeAndUserInfo.Replace('/', '_').ToLower(), tag);

        if (!Directory.Exists(targetFolderPath) || !Directory.EnumerateFileSystemEntries(targetFolderPath).Any())
        {
            var cloneFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var publishFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            var escapedUriWithoutUserInfo = new Uri(repository)
                .GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);

            try
            {
                // Clone respository
                Directory.CreateDirectory(cloneFolderPath);

                var startInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    FileName = "git",
                    Arguments = $"clone --depth 1 --branch {tag} --recurse-submodules {repository} {cloneFolderPath}",
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo) ?? throw new Exception("Process is null.");
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var error = process is null
                        ? default :
                        $" Reason: {await process.StandardError.ReadToEndAsync(cancellationToken)}";

                    throw new Exception($"Unable to clone repository {escapedUriWithoutUserInfo}.{error}");
                }

                // Publish project
                var csprojFilePath = Path.Combine(cloneFolderPath, csproj);

                await PublishProjectAsync(
                    csprojFilePath,
                    targetFolderPath,
                    publishFolderPath,
                    escapedUriWithoutUserInfo,
                    cancellationToken
                );

                // Clone folder
                CloneFolder(publishFolderPath, targetFolderPath);
            }
            catch
            {
                // try delete restore folder
                try
                {
                    if (Directory.Exists(targetFolderPath))
                        Directory.Delete(targetFolderPath, recursive: true);
                }
                catch { }

                throw;
            }
            finally
            {
                // try delete clone folder
                try
                {
                    if (Directory.Exists(cloneFolderPath))
                        Directory.Delete(cloneFolderPath, recursive: true);
                }
                catch { }

                // try delete publish folder
                try
                {
                    if (Directory.Exists(publishFolderPath))
                        Directory.Delete(publishFolderPath, recursive: true);
                }
                catch { }
            }
        }
        else
        {
            _logger.LogDebug("Package is already restored");
        }

        return targetFolderPath;
    }

    #endregion

}
