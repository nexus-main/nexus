// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namotion.Reflection;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Extensibility;
using Nexus.Services;
using NJsonSchema;
using NJsonSchema.Generation;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to extensions.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class SourcesController(
    IExtensionHive<IDataSource> extensionHive,
    IPipelineService pipelineService
) : ControllerBase
{
    // GET      /api/sources/descriptions
    // GET      /api/sources/pipelines
    // POST     /api/sources/pipelines
    // PUT      /api/sources/pipelines/{pipelineId}
    // DELETE   /api/sources/pipelines/{pipelineId}

    private readonly IExtensionHive<IDataSource> _extensionHive = extensionHive;

    private readonly IPipelineService _pipelineService = pipelineService;

    private static readonly SystemTextJsonSchemaGeneratorSettings _jsonSchemaGeneratorSettings = new()
    {
        ReflectionService = new ConfigurationSchemaReflectionService(),
        SchemaProcessors = { new ConfigurationSchemaProcessor() },
        SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }
    };

    private sealed class ConfigurationSchemaProcessor : ISchemaProcessor
    {
        public void Process(SchemaProcessorContext context)
        {
            var type = context.ContextualType.Type;
            var schema = context.Schema;

            if (type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false) &&
                schema.Type.HasFlag(JsonObjectType.Integer))
            {
                // System.Text.Json accepts any integer in the underlying range, not just
                // named flags or combinations of known bits. Labels are renderer metadata.
                schema.ExtensionData ??= new Dictionary<string, object?>();
                schema.ExtensionData.TryAdd("x-enumValues", schema.Enumeration.ToArray());
                schema.Enumeration.Clear();
                schema.Format = null;
                (schema.Minimum, schema.Maximum) = Type.GetTypeCode(Enum.GetUnderlyingType(type)) switch
                {
                    TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
                    TypeCode.Byte => (byte.MinValue, byte.MaxValue),
                    TypeCode.Int16 => (short.MinValue, short.MaxValue),
                    TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
                    TypeCode.Int32 => (int.MinValue, int.MaxValue),
                    TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
                    TypeCode.Int64 => ((decimal)long.MinValue, (decimal)long.MaxValue),
                    TypeCode.UInt64 => ((decimal)ulong.MinValue, (decimal)ulong.MaxValue),
                    _ => throw new NotSupportedException("Unsupported flags enum underlying type.")
                };
            }
            else if (type == typeof(byte) && schema.Type.HasFlag(JsonObjectType.Integer))
            {
                // OpenAPI/Ajv's byte format denotes a base64 string, not a CLR byte number.
                schema.Format = null;
                schema.Minimum = byte.MinValue;
                schema.Maximum = byte.MaxValue;
            }
        }
    }

    private sealed class ConfigurationSchemaReflectionService : SystemTextJsonReflectionService
    {
        public override void GenerateProperties(
            JsonSchema schema,
            ContextualType contextualType,
            SystemTextJsonSchemaGeneratorSettings settings,
            JsonSchemaGenerator schemaGenerator,
            JsonSchemaResolver schemaResolver)
        {
            base.GenerateProperties(schema, contextualType, settings, schemaGenerator, schemaResolver);

            // NJsonSchema 11.1 does not recognize System.Text.Json's required members.
            // Presence is independent of whether the property's value may be null.
            var typeInfo = new DefaultJsonTypeInfoResolver().GetTypeInfo(contextualType.Type, settings.SerializerOptions);

            foreach (var property in typeInfo.Properties)
            {
                if (property.IsRequired &&
                    schema.Properties.ContainsKey(property.Name) &&
                    !schema.RequiredProperties.Contains(property.Name))
                {
                    schema.RequiredProperties.Add(property.Name);
                }
            }
        }
    }

    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    [HttpGet("descriptions")]
    public List<ExtensionDescription> GetDescriptions()
    {
        var result = GetExtensionDescriptions(_extensionHive.GetExtensions());
        return result;
    }

    /// <summary>
    /// Gets the list of data source pipelines.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <returns></returns>
    [HttpGet("pipelines")]
    public async Task<ActionResult<IDictionary<Guid, DataSourcePipeline>>> GetPipelinesAsync(
        [FromQuery] string? userId = default)
    {
        if (TryAuthenticate(userId, out var actualUserId, out var response))
            return Ok(await _pipelineService.GetAllForUserAsync(actualUserId));

        else
            return response;
    }

    /// <summary>
    /// Creates a data source pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to create.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    [HttpPost("pipelines")]
    public async Task<ActionResult<Guid>> CreatePipelineAsync(
        DataSourcePipeline pipeline,
        [FromQuery] string? userId = default)
    {
        if (TryAuthenticate(userId, out var actualUserId, out var response))
            return Ok(await _pipelineService.PutAsync(actualUserId, pipeline));

        else
            return response;
    }

    /// <summary>
    /// Updates a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to update.</param>
    /// <param name="pipeline">The new pipeline.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    [HttpPut("pipelines/{pipelineId}")]
    public async Task<ActionResult> UpdatePipelineAsync(
        Guid pipelineId,
        DataSourcePipeline pipeline,
        [FromQuery] string? userId = default)
    {
        if (TryAuthenticate(userId, out var actualUserId, out var response))
        {
            if (await _pipelineService.TryUpdateAsync(actualUserId, pipelineId, pipeline))
                return Ok();

            else
                return NotFound();
        }

        else
        {
            return response;
        }
    }

    /// <summary>
    /// Deletes a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to delete.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    [HttpDelete("pipelines/{pipelineId}")]
    public async Task<ActionResult> DeletePipelineAsync(
        Guid pipelineId,
        [FromQuery] string? userId = default)
    {
        if (TryAuthenticate(userId, out var actualUserId, out var response))
        {
            await _pipelineService.DeleteAsync(actualUserId, pipelineId);
            return Ok();
        }

        else
        {
            return response;
        }
    }

    private static List<ExtensionDescription> GetExtensionDescriptions(
        IEnumerable<Type> extensions)
    {
        return extensions.Select(dataSourceType =>
        {
            var configurationType = ConfigurationTypeResolver.Resolve(dataSourceType);
            var sourceConfigurationSchema = new JsonSchema();
            var generator = new JsonSchemaGenerator(_jsonSchemaGeneratorSettings);
            var resolver = new JsonSchemaResolver(sourceConfigurationSchema, _jsonSchemaGeneratorSettings);

            if (configurationType.Nullability == Nullability.Nullable && configurationType.Type != typeof(object))
            {
                // Keep the non-null definition strict, including recursive references to it.
                sourceConfigurationSchema.AnyOf.Add(new JsonSchema { Type = JsonObjectType.Null });
                sourceConfigurationSchema.AnyOf.Add(generator.GenerateWithReference<JsonSchema>(configurationType, resolver));
            }
            else
            {
                generator.Generate(sourceConfigurationSchema, configurationType, resolver);
            }

            var additionalInformation = new Dictionary<string, JsonElement>
            {
                [UI.Core.Constants.SOURCE_CONFIGURATION_SCHEMA_KEY] = JsonSerializer.Deserialize<JsonElement>(sourceConfigurationSchema.ToJson())
            };

            var version = dataSourceType.Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            var attribute = dataSourceType
                .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

            if (attribute is null)
                return new ExtensionDescription(dataSourceType.FullName!, version, default, default, default, additionalInformation);

            else
                return new ExtensionDescription(dataSourceType.FullName!, version, attribute.Description, attribute.ProjectUrl, attribute.RepositoryUrl, additionalInformation);
        })
        .ToList();
    }

    // TODO: code duplication (UsersController)
    private bool TryAuthenticate(
        string? requestedId,
        out string userId,
        [NotNullWhen(returnValue: false)] out ActionResult? response)
    {
        var isAdmin = User.IsInRole(nameof(NexusRoles.Administrator));
        var currentId = User.FindFirstValue(Claims.Subject) ?? throw new Exception("The sub claim is null.");

        if (isAdmin || requestedId is null || requestedId == currentId)
            response = null;

        else
            response = StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to get source registrations of user {requestedId}.");

        userId = requestedId is null
            ? currentId
            : requestedId;

        return response is null;
    }
}
