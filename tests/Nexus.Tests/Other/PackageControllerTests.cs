// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Other;

public class PackageControllerTests
{
    // Need to do it this way because GitHub revokes obvious tokens on commit.
    // However, this token - in combination with the test user's account
    // privileges - allows only read-only access to a test project, so there
    // is no real risk.
    private static readonly byte[] _token =
    [
        0x67,
        0x69,
        0x74,
        0x68,
        0x75,
        0x62,
        0x5F,
        0x70,
        0x61,
        0x74,
        0x5F,
        0x31,
        0x31,
        0x41,
        0x46,
        0x41,
        0x41,
        0x45,
        0x59,
        0x49,
        0x30,
        0x63,
        0x55,
        0x79,
        0x35,
        0x77,
        0x72,
        0x68,
        0x38,
        0x47,
        0x4E,
        0x7A,
        0x4B,
        0x5F,
        0x65,
        0x4C,
        0x33,
        0x4F,
        0x44,
        0x39,
        0x30,
        0x30,
        0x4D,
        0x52,
        0x36,
        0x4F,
        0x62,
        0x76,
        0x50,
        0x6E,
        0x6C,
        0x58,
        0x42,
        0x36,
        0x38,
        0x50,
        0x4B,
        0x52,
        0x37,
        0x30,
        0x37,
        0x68,
        0x58,
        0x30,
        0x69,
        0x56,
        0x4B,
        0x31,
        0x57,
        0x51,
        0x55,
        0x39,
        0x63,
        0x67,
        0x41,
        0x4E,
        0x73,
        0x5A,
        0x4E,
        0x4F,
        0x55,
        0x5A,
        0x41,
        0x50,
        0x33,
        0x4D,
        0x51,
        0x30,
        0x67,
        0x38,
        0x78,
        0x58,
        0x41
    ];

    #region Load

    [Fact]
    public async Task CanLoadAndUnloadAsync()
    {
        // prepare extension
        var extensionFolderPath = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var pathHash = new Guid(extensionFolderPath.Hash()).ToString();
        var configuration = "Debug";
        var csprojPath = "./../../../../tests/TestExtensionProject/TestExtensionProject.csproj";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish --output {Path.Combine(extensionFolderPath, "v1.0.0-unit.test")} --configuration {configuration} {csprojPath}"
            }
        };

        process.Start();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);

        // prepare restore root
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");

        try
        {
            var version = "v1.0.0-unit.test";

            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "local",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["path"] = extensionFolderPath,
                    ["version"] = version
                }
            );

            var fileToDelete = Path.Combine(restoreRoot, "local", pathHash, version, "TestExtensionProject.dll");
            var weakReference = await Load_Run_and_Unload_Async(restoreRoot, fileToDelete, packageReference);

            for (int i = 0; weakReference.IsAlive && i < 10; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            // try to delete file
            File.Delete(fileToDelete);
        }
        finally
        {
            try
            {
                Directory.Delete(restoreRoot, recursive: true);
            }
            catch { }

            try
            {
                Directory.Delete(extensionFolderPath, recursive: true);
            }
            catch { }
        }
    }

    // https://docs.microsoft.com/en-us/dotnet/standard/assembly/unloadability
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> Load_Run_and_Unload_Async(
        string restoreRoot, string fileToDelete, InternalPackageReference packageReference)
    {
        // load
        var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
        var assembly = await packageController.LoadAsync(restoreRoot, CancellationToken.None);

        var dataSourceType = assembly
            .ExportedTypes
            .First(type => typeof(IDataSource).IsAssignableFrom(type))
                ?? throw new Exception("data source type is null");

        // run

        if (Activator.CreateInstance(dataSourceType) is not IDataSource dataSource)
            throw new Exception("data source is null");

        var exception = await Assert.ThrowsAsync<NotImplementedException>(() => dataSource.GetCatalogAsync(string.Empty, CancellationToken.None));

        Assert.Equal(nameof(IDataSource.GetCatalogAsync), exception.Message);

        // delete should fail
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Throws<UnauthorizedAccessException>(() => File.Delete(fileToDelete));

        // unload
        var weakReference = packageController.Unload();

        return weakReference;
    }

    #endregion

    #region Provider: local

    [Fact]
    public async Task CanDiscover_local()
    {
        // create dirs
        var root = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");

        var expected = new[]
        {
            "v2.0.0 postfix",
            "v1.1.1 postfix",
            "v1.0.1 postfix",
            "v1.0.0-beta2+12347 postfix",
            "v1.0.0-beta1+12346 postfix",
            "v1.0.0-alpha1+12345 postfix",
            "v0.1.0"
        };

        foreach (var version in expected)
        {
            var dataFolderPath = Path.Combine(root, version);
            Directory.CreateDirectory(Path.Combine(dataFolderPath, "sub"));

            await File.Create(Path.Combine(dataFolderPath, "sub", "a.deps.json")).DisposeAsync();
            await File.Create(Path.Combine(dataFolderPath, "sub", "a.dll")).DisposeAsync();
        }

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "local",
                Configuration: new Dictionary<string, string>
                {
                    ["path"] = root,
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

            var actual = (await packageController
                .DiscoverAsync(CancellationToken.None))
                .ToArray();

            Assert.Equal(expected.Length, actual.Length);

            foreach (var (expectedItem, actualItem) in expected.Zip(actual))
            {
                Assert.Equal(expectedItem, actualItem);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CanRestore_local()
    {
        // create extension folder
        var version = "v1.0.1 postfix";
        var extensionRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var extensionRootHash = new Guid(extensionRoot.Hash()).ToString();
        var extensionFolderPath = Path.Combine(extensionRoot, version);
        Directory.CreateDirectory(Path.Combine(extensionFolderPath, "sub", "sub"));

        await File.Create(Path.Combine(extensionFolderPath, "sub", "a.deps.json")).DisposeAsync();
        await File.Create(Path.Combine(extensionFolderPath, "sub", "a.dll")).DisposeAsync();
        await File.Create(Path.Combine(extensionFolderPath, "sub", "b.dll")).DisposeAsync();
        await File.Create(Path.Combine(extensionFolderPath, "sub", "sub", "c.data")).DisposeAsync();

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var restoreFolderPath = Path.Combine(restoreRoot, "local", extensionRootHash, version);
        Directory.CreateDirectory(restoreRoot);

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "local",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["path"] = extensionRoot,
                    ["version"] = version
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
        }
        finally
        {
            Directory.Delete(extensionRoot, recursive: true);
            Directory.Delete(restoreRoot, recursive: true);
        }
    }

    #endregion

    #region Provider: git_tag

    // Disable when running on GitHub Actions. It seems that there git ls-remote ignores the credentials (git clone works).
#if !CI
    [Fact]
    public async Task CanDiscover_git_tag()
    {
        var expected = new[]
        {
            "v2.0.0-beta.1",
            "v2.0.0",
            "v1.1.1",
            "v1.0.1",
            "v1.0.0-beta2+12347",
            "v1.0.0-beta1+12346",
            "v1.0.0-alpha1+12345",
            "v0.1.0"
        };

        var packageReference = new InternalPackageReference(
            Id: Guid.NewGuid(),
            Provider: "git-tag",
            Configuration: new Dictionary<string, string>
            {
                // required
                ["repository"] = $"https://{Encoding.ASCII.GetString(_token)}@github.com/nexus-main/git-tag-provider-test-project"
            }
        );

        var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

        var actual = (await packageController
            .DiscoverAsync(CancellationToken.None))
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);

        foreach (var (expectedItem, actualItem) in expected.Zip(actual))
        {
            Assert.Equal(expectedItem, actualItem);
        }
    }
#endif

    [Fact]
    public async Task CanRestore_git_tag()
    {
        var version = "v2.0.0-beta.1";

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var restoreFolderPath = Path.Combine(restoreRoot, "git-tag", "github.com_nexus-main_git-tag-provider-test-project", version);
        Directory.CreateDirectory(restoreRoot);

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "git-tag",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["repository"] = $"https://{Encoding.ASCII.GetString(_token)}@github.com/nexus-main/git-tag-provider-test-project",
                    ["tag"] = version,
                    ["csproj"] = "git-tags-provider-test-project.csproj"
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "git-tags-provider-test-project.deps.json")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "git-tags-provider-test-project.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "git-tags-provider-test-project.pdb")));
        }
        finally
        {
            Directory.Delete(restoreRoot, recursive: true);
        }
    }

    #endregion

    #region Provider: github_releases

    [Fact]
    public async Task CanDiscover_github_releases()
    {
        var expected = new[]
        {
            "v2.0.0",
            "v1.1.1",
            "v1.0.1",
            "v1.0.0-beta2+12347",
            "v1.0.0-beta1+12346",
            "v1.0.0-alpha1+12345",
            "v0.1.0"
        };

        var packageReference = new InternalPackageReference(
            Id: Guid.NewGuid(),
            Provider: "github-releases",
            Configuration: new Dictionary<string, string>
            {
                // required
                ["project-path"] = "nexus-main/github-releases-provider-test-project",

                // optional token with scope(s): repo
                ["token"] = Encoding.ASCII.GetString(_token)
            }
        );

        var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

        var actual = (await packageController
            .DiscoverAsync(CancellationToken.None))
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);

        foreach (var (expectedItem, actualItem) in expected.Zip(actual))
        {
            Assert.Equal(expectedItem, actualItem);
        }
    }

    [Theory]
    [InlineData(@"assets.*\.tar.gz")]
    [InlineData(@"assets.*\.zip")]
    public async Task CanRestore_github_releases(string assetSelector)
    {
        var version = "v1.0.1";

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var restoreFolderPath = Path.Combine(restoreRoot, "github-releases", "nexus-main_github-releases-provider-test-project", version);
        Directory.CreateDirectory(restoreRoot);

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "github-releases",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["project-path"] = "nexus-main/github-releases-provider-test-project",
                    ["tag"] = "v1.0.1",
                    ["asset-selector"] = assetSelector,

                    // optional token with scope(s): repo
                    ["token"] = Encoding.ASCII.GetString(_token)
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
        }
        finally
        {
            Directory.Delete(restoreRoot, recursive: true);
        }
    }

    #endregion

    #region Provider: gitlab-releases-v4

    [Fact(Skip = "The current approach does not work. See rationale in #region gitlab-releases-v4.")]
    public async Task CanDiscover_gitlab_releases_v4()
    {
        var expected = new[]
        {
            "v2.0.0",
            "v1.1.1",
            "v1.0.1",
            "v1.0.0-beta2+12347",
            "v1.0.0-beta1+12346",
            "v1.0.0-alpha1+12345",
            "v0.1.0"
        };

        var packageReference = new InternalPackageReference(
            Id: Guid.NewGuid(),
            Provider: "gitlab-releases-v4",
            Configuration: new Dictionary<string, string>
            {
                // required
                ["server"] = "https://gitlab.com",
                ["project-path"] = "nexus-main/Test-Group/my-awesome-test-project",

                // optional token with scope(s): read_api
                ["token"] = "doQyXYqgmFxS1LUsupue"
            }
        );

        var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

        var actual = (await packageController
            .DiscoverAsync(CancellationToken.None))
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);

        foreach (var (expectedItem, actualItem) in expected.Zip(actual))
        {
            Assert.Equal(expectedItem, actualItem);
        }
    }

    [Theory(Skip = "The current approach does not work. See rationale in #region gitlab-releases-v4.")]
    [InlineData(@"assets.*\.tar.gz")]
    [InlineData(@"assets.*\.zip")]
    public async Task CanRestore_gitlab_releases_v4(string assetSelector)
    {
        var version = "v1.0.1";

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var restoreFolderPath = Path.Combine(restoreRoot, "gitlab-releases-v4", $"nexus-main_test-group_my-awesome-test-project", version);
        Directory.CreateDirectory(restoreRoot);

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "gitlab-releases-v4",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["server"] = "https://gitlab.com",
                    ["project-path"] = "nexus-main/Test-Group/my-awesome-test-project",
                    ["tag"] = "v1.0.1",
                    ["asset-selector"] = assetSelector,

                    // optional token with scope(s): read_api
                    ["token"] = "glpat-sQVhisTwk56oJyGJhzx1"
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
        }
        finally
        {
            Directory.Delete(restoreRoot, recursive: true);
        }
    }

    #endregion

    #region Provider: gitlab-packages-generic-v4

    [Fact]
    public async Task CanDiscover_gitlab_packages_generic_v4()
    {
        var expected = new[]
        {
            "v2.0.0",
            "v1.1.1",
            "v1.0.1",
            "v1.0.0-beta2+12347",
            "v1.0.0-beta1+12346",
            "v1.0.0-alpha1+12345",
            "v0.1.0"
        };

        var packageReference = new InternalPackageReference(
            Id: Guid.NewGuid(),
            Provider: "gitlab-packages-generic-v4",
            Configuration: new Dictionary<string, string>
            {
                // required
                ["server"] = "https://gitlab.com",
                ["project-path"] = "nexus-main/Test-Group/my-awesome-test-project",
                ["package"] = "test-package",

                // optional token with scope(s): read_api
                ["token"] = "glpat-sQVhisTwk56oJyGJhzx1",
            }
        );

        var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);

        var actual = (await packageController
            .DiscoverAsync(CancellationToken.None))
            .ToArray();

        Assert.Equal(expected.Length, actual.Length);

        foreach (var (expectedItem, actualItem) in expected.Zip(actual))
        {
            Assert.Equal(expectedItem, actualItem);
        }
    }

    [Theory]
    [InlineData(@"assets.*\.tar.gz")]
    [InlineData(@"assets.*\.zip")]
    public async Task CanRestore_gitlab_packages_generic_v4(string assetSelector)
    {
        var version = "v1.0.1";

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        var restoreFolderPath = Path.Combine(restoreRoot, "gitlab-packages-generic-v4", $"nexus-main_test-group_my-awesome-test-project", version);
        Directory.CreateDirectory(restoreRoot);

        try
        {
            var packageReference = new InternalPackageReference(
                Id: Guid.NewGuid(),
                Provider: "gitlab-packages-generic-v4",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["server"] = "https://gitlab.com",
                    ["project-path"] = "nexus-main/Test-Group/my-awesome-test-project",
                    ["package"] = "test-package",
                    ["version"] = "v1.0.1",
                    ["asset-selector"] = assetSelector,

                    // optional token with scope(s): read_api
                    ["token"] = "glpat-sQVhisTwk56oJyGJhzx1",
                }
            );

            var packageController = new PackageController(packageReference, NullLogger<PackageController>.Instance);
            await packageController.RestoreAsync(restoreRoot, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.deps.json")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "a.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "b.dll")));
            Assert.True(File.Exists(Path.Combine(restoreFolderPath, "sub", "sub", "c.data")));
        }
        finally
        {
            Directory.Delete(restoreRoot, recursive: true);
        }
    }

    #endregion
}