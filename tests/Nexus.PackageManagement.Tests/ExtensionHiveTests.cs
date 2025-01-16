// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Extensibility;
using Nexus.PackageManagement;
using Nexus.PackageManagement.Services;
using System.Diagnostics;
using Xunit;

namespace Services;

public class ExtensionHiveTests
{
    [Fact]
    public async Task CanInstantiateExtensions()
    {
        var extensionFolderPath = "../../../../tests/resources/TestExtension";

        // create restore folder
        var restoreRoot = Path.Combine(Path.GetTempPath(), $"Nexus.Tests.{Guid.NewGuid()}");
        Directory.CreateDirectory(restoreRoot);

        try
        {
            // load packages
            var pathsOptions = Mock.Of<IPackageManagementPathsOptions>();

            Mock.Get(pathsOptions)
                .SetupGet(pathsOptions => pathsOptions.Packages)
                .Returns(restoreRoot);

            var loggerFactory = Mock.Of<ILoggerFactory>();

            Mock.Get(loggerFactory)
                .Setup(loggerFactory => loggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(NullLogger.Instance);

            var hive = new ExtensionHive<IDataSource, IDataWriter>(pathsOptions, NullLogger<ExtensionHive<IDataSource, IDataWriter>>.Instance, loggerFactory);
            var version = "v0.1.0";

            var packageReference = new PackageReference(
                Provider: "local",
                Configuration: new Dictionary<string, string>
                {
                    // required
                    ["path"] = extensionFolderPath,
                    ["version"] = version,
                    ["csproj"] = "TestExtension.csproj"
                }
            );

            var packageReferenceMap = new Dictionary<Guid, PackageReference>
            {
                [Guid.NewGuid()] = packageReference
            };

            await hive.LoadPackagesAsync(packageReferenceMap, new Progress<double>(), CancellationToken.None);

            // instantiate
            hive.GetInstance<IDataSource>("TestExtension.TestDataSource");
            hive.GetInstance<IDataWriter>("TestExtension.TestDataWriter");

            Assert.Throws<Exception>(() => hive.GetInstance<IDataSource>("TestExtension.TestDataWriter"));
        }
        finally
        {
            try
            {
                Directory.Delete(restoreRoot, recursive: true);
            }
            catch { }
        }
    }
}