// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.Services;
using Nexus.Sources;
using Xunit;

namespace Services;

public class DevelopmentSampleLicenseSeederTests
{
    [Fact]
    public void SeedCreatesLicenseAttachment()
    {
        var paths = GetTempPaths();

        try
        {
            var databaseService = GetDatabaseService(paths);
            var seeder = new DevelopmentSampleLicenseSeeder(databaseService);

            seeder.Seed();

            Assert.True(databaseService.TryReadAttachment(
                Sample.LicensedCatalogId,
                DevelopmentSampleLicenseSeeder.LicenseAttachmentId,
                out var attachment));

            using var stream = attachment;
            using var reader = new StreamReader(stream);

            Assert.Equal(DevelopmentSampleLicenseSeeder.LicenseText, reader.ReadToEnd());
        }
        finally
        {
            Delete(paths.Root);
        }
    }

    [Fact]
    public void SeedDoesNotOverwriteExistingLicenseAttachment()
    {
        var paths = GetTempPaths();

        try
        {
            var databaseService = GetDatabaseService(paths);
            const string existingLicense = "existing license";

            using (var stream = databaseService.WriteAttachment(
                Sample.LicensedCatalogId,
                DevelopmentSampleLicenseSeeder.LicenseAttachmentId))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(existingLicense);
            }

            var seeder = new DevelopmentSampleLicenseSeeder(databaseService);

            seeder.Seed();

            Assert.True(databaseService.TryReadAttachment(
                Sample.LicensedCatalogId,
                DevelopmentSampleLicenseSeeder.LicenseAttachmentId,
                out var attachment));

            using var readStream = attachment;
            using var reader = new StreamReader(readStream);

            Assert.Equal(existingLicense, reader.ReadToEnd());
        }
        finally
        {
            Delete(paths.Root);
        }
    }

    private static DatabaseService GetDatabaseService(TestPaths paths)
    {
        return new DatabaseService(Options.Create(new PathsOptions()
        {
            Catalogs = paths.Catalogs,
            Config = paths.Config,
            Cache = paths.Cache,
            Artifacts = paths.Artifacts,
            Packages = paths.Packages
        }));
    }

    private static TestPaths GetTempPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nexus", Guid.NewGuid().ToString());

        return new TestPaths(
            root,
            Path.Combine(root, "catalogs"),
            Path.Combine(root, "config"),
            Path.Combine(root, "cache"),
            Path.Combine(root, "artifacts"),
            Path.Combine(root, "packages")
        );
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed record TestPaths(
        string Root,
        string Catalogs,
        string Config,
        string Cache,
        string Artifacts,
        string Packages
    );
}
