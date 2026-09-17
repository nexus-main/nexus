// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
using System.Text.Json.Serialization;
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
        SchemaProcessors = { new ConfigurationSchemaProcessor() },
        SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true
        }
    };

    private sealed class ConfigurationSchemaProcessor : ISchemaProcessor
    {
        public void Process(SchemaProcessorContext context)
        {
            var type = context.ContextualType.Type;
            var schema = context.Schema;

            AddRequiredConstructorParameters(type, schema);
        }

        private static void AddRequiredConstructorParameters(Type type, JsonSchema schema)
        {
            if (!schema.Type.HasFlag(JsonObjectType.Object))
            {
                return;
            }

            var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Where(constructor => constructor.GetParameters().Length > 0)
                .OrderByDescending(constructor => constructor.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                return;
            }

            foreach (var parameter in constructor.GetParameters())
            {
                if (parameter.HasDefaultValue || parameter.Name is null)
                {
                    continue;
                }

                var propertyName = GetSerializedPropertyName(type, parameter.Name);

                if (propertyName is null || !schema.Properties.ContainsKey(propertyName) || schema.RequiredProperties.Contains(propertyName))
                {
                    continue;
                }

                schema.RequiredProperties.Add(propertyName);
            }
        }

        private static string? GetSerializedPropertyName(Type type, string parameterName)
        {
            var property = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(property => string.Equals(property.Name, parameterName, StringComparison.OrdinalIgnoreCase));

            if (property is null || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                return null;
            }

            return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
                JsonNamingPolicy.CamelCase.ConvertName(property.Name);
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
            var configurationType = DataSourceController.GetConfigurationType(dataSourceType);
            var sourceConfigurationSchema = JsonSchema.FromType(configurationType, _jsonSchemaGeneratorSettings);

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
