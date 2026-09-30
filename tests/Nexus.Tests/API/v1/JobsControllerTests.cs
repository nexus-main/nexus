using System.Security.Claims;
using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Controllers.V1;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Xunit;

namespace API.V1;

public class JobsControllerTests
{
    [Fact]
    public async Task ClearCacheRejectsAliasCatalogEvenWithWritePermission()
    {
        // Arrange
        var catalogManager = new Mock<ICatalogManager>();
        var databaseService = new Mock<IDatabaseService>();
        var jobService = new Mock<IJobService>();
        var serviceProvider = new Mock<IServiceProvider>();
        var pipeline = new DataSourcePipeline([]);
        var sourceContainer = new CatalogContainer(
            new CatalogRegistration("/SOURCE", default),
            Guid.NewGuid(),
            pipeline,
            Array.Empty<Guid>(),
            new CatalogMetadata(default, default, default),
            catalogManager.Object,
            databaseService.Object,
            default!);
        var aliasContainer = new CatalogContainer(
            new CatalogRegistration("/ALIAS", default, LinkTarget: "/SOURCE"),
            Guid.NewGuid(),
            pipeline,
            Array.Empty<Guid>(),
            new CatalogMetadata(default, default, default),
            catalogManager.Object,
            databaseService.Object,
            default!);
        var root = CatalogContainer.CreateRoot(catalogManager.Object, databaseService.Object);

        catalogManager.Setup(manager => manager.GetCatalogContainersAsync(
                It.Is<CatalogContainer>(container => container.Id == CatalogContainer.RootCatalogId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([aliasContainer, sourceContainer]);

        var appState = new AppState
        {
            CatalogState = new CatalogState(root, new CatalogCache())
        };
        var appStateManager = new AppStateManager(
            appState,
            Mock.Of<IPackageService>(),
            Mock.Of<IUpgradeConfigurationService>(),
            Mock.Of<IExtensionHive<IDataSource>>(),
            Mock.Of<IExtensionHive<IDataWriter>>(),
            catalogManager.Object,
            databaseService.Object,
            NullLogger<AppStateManager>.Instance);
        var controller = new JobsController(
            appStateManager,
            jobService.Object,
            serviceProvider.Object,
            Mock.Of<IAcceptedLicenseService>(),
            Mock.Of<Serilog.IDiagnosticContext>(),
            NullLogger<JobsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = CreateUser("^/ALIAS$", "^/ALIAS$")
                }
            }
        };

        // Act
        var actual = await controller.ClearCacheAsync(
            "/ALIAS",
            new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 01, 02, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        var result = Assert.IsType<ObjectResult>(actual.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Alias catalogs are read-only views and cannot be modified.", result.Value);
        jobService.Verify(service => service.AddJob(
            It.IsAny<Job>(),
            It.IsAny<Progress<double>>(),
            It.IsAny<Func<JobControl, CancellationTokenSource, Task<object?>>>()), Times.Never);
    }

    private static ClaimsPrincipal CreateUser(string catalogPattern, string writableCatalogPattern)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(NexusClaimTypes.Subject, "user"),
                new Claim(NexusClaimsConstants.ENABLED_CATALOGS_PATTERN_CLAIM, catalogPattern),
                new Claim(nameof(NexusClaims.CanReadCatalog), catalogPattern),
                new Claim(nameof(NexusClaims.CanWriteCatalog), writableCatalogPattern)
            ],
            authenticationType: "test");

        return new ClaimsPrincipal(identity);
    }
}
