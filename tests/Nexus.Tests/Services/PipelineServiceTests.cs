// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;
using Moq;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Services;
using Nexus.Utilities;
using Xunit;

namespace Services;

public class PipelineServiceTests
{
    delegate bool GobbleReturns(string userId, out string? pipelineMap);

    private const string USERNAME_1 = "starlord";
    private const string USERNAME_2 = "groot";

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
        var expectedId = await pipelineService.PutAsync(
            USERNAME_1,
            pipeline
        );

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

        var userToPipelinesMap = new Dictionary<string, Dictionary<Guid, DataSourcePipeline>>()
        {
            [USERNAME_1] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".^"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".*"
                )
            }
        };

        var pipelineService = GetPipelineService(default!, userToPipelinesMap);

        // Act
        var actualPipeline = await pipelineService.GetAsync(USERNAME_1, id2);

        // Assert
        Assert.Equal(
            expected: JsonSerializer.Serialize(userToPipelinesMap[USERNAME_1][id2]),
            actual: JsonSerializer.Serialize(actualPipeline)
        );
    }

    [Fact]
    public async Task CanTryUpdatePipeline()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var userToPipelinesMap = new Dictionary<string, Dictionary<Guid, DataSourcePipeline>>()
        {
            [USERNAME_1] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".^"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".*"
                )
            }
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, userToPipelinesMap);

        var newPipeline = new DataSourcePipeline(
            Registrations: [],
            ReleasePattern: "foo"
        );

        var expected = userToPipelinesMap[USERNAME_1]
            .ToDictionary(x => x.Key, x => x.Value);

        expected[id1] = newPipeline;

        // Act
        var success = await pipelineService.TryUpdateAsync(USERNAME_1, id1, newPipeline);

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

        var userToPipelinesMap = new Dictionary<string, Dictionary<Guid, DataSourcePipeline>>()
        {
            [USERNAME_1] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".^"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".*"
                )
            }
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, userToPipelinesMap);

        // Act
        await pipelineService.DeleteAsync(USERNAME_1, id1);

        // Assert
        userToPipelinesMap[USERNAME_1].Remove(id1);
        var expected = JsonSerializerHelper.SerializeIndented(userToPipelinesMap[USERNAME_1]);
        var actual = File.ReadAllText(filePath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanGetAllPipelinesForUser()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var userToPipelinesMap = new Dictionary<string, Dictionary<Guid, DataSourcePipeline>>()
        {
            [USERNAME_1] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".^"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".*"
                )
            }
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, userToPipelinesMap);

        // Act
        var actualPipelineMap = await pipelineService.GetAllForUserAsync(USERNAME_1);

        // Assert
        var expected = JsonSerializerHelper.SerializeIndented(userToPipelinesMap[USERNAME_1].OrderBy(current => current.Key));
        var actual = JsonSerializerHelper.SerializeIndented(actualPipelineMap.OrderBy(current => current.Key));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CanGetAllPipelines()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var userToPipelinesMap = new Dictionary<string, Dictionary<Guid, DataSourcePipeline>>()
        {
            [USERNAME_1] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".^"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: ".*"
                )
            },
            [USERNAME_2] = new()
            {
                [id1] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: "abc"
                ),
                [id2] = new DataSourcePipeline(
                    Registrations: [],
                    ReleasePattern: "def"
                )
            }
        };

        var filePath = Path.GetTempFileName();
        var pipelineService = GetPipelineService(filePath, userToPipelinesMap);

        // Act
        var actualUserToPipelinesMap = await pipelineService.GetAllAsync();

        // Assert
        var expected = JsonSerializerHelper.SerializeIndented(userToPipelinesMap.SelectMany(x => x.Value.OrderBy(y => y.Key)));
        var actual = JsonSerializerHelper.SerializeIndented(actualUserToPipelinesMap.SelectMany(x => x.Value.OrderBy(y => y.Key)));

        Assert.Equal(expected, actual);
    }

    private static IPipelineService GetPipelineService(
        string filePath,
        Dictionary<string, Dictionary<Guid, DataSourcePipeline>> userToPipelinesMap
    )
    {
        var databaseService = Mock.Of<IDatabaseService>();

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.EnumerateUsers())
            .Returns([USERNAME_1, USERNAME_2]);

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.TryReadPipelineMap(It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns(new GobbleReturns((string userId, out string? pipelineMapString) =>
            {
                if (userToPipelinesMap.ContainsKey(userId))
                {
                    pipelineMapString = JsonSerializer.Serialize(userToPipelinesMap[userId]);
                    return true;
                }

                else
                {
                    pipelineMapString = default;
                    return false;
                }
            }));

        Mock.Get(databaseService)
            .Setup(databaseService => databaseService.WritePipelineMap(It.IsAny<string>()))
            .Returns(() => File.OpenWrite(filePath));

        var pipelineService = new PipelineService(databaseService);

        return pipelineService;
    }
}