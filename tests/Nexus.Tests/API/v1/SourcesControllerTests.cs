// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Nexus.Controllers.V1;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using NJsonSchema;
using Xunit;

namespace API.V1;

public class SourcesControllerTests
{
    private static readonly JsonSerializerOptions _sourceConfigurationJsonOptions = new(JsonSerializerOptions.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    [Fact]
    public void SharedSchemaFixturesMatchServerOutput()
    {
        var schemas = new Dictionary<string, JsonElement>
        {
            ["nonNullableRoot"] = GetSchema(typeof(NonNullableRootSource)),
            ["nullableRoot"] = GetSchema(typeof(NullableRootSource)),
            ["nullableReference"] = GetSchema(typeof(SchemaSource<ReferenceConfiguration>)),
            ["dictionaryRoot"] = GetSchema(typeof(NonNullableDictionaryRootSource)),
            ["nullableValueRoot"] = GetSchema(typeof(SchemaSource<int?>)),
            ["formats"] = GetSchema(typeof(SchemaSource<FormatConfiguration>))
        };
        var actual = JsonSerializer.SerializeToElement(schemas);
        var expected = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "API/v1/fixtures/source-configuration-schemas.json")));

        Assert.True(JsonElement.DeepEquals(expected, actual), JsonSerializer.Serialize(schemas, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Theory]
    [InlineData("12:34:56", "2026-09-16T12:34:56")]
    [InlineData("23:59:59.1234567", "2026-09-16T12:34:56.1234567Z")]
    [InlineData("00:00:00", "2026-09-16T12:34:56+02:00")]
    public void FormatFixturesRepresentRuntimeLocalTimesAndDateTimes(string time, string timestamp)
    {
        var json = $$"""
            {"id":"12345678-1234-1234-1234-123456789abc","count":18446744073709551615,
             "amount":123.456,"time":"{{time}}","timestamp":"{{timestamp}}"}
            """;
        var configuration = JsonSerializer.Deserialize<FormatConfiguration>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), configuration.Id);
        Assert.Equal(ulong.MaxValue, configuration.Count);
        Assert.Equal(123.456m, configuration.Amount);
        Assert.Equal(TimeOnly.Parse(time), configuration.Time);
    }

    [Theory]
    [InlineData("{\"requiredNullable\":null}", true)]
    [InlineData("{\"requiredNullable\":\"value\"}", true)]
    [InlineData("{}", false)]
    [InlineData("{\"requiredNullable\":null,\"optionalNonNullable\":null}", false)]
    [InlineData("{\"requiredNullable\":null,\"optionalNullable\":null}", true)]
    [InlineData("{\"requiredNullable\":null,\"optionalNonNullable\":\"value\"}", true)]
    [InlineData("{\"requiredNullable\":42}", false)]
    public async Task GeneratedSchemaSeparatesRequiredFromNullable(string json, bool valid)
    {
        var document = GetSchema(typeof(SchemaSource<PropertyConfiguration>));
        var required = document.GetProperty("required").EnumerateArray().Select(item => item.GetString());
        Assert.Equal(["requiredNullable"], required);
        Assert.Equal("http://json-schema.org/draft-04/schema#", document.GetProperty("$schema").GetString());

        var schema = await JsonSchema.FromJsonAsync(document.GetRawText());
        Assert.Equal(valid, schema.Validate(json).Count == 0);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"child\":null}", true)]
    [InlineData("{\"child\":{\"name\":\"value\"}}", true)]
    [InlineData("{\"child\":{}}", false)]
    [InlineData("{\"child\":{\"name\":null}}", false)]
    [InlineData("{\"child\":42}", false)]
    public async Task GeneratedSchemaResolvesNullableReferences(string json, bool valid)
    {
        var document = GetSchema(typeof(SchemaSource<ReferenceConfiguration>));
        var alternatives = document.GetProperty("properties").GetProperty("child").GetProperty("oneOf");
        Assert.Contains(alternatives.EnumerateArray(), item =>
            item.TryGetProperty("type", out var type) && type.GetString() == "null");
        Assert.Contains(alternatives.EnumerateArray(), item =>
            item.TryGetProperty("$ref", out var reference) && reference.GetString() == "#/definitions/ChildConfiguration");
        Assert.True(document.GetProperty("definitions").TryGetProperty("ChildConfiguration", out _));

        var schema = await JsonSchema.FromJsonAsync(document.GetRawText());
        Assert.Equal(valid, schema.Validate(json).Count == 0);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"arbitrary-key\":{\"name\":\"value\"}}", true)]
    [InlineData("{\"arbitrary-key\":{}}", false)]
    [InlineData("{\"arbitrary-key\":42}", false)]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    public async Task GeneratedDictionaryRootValidatesAdditionalProperties(string json, bool valid)
    {
        var document = GetSchema(typeof(SchemaSource<IReadOnlyDictionary<string, ChildConfiguration>>));
        Assert.Equal("object", document.GetProperty("type").GetString());
        Assert.Equal("#/definitions/ChildConfiguration",
            document.GetProperty("additionalProperties").GetProperty("$ref").GetString());

        var schema = await JsonSchema.FromJsonAsync(document.GetRawText());
        Assert.Equal(valid, schema.Validate(json).Count == 0);
    }

    [Theory]
    [InlineData(typeof(DirectNonNullableSource), false)]
    [InlineData(typeof(NonNullableRootSource), false)]
    [InlineData(typeof(NonNullableChainSource), false)]
    [InlineData(typeof(NonNullableDirectChainSource), false)]
    [InlineData(typeof(NonNullableInterfaceSource), false)]
    [InlineData(typeof(ObliviousRootSource), false)]
    [InlineData(typeof(ObliviousDirectSource), false)]
    public async Task RootNullabilityFollowsSourceDeclaration(Type source, bool nullable)
    {
        var schema = await JsonSchema.FromJsonAsync(GetSchema(source).GetRawText());

        Assert.Equal(nullable, schema.Validate("null").Count == 0);
        Assert.Empty(schema.Validate("{\"requiredNullable\":null}"));
        Assert.NotEmpty(schema.Validate("{}"));
        Assert.NotEmpty(schema.Validate("{\"requiredNullable\":null,\"optionalNonNullable\":null}"));
        Assert.NotEmpty(schema.Validate("42"));
        Assert.NotEmpty(schema.Validate("[]"));
    }

    [Theory]
    [InlineData(typeof(SchemaSource<int>), false)]
    [InlineData(typeof(SchemaSource<int?>), true)]
    [InlineData(typeof(DirectSource<int>), false)]
    [InlineData(typeof(DirectSource<int?>), true)]
    [InlineData(typeof(ValueChainSource), false)]
    [InlineData(typeof(NullableValueChainSource), true)]
    [InlineData(typeof(AnnotatedValueChainSource), true)]
    [InlineData(typeof(NullableEnumSource), true)]
    [InlineData(typeof(NonNullableEnumSource), false)]
    public async Task ValueRootNullabilityFollowsSourceDeclaration(Type source, bool nullable)
    {
        var schema = await JsonSchema.FromJsonAsync(GetSchema(source).GetRawText());

        Assert.Equal(nullable, schema.Validate("null").Count == 0);
        Assert.Empty(schema.Validate("0"));
        Assert.NotEmpty(schema.Validate("\"42\""));
        Assert.NotEmpty(schema.Validate("{}"));
    }

    [Fact]
    public async Task GenericSubstitutionPreservesNestedNonNullableItems()
    {
        var schema = await JsonSchema.FromJsonAsync(GetSchema(typeof(NonNullableItemsSource)).GetRawText());

        Assert.NotEmpty(schema.Validate("null"));
        Assert.Empty(schema.Validate("[]"));
        Assert.NotEmpty(schema.Validate("[null]"));
        Assert.Empty(schema.Validate("[{\"requiredNullable\":null}]"));
        Assert.NotEmpty(schema.Validate("[{}]"));
    }

    [Fact]
    public async Task JsonRequiredPropertyMustBePresentButCanBeNull()
    {
        var schema = await JsonSchema.FromJsonAsync(GetSchema(typeof(SchemaSource<PropertyConfiguration>)).GetRawText());

        Assert.NotEmpty(schema.Validate("{}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PropertyConfiguration>("{}", JsonSerializerOptions.Web));
        Assert.Empty(schema.Validate("{\"requiredNullable\":null}"));
        Assert.NotNull(JsonSerializer.Deserialize<PropertyConfiguration>("{\"requiredNullable\":null}", JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("42")]
    public async Task SampleObjectRootRemainsUnconstrained(string json)
    {
        var schema = await JsonSchema.FromJsonAsync(GetSchema(typeof(Nexus.Sources.Sample)).GetRawText());
        Assert.Empty(schema.Validate(json));
    }

    [Theory]
    [InlineData("{\"renamed\":null}", true)]
    [InlineData("{\"renamed\":\"value\"}", true)]
    [InlineData("{}", false)]
    [InlineData("{\"value\":null}", false)]
    public async Task GeneratedSchemaHonorsRequiredKeywordAndSerializedNames(string json, bool valid)
    {
        var document = GetSchema(typeof(SchemaSource<RequiredConfiguration>));
        Assert.Equal(["renamed"], document.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var schema = await JsonSchema.FromJsonAsync(document.GetRawText());
        Assert.Equal(valid, schema.Validate(json).Count == 0);

        if (valid)
            Assert.NotNull(JsonSerializer.Deserialize<RequiredConfiguration>(json, JsonSerializerOptions.Web));
        else
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequiredConfiguration>(json, JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("{\"requiredNonNullable\":\"value\",\"requiredNullable\":null,\"requiredValue\":1}", true, true)]
    [InlineData("{\"requiredNonNullable\":\"value\",\"requiredNullable\":\"value\",\"requiredValue\":1}", true, true)]
    [InlineData("{}", false, false)]
    [InlineData("{\"requiredNonNullable\":\"value\",\"requiredValue\":1}", false, false)]
    [InlineData("{\"requiredNonNullable\":null,\"requiredNullable\":null,\"requiredValue\":1}", false, true)]
    [InlineData("{\"requiredNonNullable\":\"value\",\"requiredNullable\":null,\"requiredValue\":1,\"optionalNonNullable\":null}", false, true)]
    public async Task GeneratedSchemaHonorsRequiredConstructorParameters(string json, bool schemaValid, bool runtimeValid)
    {
        var document = GetSchema(typeof(SchemaSource<ConstructorParameterConfiguration>));
        Assert.Equal(["requiredNonNullable", "requiredNullable", "requiredValue"],
            document.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var schema = await JsonSchema.FromJsonAsync(document.GetRawText());
        Assert.Equal(schemaValid, schema.Validate(json).Count == 0);

        if (runtimeValid)
            Assert.NotNull(JsonSerializer.Deserialize<ConstructorParameterConfiguration>(json, _sourceConfigurationJsonOptions));
        else
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConstructorParameterConfiguration>(json, _sourceConfigurationJsonOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineListDefaultsToCurrentUserEvenForAdministrators(bool isAdmin)
    {
        const string USER_ID = "current-user";
        var pipelines = new Mock<IPipelineService>(MockBehavior.Strict);
        IReadOnlyDictionary<Guid, DataSourcePipeline> expected = new Dictionary<Guid, DataSourcePipeline>();
        pipelines.Setup(service => service.GetAllForUserAsync(USER_ID)).ReturnsAsync(expected);
        var identity = new ClaimsIdentity([new Claim("sub", USER_ID)], "test");

        if (isAdmin)
            identity.AddClaim(new Claim(ClaimTypes.Role, "Administrator"));

        var controller = new SourcesController(Mock.Of<IExtensionHive<IDataSource>>(), pipelines.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };

        var response = await controller.GetPipelinesAsync();

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
        pipelines.Verify(service => service.GetAllForUserAsync(USER_ID), Times.Once);
        pipelines.VerifyNoOtherCalls();
    }

    private static JsonElement GetSchema(Type sourceType)
    {
        var hive = new Mock<IExtensionHive<IDataSource>>();
        hive.Setup(service => service.GetExtensions()).Returns([sourceType]);
        var controller = new SourcesController(hive.Object, Mock.Of<IPipelineService>());

        return Assert.Single(controller.GetDescriptions()).AdditionalInformation![Nexus.UI.Core.Constants.SOURCE_CONFIGURATION_SCHEMA_KEY];
    }

    public class PropertyConfiguration
    {
        [JsonRequired]
        public string? RequiredNullable { get; set; }

        public string OptionalNonNullable { get; set; } = "default";

        public string? OptionalNullable { get; set; }
    }

    public class FormatConfiguration
    {
        public Guid Id { get; set; }
        public ulong Count { get; set; }
        public decimal Amount { get; set; }
        public TimeOnly Time { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ReferenceConfiguration
    {
        public ChildConfiguration? Child { get; set; }
    }

    public class ChildConfiguration
    {
        [JsonRequired]
        public string Name { get; set; } = "default";
    }

    public class RequiredConfiguration
    {
        [JsonPropertyName("renamed")]
        public required string? Value { get; set; }

        [JsonIgnore]
        public string? Ignored { get; set; }
    }

    public record ConstructorParameterConfiguration(
        string RequiredNonNullable,
        string? RequiredNullable,
        int RequiredValue,
        string OptionalNonNullable = "default",
        string? OptionalNullable = null,
        int OptionalValue = 42
    );

    public abstract class SchemaSource<T> : SimpleDataSource<T>;

    public abstract class NullableRootSource : SimpleDataSource<PropertyConfiguration?>;

    public abstract class NonNullableRootSource : SimpleDataSource<PropertyConfiguration>;

    public abstract class NullableDictionaryRootSource : SimpleDataSource<IReadOnlyDictionary<string, ChildConfiguration>?>;

    public abstract class NonNullableDictionaryRootSource : SimpleDataSource<IReadOnlyDictionary<string, ChildConfiguration>>;

    public class ConcreteNullableSource : SimpleDataSource<PropertyConfiguration?>
    {
        public override Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task<ResourceCatalog> EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public override Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData,
            IProgress<double> progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

#nullable disable
    public abstract class ObliviousRootSource : SimpleDataSource<PropertyConfiguration>;

    public abstract class ObliviousDirectSource : SourceOperations, IDataSource<PropertyConfiguration>
    {
        public abstract Task SetContextAsync(DataSourceContext<PropertyConfiguration> context, ILogger logger, CancellationToken cancellationToken);
    }
#nullable restore

    public abstract class ChainSource<TIgnored, TConfiguration> : SchemaSource<TConfiguration>;

    public abstract class NullableChainSource : ChainSource<string, PropertyConfiguration?>;

    public abstract class NonNullableChainSource : ChainSource<string?, PropertyConfiguration>;

    public abstract class AnnotatedSource<T> : SchemaSource<T?> where T : class;

    public abstract class AnnotatedChainSource : AnnotatedSource<PropertyConfiguration>;

    public abstract class ValueChainSource : ChainSource<string, int>;

    public abstract class NullableValueChainSource : ChainSource<string, int?>;

    public abstract class AnnotatedValueSource<T> : SimpleDataSource<T?> where T : struct;

    public abstract class AnnotatedValueChainSource : AnnotatedValueSource<int>;

    public enum ConfigurationMode { Default, Custom }

    public abstract class NullableEnumSource : SimpleDataSource<ConfigurationMode?>;

    public abstract class NonNullableEnumSource : SimpleDataSource<ConfigurationMode>;

    public abstract class ItemsSource<T> : ChainSource<int, List<T>>;

    public abstract class NullableItemsSource : ItemsSource<PropertyConfiguration?>;

    public abstract class NonNullableItemsSource : ItemsSource<PropertyConfiguration>;

    public class RecursiveConfiguration
    {
        public RecursiveConfiguration Next { get; set; } = null!;
    }

    public abstract class RecursiveSource : SimpleDataSource<RecursiveConfiguration?>;

    public abstract class DirectNullableSource : SourceOperations, IDataSource<PropertyConfiguration?>
    {
        public abstract Task SetContextAsync(DataSourceContext<PropertyConfiguration?> context, ILogger logger, CancellationToken cancellationToken);
    }

    public abstract class DirectNonNullableSource : SourceOperations, IDataSource<PropertyConfiguration>
    {
        public abstract Task SetContextAsync(DataSourceContext<PropertyConfiguration> context, ILogger logger, CancellationToken cancellationToken);
    }

    public abstract class DirectSource<T> : SourceOperations, IDataSource<T>
    {
        public abstract Task SetContextAsync(DataSourceContext<T> context, ILogger logger, CancellationToken cancellationToken);
    }

    public abstract class DirectChainSource<T> : DirectSource<T>;

    public abstract class NullableDirectChainSource : DirectChainSource<PropertyConfiguration?>;

    public abstract class NonNullableDirectChainSource : DirectChainSource<PropertyConfiguration>;

    public interface ISource<T> : IDataSource<T>;

    public abstract class NullableInterfaceSource : SourceOperations, ISource<PropertyConfiguration?>
    {
        public abstract Task SetContextAsync(DataSourceContext<PropertyConfiguration?> context, ILogger logger, CancellationToken cancellationToken);
    }

    public abstract class NonNullableInterfaceSource : SourceOperations, ISource<PropertyConfiguration>
    {
        public abstract Task SetContextAsync(DataSourceContext<PropertyConfiguration> context, ILogger logger, CancellationToken cancellationToken);
    }

    public abstract class SourceOperations : IDataSource
    {
        public abstract Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);
        public abstract Task<ResourceCatalog> EnrichCatalogAsync(ResourceCatalog catalog, CancellationToken cancellationToken);
        public abstract Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);
        public abstract Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);
        public abstract Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, ReadDataHandler readData,
            IProgress<double> progress, CancellationToken cancellationToken);
    }
}
