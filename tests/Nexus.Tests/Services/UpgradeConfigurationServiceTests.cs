// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Apollo3zehn.PackageManagement.Services;
using DataSource;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core.V1;
using Nexus.Extensibility;
using Nexus.Services;
using Xunit;

namespace Services;

public class UpgradeConfigurationServiceTests
{
    [Fact]
    public async Task CanUpgrade()
    {
        // Arrange
        const string USER_ID = "UserA";

        var registration = new DataSourceRegistration(
            Type: typeof(TestSource).FullName!,
            ResourceLocator: new Uri("", UriKind.Relative),
            Configuration: JsonSerializer.Deserialize<JsonElement>
            (
                $$"""
                {
                    "foo": 1.99
                }
                """
            ),
            default
        );

        var expected = new DataSourcePipeline(
            [
                registration with {
                    Configuration = JsonSerializer.Deserialize<JsonElement>
                    (
                        $$"""
                        {
                            "version": 2,
                            "bar": 1.99
                        }
                        """
                    ),
                }
            ]
        );

        /* extensionHive */
        var extensionHive = Mock.Of<IExtensionHive<IDataSource>>();

        Mock.Get(extensionHive)
            .Setup(extensionHive => extensionHive.GetExtensionType(It.IsAny<string>()))
            .Returns(typeof(TestSource));

        /* pipelineService */
        var pipelineService = Mock.Of<IPipelineService>();
        var pipelineId = Guid.NewGuid();

        Mock.Get(pipelineService)
            .Setup(pipelineService => pipelineService.GetAllAsync())
            .ReturnsAsync(() =>
            {
                return new Dictionary<string, IReadOnlyDictionary<Guid, DataSourcePipeline>>
                {
                    [USER_ID] = new Dictionary<Guid, DataSourcePipeline>()
                    {
                        [pipelineId] = new DataSourcePipeline([registration])
                    }
                };
            });

        var actual = default(DataSourcePipeline);

        Mock.Get(pipelineService)
            .Setup(pipelineService => pipelineService.TryUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataSourcePipeline>()))
            .Callback<string, Guid, DataSourcePipeline>((_, _, newPipeline) => actual = newPipeline);

        /* upgradeConfigurationService */
        var upgradeConfigurationService = new UpgradeConfigurationService(
            pipelineService,
            extensionHive,
            NullLogger<UpgradeConfigurationService>.Instance
        );

        // Act
        await upgradeConfigurationService.UpgradeAsync();

        // Assert
        Assert.Equal(
            expected: JsonSerializer.Serialize(expected),
            actual: JsonSerializer.Serialize(actual)
        );
    }
}