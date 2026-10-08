using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nexus.Controllers.V1;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Xunit;

namespace API.V1;

public class CatalogsControllerTests
{
    [Fact]
    public async Task GetTimeRangeClampsToAliasRange()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/ALIAS$"), begin, end, TimeSpan.FromDays(1));

        // Act
        var actual = await controller.GetTimeRangeAsync("/ALIAS", CancellationToken.None);

        // Assert
        Assert.NotNull(actual.Value);
        Assert.Equal(new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc), actual.Value.Begin);
        Assert.Equal(new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Utc), actual.Value.End);

        dataSourceController.Verify(controller => controller.GetTimeRangeAsync(
            "/SOURCE",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTimeRangeRejectsSourcePermissionWithoutAliasPermission()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/SOURCE$"), begin, end, TimeSpan.FromDays(1));

        // Act
        var actual = await controller.GetTimeRangeAsync("/ALIAS", CancellationToken.None);

        // Assert
        var result = Assert.IsType<ObjectResult>(actual.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);

        dataSourceController.Verify(controller => controller.GetTimeRangeAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAvailabilityClampsToAliasRangeAndUsesBackingSourceId()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromDays(1);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/ALIAS$"), begin, end, step);

        // Act
        var actual = await controller.GetAvailabilityAsync("/ALIAS", begin, end, step, CancellationToken.None);

        // Assert
        Assert.NotNull(actual.Value);
        Assert.Equal(new[] { 0.0, 1, 1, 0 }, actual.Value.Data);

        dataSourceController.Verify(controller => controller.GetAvailabilityAsync(
            "/SOURCE",
            new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Utc),
            step,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailabilityIgnoresTrailingPartialBucket()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 12, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromDays(1);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/ALIAS$"), begin, end, step);

        // Act
        var actual = await controller.GetAvailabilityAsync("/ALIAS", begin, end, step, CancellationToken.None);

        // Assert
        Assert.NotNull(actual.Value);
        Assert.Equal(new[] { 0.0, 1, 1, 0 }, actual.Value.Data);

        dataSourceController.Verify(controller => controller.GetAvailabilityAsync(
            "/SOURCE",
            new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Utc),
            step,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailabilityRejectsStepLargerThanRange()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 01, 12, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromDays(1);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/ALIAS$"), begin, end, step);

        // Act
        var actual = await controller.GetAvailabilityAsync("/ALIAS", begin, end, step, CancellationToken.None);

        // Assert
        var result = Assert.IsType<UnprocessableEntityObjectResult>(actual.Result);
        Assert.Equal("The step must be smaller than or equal to the requested time range.", result.Value);

        dataSourceController.Verify(controller => controller.GetAvailabilityAsync(
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAvailabilityRejectsSourcePermissionWithoutAliasPermission()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromDays(1);
        var (_, dataSourceController, controller) = CreateController(CreateUser("^/SOURCE$"), begin, end, step);

        // Act
        var actual = await controller.GetAvailabilityAsync("/ALIAS", begin, end, step, CancellationToken.None);

        // Assert
        var result = Assert.IsType<ObjectResult>(actual.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);

        dataSourceController.Verify(controller => controller.GetAvailabilityAsync(
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidAliasRangeExposesEmptyTimeRangeAndAvailability()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var aliasBegin = new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Utc);
        var aliasEnd = new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromDays(1);
        var (_, dataSourceController, controller) = CreateController(
            CreateUser("^/ALIAS$"),
            begin,
            end,
            step,
            aliasBegin,
            aliasEnd);

        // Act
        var timeRange = await controller.GetTimeRangeAsync("/ALIAS", CancellationToken.None);
        var availability = await controller.GetAvailabilityAsync("/ALIAS", begin, end, step, CancellationToken.None);

        // Assert
        Assert.NotNull(timeRange.Value);
        Assert.Equal(aliasBegin, timeRange.Value.Begin);
        Assert.Equal(aliasBegin, timeRange.Value.End);
        Assert.NotNull(availability.Value);
        Assert.Equal(new[] { 0.0, 0, 0, 0 }, availability.Value.Data);

        dataSourceController.Verify(controller => controller.GetAvailabilityAsync(
            It.IsAny<string>(),
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChildCatalogInfosReportsAliasAsReadOnly()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var (_, _, controller) = CreateController(CreateUser("^/ALIAS$", "^/ALIAS$"), begin, end, TimeSpan.FromDays(1));

        // Act
        var actual = await controller.GetChildCatalogInfosAsync(CatalogContainer.RootCatalogId, CancellationToken.None);

        // Assert
        var catalogInfo = Assert.Single(Assert.IsType<CatalogInfo[]>(actual.Value));
        Assert.Equal("/ALIAS", catalogInfo.Id);
        Assert.False(catalogInfo.IsWritable);
    }

    [Fact]
    public async Task GetMetadataInheritsFromResolvedAliasSource()
    {
        // Arrange
        var begin = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 01, 05, 0, 0, 0, DateTimeKind.Utc);
        var sourceMetadata = new CatalogMetadata("contact", ["group"], default);
        var (_, _, controller) = CreateController(
            CreateUser("^/ALIAS$"),
            begin,
            end,
            TimeSpan.FromDays(1),
            sourceMetadata: sourceMetadata);

        // Act
        var actual = await controller.GetMetadataAsync("/ALIAS", CancellationToken.None);

        // Assert
        Assert.Same(sourceMetadata, actual.Value);
    }

    private static ClaimsPrincipal CreateUser(string catalogPattern, string? writableCatalogPattern = default)
    {
        var claims = new List<Claim>
        {
            new(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, catalogPattern),
            new(nameof(NexusClaims.CanReadCatalog), catalogPattern)
        };

        if (writableCatalogPattern is not null)
            claims.Add(new Claim(nameof(NexusClaims.CanWriteCatalog), writableCatalogPattern));

        var identity = new ClaimsIdentity(claims, authenticationType: "test");

        return new ClaimsPrincipal(identity);
    }

    private static (Mock<IDataControllerService> DataControllerService, Mock<IDataSourceController> DataSourceController, CatalogsController Controller) CreateController(
        ClaimsPrincipal user,
        DateTime begin,
        DateTime end,
        TimeSpan step,
        DateTime? aliasBegin = default,
        DateTime? aliasEnd = default,
        CatalogMetadata? sourceMetadata = default)
    {
        var catalogManager = new Mock<ICatalogManager>();
        var databaseService = new Mock<IDatabaseService>();
        var acceptedLicenseService = new Mock<IAcceptedLicenseService>();
        var dataControllerService = new Mock<IDataControllerService>();
        var dataSourceController = new Mock<IDataSourceController>();

        var pipeline = new DataSourcePipeline(
            [new DataSourceRegistration("test", new Uri("http://example.com"), JsonSerializer.SerializeToElement<object?>(default), default)]);

        aliasBegin ??= new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc);
        aliasEnd ??= new DateTime(2024, 01, 04, 0, 0, 0, DateTimeKind.Utc);

        var root = CatalogContainer.CreateRoot(catalogManager.Object, databaseService.Object);
        var sourceContainer = new CatalogContainer(
            new CatalogRegistration("/SOURCE", default),
            Guid.NewGuid(),
            pipeline,
            Array.Empty<Guid>(),
            sourceMetadata ?? new CatalogMetadata(default, default, default),
            catalogManager.Object,
            databaseService.Object,
            dataControllerService.Object);

        var aliasContainer = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE", MinBegin: aliasBegin, MaxEnd: aliasEnd),
            Guid.NewGuid(),
            pipeline,
            Array.Empty<Guid>(),
            new CatalogMetadata(default, default, default),
            catalogManager.Object,
            databaseService.Object,
            dataControllerService.Object);

        catalogManager.Setup(manager => manager.GetCatalogContainersAsync(
                It.Is<CatalogContainer>(container => container.Id == CatalogContainer.RootCatalogId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([aliasContainer, sourceContainer]);

        dataControllerService.Setup(service => service.GetDataSourceControllerAsync(
                pipeline,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dataSourceController.Object);

        dataSourceController.Setup(controller => controller.GetAvailabilityAsync(
                "/SOURCE",
                aliasBegin.Value,
                aliasEnd.Value,
                step,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogAvailability([1, 1]));

        dataSourceController.Setup(controller => controller.GetTimeRangeAsync(
                "/SOURCE",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTimeRange(begin, end));

        var appState = new AppState
        {
            CatalogState = new CatalogState(root, new CatalogCache())
        };

        var controller = new CatalogsController(appState, databaseService.Object, acceptedLicenseService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            }
        };

        return (dataControllerService, dataSourceController, controller);
    }
}
