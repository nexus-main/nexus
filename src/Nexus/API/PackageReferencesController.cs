// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;
using Nexus.Services;

namespace Nexus.Controllers;

/// <summary>
/// Provides access to package references.
/// </summary>
[Authorize(Policy = NexusPolicies.RequireAdmin)]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class PackageReferencesController(
    AppState appState,
    AppStateManager appStateManager,
    IExtensionHive extensionHive) : ControllerBase
{
    // GET      /api/packagereferences
    // POST     /api/packagereferences
    // DELETE   /api/packagereferences/{packageReferenceId}
    // GET      /api/packagereferences/{packageReferenceId}/versions

    private readonly AppState _appState = appState;
    private readonly AppStateManager _appStateManager = appStateManager;
    private readonly IExtensionHive _extensionHive = extensionHive;

    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public IDictionary<Guid, PackageReference> Get()
    {
        return _appState.Project.PackageReferences
            .ToDictionary(
                entry => entry.Key,
                entry => new PackageReference(entry.Value.Provider, entry.Value.Configuration));
    }

    /// <summary>
    /// Creates a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to create.</param>
    [HttpPost]
    public async Task<Guid> CreateAsync(
        [FromBody] PackageReference packageReference)
    {
        var internalPackageReference = new InternalPackageReference(
            Id: Guid.NewGuid(),
            Provider: packageReference.Provider,
            Configuration: packageReference.Configuration);

        await _appStateManager.PutPackageReferenceAsync(internalPackageReference);

        return internalPackageReference.Id;
    }

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    [HttpDelete("{packageReferenceId}")]
    public Task DeleteAsync(
        Guid packageReferenceId)
    {
        return _appStateManager.DeletePackageReferenceAsync(packageReferenceId);
    }

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{packageReferenceId}/versions")]
    public async Task<ActionResult<string[]>> GetVersionsAsync(
        Guid packageReferenceId,
        CancellationToken cancellationToken)
    {
        var project = _appState.Project;

        if (!project.PackageReferences.TryGetValue(packageReferenceId, out var packageReference))
            return NotFound($"Unable to find package reference with ID {packageReferenceId}.");

        var result = await _extensionHive
            .GetVersionsAsync(packageReference, cancellationToken);

        return result;
    }
}