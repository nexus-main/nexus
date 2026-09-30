// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Sources;
using Xunit;

namespace DataSource;

public class DevDataSourceTests
{
    [Fact]
    public async Task ProvidesNoCatalogRegistrationsOutsideDevelopment()
    {
        // Arrange
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        try
        {
            var dataSource = new Dev() as IDataSource<object?>;

            // Act
            var actual = await dataSource.GetCatalogRegistrationsAsync("/", CancellationToken.None);

            // Assert
            Assert.Empty(actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
        }
    }

    [Fact]
    public async Task ProvidesDevCatalogRegistrationsInDevelopment()
    {
        // Arrange
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        try
        {
            var dataSource = new Dev() as IDataSource<object?>;

            // Act
            var actual = await dataSource.GetCatalogRegistrationsAsync("/", CancellationToken.None);

            // Assert
            var rangeLimited = Assert.Single(actual, r => r.Path == Dev.RangeLimitedCatalogId);
            Assert.Equal(new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc), rangeLimited.MinBegin);
            Assert.Equal(new DateTime(2020, 02, 01, 0, 0, 0, DateTimeKind.Utc), rangeLimited.MaxEnd);

            var aliasRange = Assert.Single(actual, r => r.Path == Dev.AliasRangeCatalogId);
            Assert.Equal(Dev.RangeLimitedCatalogId, aliasRange.LinkTarget);
            Assert.Equal(new DateTime(2020, 01, 15, 0, 0, 0, DateTimeKind.Utc), aliasRange.MinBegin);
            Assert.Equal(new DateTime(2020, 03, 01, 0, 0, 0, DateTimeKind.Utc), aliasRange.MaxEnd);

            var aliasToAlias = Assert.Single(actual, r => r.Path == Dev.AliasToAliasCatalogId);
            Assert.Equal(Dev.AliasRangeCatalogId, aliasToAlias.LinkTarget);
            Assert.Equal(new DateTime(2020, 01, 20, 0, 0, 0, DateTimeKind.Utc), aliasToAlias.MinBegin);
            Assert.Equal(new DateTime(2020, 02, 15, 0, 0, 0, DateTimeKind.Utc), aliasToAlias.MaxEnd);

            var sourceWithChildren = Assert.Single(actual, r => r.Path == Dev.SourceWithChildrenCatalogId);
            Assert.Null(sourceWithChildren.LinkTarget);

            var aliasWithChildren = Assert.Single(actual, r => r.Path == Dev.AliasWithChildrenCatalogId);
            Assert.Equal(Dev.SourceWithChildrenCatalogId, aliasWithChildren.LinkTarget);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
        }
    }

    [Fact]
    public async Task ProvidesChildCatalogRegistrations()
    {
        // Arrange
        var dataSource = new Dev() as IDataSource<object?>;

        // Act
        var actual = await dataSource.GetCatalogRegistrationsAsync(
            Dev.SourceWithChildrenCatalogId + "/",
            CancellationToken.None);

        // Assert
        Assert.Equal(2, actual.Length);
        Assert.Contains(actual, r => r.Path == Dev.ChildACatalogId);
        Assert.Contains(actual, r => r.Path == Dev.ChildBCatalogId);
    }

    [Fact]
    public async Task ProvidesContainerCatalogWithoutResources()
    {
        // Arrange
        var dataSource = new Dev() as IDataSource<object?>;

        // Act
        var actual = await dataSource.EnrichCatalogAsync(
            new ResourceCatalog(Dev.SourceWithChildrenCatalogId),
            CancellationToken.None);

        // Assert
        Assert.Null(actual.Resources);
    }

    [Fact]
    public async Task ProvidesChildCatalogA()
    {
        // Arrange
        var dataSource = new Dev() as IDataSource<object?>;

        // Act
        var actual = await dataSource.EnrichCatalogAsync(
            new ResourceCatalog(Dev.ChildACatalogId),
            CancellationToken.None);

        // Assert
        Assert.NotNull(actual.Resources);
        var resource = Assert.Single(actual.Resources);
        Assert.Equal("T1", resource.Id);
        Assert.Equal("°C", resource.Properties?.GetStringValue(DataModelExtensions.UnitKey));
    }

    [Fact]
    public async Task ProvidesChildCatalogB()
    {
        // Arrange
        var dataSource = new Dev() as IDataSource<object?>;

        // Act
        var actual = await dataSource.EnrichCatalogAsync(
            new ResourceCatalog(Dev.ChildBCatalogId),
            CancellationToken.None);

        // Assert
        Assert.NotNull(actual.Resources);
        var resource = Assert.Single(actual.Resources);
        Assert.Equal("V1", resource.Id);
        Assert.Equal("m/s", resource.Properties?.GetStringValue(DataModelExtensions.UnitKey));
    }
}
