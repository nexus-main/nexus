// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Moq;
using Nexus.Core.V1;
using Nexus.Services;
using Nexus.Utilities;
using Xunit;

namespace Services;

public class PipelineServiceTests
{
    delegate bool GobbleReturns(out string? pipelineMap);

    [Fact]
    public async Task CanCreatePipeline()
    {
        // Arrange
        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, []);

        var dataSourceRegistration1 = new DataSourceRegistration(
            Type: "foo",
            default!,
            JsonSerializer.Deserialize<JsonElement>("null"),
            default!
        );

        var dataSourceRegistration2 = new DataSourceRegistration(
            Type: "bar",
            default!,
            JsonSerializer.Deserialize<JsonElement>("null"),
            default!
        );

        var pipeline = new DataSourcePipeline(
            Registrations: [
                dataSourceRegistration1,
                dataSourceRegistration2
            ]
        );

        // Act
        var expectedId = await pipelineService.PutAsync(pipeline);

        // Assert
        var jsonString = File.ReadAllText(filePath);
        var actualPipelineMap = JsonSerializer.Deserialize<Dictionary<Guid, DataSourcePipeline>>(jsonString, JsonSerializerOptions.Web)!;
        var entry = Assert.Single(actualPipelineMap);

        Assert.Equal(expectedId, entry.Key);

        Assert.Collection(entry.Value.Registrations,
            entry1_1 =>
            {
                Assert.Equal(dataSourceRegistration1.Type, entry1_1.Type);
            },
            entry1_2 =>
            {
                Assert.Equal(dataSourceRegistration2.Type, entry1_2.Type);
            }
        );
    }

    [Fact]
    public async Task CanGetPipeline()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var pipelineMap = new Dictionary<Guid, DataSourcePipeline>()
        {
            [id1] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".^"
            ),
            [id2] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".*"
            )
        };

        var pipelineService = GetPipelineService(default!, pipelineMap);

        // Act
        var actualPipeline = await pipelineService.GetAsync(id2);

        // Assert
        Assert.Equal(
            expected: JsonSerializer.Serialize(pipelineMap[id2]),
            actual: JsonSerializer.Serialize(actualPipeline)
        );
    }

    [Fact]
    public async Task CanTryUpdatePipeline()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var pipelineMap = new Dictionary<Guid, DataSourcePipeline>()
        {
            [id1] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".^"
            ),
            [id2] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".*"
            )
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, pipelineMap);

        var newPipeline = new DataSourcePipeline(
            Registrations: [],
            VisibilityPattern: "foo"
        );

        var expected = pipelineMap.ToDictionary(x => x.Key, x => x.Value);
        expected[id1] = newPipeline;

        // Act
        var success = await pipelineService.TryUpdateAsync(id1, newPipeline);

        // Assert
        Assert.True(success);

        var actual = JsonSerializer.Deserialize<Dictionary<Guid, DataSourcePipeline>>(
            File.ReadAllText(filePath),
            JsonSerializerOptions.Web
        );

        Assert.Equivalent(expected, actual, strict: true);
    }

    [Fact]
    public async Task CanDeletePipeline()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var pipelineMap = new Dictionary<Guid, DataSourcePipeline>()
        {
            [id1] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".^"
            ),
            [id2] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".*"
            )
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, pipelineMap);

        // Act
        await pipelineService.DeleteAsync(id1);

        // Assert
        pipelineMap.Remove(id1);
        var expected = JsonSerializerHelper.SerializeIndented(pipelineMap);
        var actual = File.ReadAllText(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanGetAllPipelines()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var pipelineMap = new Dictionary<Guid, DataSourcePipeline>()
        {
            [id1] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".^"
            ),
            [id2] = new DataSourcePipeline(
                Registrations: [],
                VisibilityPattern: ".*"
            )
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, pipelineMap);

        // Act
        var actualPipelineMap = await pipelineService.GetAllAsync();

        // Assert
        var expected = JsonSerializerHelper.SerializeIndented(pipelineMap.OrderBy(current => current.Key));
        var actual = JsonSerializerHelper.SerializeIndented(actualPipelineMap.OrderBy(current => current.Key));

        Assert.Equal(expected, actual);
    }

    private static IPipelineService GetPipelineService(
        string filePath,
        Dictionary<Guid, DataSourcePipeline> pipelineMap
    )
    {
        var databaseService = Mock.Of<IDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadPipelineMap(out It.Ref<string?>.IsAny))
            .Returns(new GobbleReturns((out string? pipelineMapString) =>
            {
                pipelineMapString = JsonSerializer.Serialize(pipelineMap);
                return true;
            }));

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.WritePipelineMap())
            .Returns(() => File.OpenWrite(filePath));

        var pipelineService = new PipelineService(databaseService);

        return pipelineService;
    }
}
