// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.Extensibility;
using Nexus.PackageManagement.Services;
using Nexus.Services;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Claims;
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
    IExtensionHive extensionHive,
    IPipelineService pipelineService
) : ControllerBase
{
    // GET      /api/sources/descriptions
    // GET      /api/sources/pipelines
    // POST     /api/sources/pipelines
    // DELETE   /api/sources/pipelines/{pipelineId}

    private readonly IExtensionHive _extensionHive = extensionHive;

    private readonly IPipelineService _pipelineService = pipelineService;

    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    [HttpGet("descriptions")]
    public List<ExtensionDescription> GetDescriptions()
    {
        var result = GetExtensionDescriptions(_extensionHive.GetExtensions<IDataSource>());
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
    /// Deletes a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline.</param>
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
        return extensions.Select(type =>
        {
            var version = type.Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            var attribute = type
                .GetCustomAttribute<ExtensionDescriptionAttribute>(inherit: false);

            if (attribute is null)
                return new ExtensionDescription(type.FullName!, version, default, default, default, default);

            else
                return new ExtensionDescription(type.FullName!, version, attribute.Description, attribute.ProjectUrl, attribute.RepositoryUrl, default);
        })
        .ToList();
    }

    // TODO: code duplication (UsersController)
    private bool TryAuthenticate(
        string? requestedId,
        out string userId,
        [NotNullWhen(returnValue: false)] out ActionResult? response)
    {
        var isAdmin = User.IsInRole(NexusRoles.ADMINISTRATOR);
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
