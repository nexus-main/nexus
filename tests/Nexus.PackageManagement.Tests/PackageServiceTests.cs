// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Moq;
using Nexus.PackageManagement;
using Nexus.PackageManagement.Core;
using Nexus.PackageManagement.Services;
using Xunit;

namespace Services;

public class PackageServiceTests
{
    delegate bool GobbleReturns(out string? pipelineMap);

    [Fact]
    public async Task CanCreatePackageReference()
    {
        // Arrange
        var filePath = Path.GetTempFileName();
        var packageService = GetPackageService(filePath, []);

        var packageReference = new PackageReference(
            Provider: "foo",
            Configuration: default!
        );

        // Act
        var expectedId = await packageService.PutAsync(packageReference);

        // Assert
        var jsonString = File.ReadAllText(filePath);
        var actualPackageReferenceMap = JsonSerializer.Deserialize<Dictionary<Guid, PackageReference>>(jsonString)!;
        var entry = Assert.Single(actualPackageReferenceMap);

        Assert.Equal(expectedId, entry.Key);
        Assert.Equal("foo", entry.Value.Provider);
        Assert.Null(entry.Value.Configuration);
    }

    [Fact]
    public void CanTryGetPackageReference()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var packageReferenceMap = new Dictionary<Guid, PackageReference>()
        {
            [id1] = new PackageReference(
                Provider: "foo",
                Configuration: default!
            ),
            [id2] = new PackageReference(
                Provider: "bar",
                Configuration: default!
            )
        };

        var packageService = GetPackageService(default!, packageReferenceMap);

        // Act
        var actual = packageService.TryGet(id2, out var actualPackageReference);

        // Assert
        Assert.True(actual);

        Assert.Equal(
            expected: JsonSerializer.Serialize(actualPackageReference),
            actual: JsonSerializer.Serialize(packageReferenceMap[id2])
        );
    }

    [Fact]
    public async Task CanDeletePackage()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var packageReferencesMap = new Dictionary<Guid, PackageReference>()
        {
            [id1] = new PackageReference(
                Provider: "foo",
                Configuration: default!
            ),
            [id2] = new PackageReference(
                Provider: "bar",
                Configuration: default!
            )
        };

        var filePath = Path.GetTempFileName();
        var packageService = GetPackageService(filePath, packageReferencesMap);

        // Act
        await packageService.DeleteAsync(id1);

        // Assert
        packageReferencesMap.Remove(id1);
        var expected = JsonSerializerHelper.SerializeIndented(packageReferencesMap);
        var actual = File.ReadAllText(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanGetAllPackages()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var packageReferencesMap = new Dictionary<Guid, PackageReference>()
        {
            [id1] = new PackageReference(
                Provider: "foo",
                Configuration: default!
            ),
            [id2] = new PackageReference(
                Provider: "bar",
                Configuration: default!
            )
        };

        var filePath = Path.GetTempFileName();
        var packageService = GetPackageService(filePath, packageReferencesMap);

        // Act
        var actualPackageMap = await packageService.GetAllAsync();

        // Assert
        var expected = JsonSerializerHelper.SerializeIndented(packageReferencesMap.OrderBy(current => current.Key));
        var actual = JsonSerializerHelper.SerializeIndented(actualPackageMap.OrderBy(current => current.Key));

        Assert.Equal(expected, actual);
    }

    private static IPackageService GetPackageService(
        string filePath,
        Dictionary<Guid, PackageReference> packageReferenceMap)
    {
        var databaseService = Mock.Of<IPackageManagementDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadPackageReferenceMap(out It.Ref<string?>.IsAny))
            .Returns(new GobbleReturns((out string? packageReferenceMapString) =>
            {
                packageReferenceMapString = JsonSerializer.Serialize(packageReferenceMap);
                return true;
            }));

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.WritePackageReferenceMap())
            .Returns(() => File.OpenWrite(filePath));

        var packageService = new PackageService(databaseService);

        return packageService;
    }
}