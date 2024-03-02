using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Services;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to extensions.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class SourcesController : ControllerBase
    {
        // GET      /api/sources/descriptions
        // GET      /api/sources/registrations
        // POST     /api/sources/registrations
        // DELETE   /api/sources/registrations/{registrationId}

        #region Fields

        private readonly AppState _appState;
        private readonly AppStateManager _appStateManager;
        private readonly IExtensionHive _extensionHive;

        #endregion

        #region Constructors

        public SourcesController(
            AppState appState,
            AppStateManager appStateManager,
            IExtensionHive extensionHive)
        {
            _appState = appState;
            _appStateManager = appStateManager;
            _extensionHive = extensionHive;
        }

        #endregion

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
        /// Gets the list of data source registrations.
        /// </summary>
        /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
        /// <returns></returns>
        [HttpGet("registrations")]
        public ActionResult<IDictionary<Guid, DataSourceRegistration>> GetRegistrations(
            [FromQuery] string? userId = default)
        {
            if (TryAuthenticate(userId, out var actualUserId, out var response))
            {
                if (_appState.Project.UserConfigurations.TryGetValue(actualUserId, out var userConfiguration))
                    return Ok(userConfiguration.DataSourceRegistrations
                        .ToDictionary(
                            entry => entry.Key,
                            entry => new DataSourceRegistration(
                                entry.Value.Type,
                                entry.Value.ResourceLocator,
                                entry.Value.Configuration,
                                entry.Value.InfoUrl,
                                entry.Value.ReleasePattern,
                                entry.Value.VisibilityPattern)));

                else
                    return Ok(new Dictionary<Guid, DataSourceRegistration>());
            }

            else
            {
                return response;
            }
        }

        /// <summary>
        /// Creates a data source registration.
        /// </summary>
        /// <param name="registration">The registration to create.</param>
        /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
        [HttpPost("registrations")]
        public async Task<ActionResult<Guid>> CreateRegistrationAsync(
            [FromBody] DataSourceRegistration registration,
            [FromQuery] string? userId = default)
        {
            if (TryAuthenticate(userId, out var actualUserId, out var response))
            {
                var internalRegistration = new InternalDataSourceRegistration(
                    Id: Guid.NewGuid(),
                    registration.Type,
                    registration.ResourceLocator,
                    registration.Configuration,
                    registration.InfoUrl,
                    registration.ReleasePattern,
                    registration.VisibilityPattern);

                await _appStateManager.PutDataSourceRegistrationAsync(actualUserId, internalRegistration);
                return Ok(internalRegistration.Id);
            }

            else
            {
                return response;
            }
        }

        /// <summary>
        /// Deletes a data source registration.
        /// </summary>
        /// <param name="registrationId">The identifier of the registration.</param>
        /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
        [HttpDelete("registrations/{registrationId}")]
        public async Task<ActionResult> DeleteRegistrationAsync(
            Guid registrationId,
            [FromQuery] string? userId = default)
        {
            if (TryAuthenticate(userId, out var actualUserId, out var response))
            {
                await _appStateManager.DeleteDataSourceRegistrationAsync(actualUserId, registrationId);
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
}
