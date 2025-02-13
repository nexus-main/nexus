// MIT License
// Copyright (c) [2024] [nexus-main]

using Apollo3zehn.PackageManagement;
using Apollo3zehn.PackageManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Core;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to package references.
/// </summary>
[Authorize(Policy = NexusPolicies.RequireAdmin)]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class PackageReferencesController(
    IPackageService packageService) : ControllerBase
{
    // GET      /api/packagereferences
    // POST     /api/packagereferences
    // DELETE   /api/packagereferences/{id}
    // GET      /api/packagereferences/{id}/versions

    private readonly IPackageService _packageService = packageService;

    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public Task<IReadOnlyDictionary<Guid, PackageReference>> GetAsync()
    {
        return _packageService.GetAllAsync();
    }

    /// <summary>
    /// Creates a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to create.</param>
    [HttpPost]
    public Task<Guid> CreateAsync(
        [FromBody] PackageReference packageReference)
    {
        return _packageService.PutAsync(packageReference);
    }

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    [HttpDelete("{id}")]
    public Task DeleteAsync(
        Guid id)
    {
        return _packageService.DeleteAsync(id);
    }

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{id}/versions")]
    public async Task<ActionResult<string[]>> GetVersionsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _packageService.GetVersionsAsync(id, cancellationToken);

        if (result is null)
            return NotFound($"Unable to find package reference with ID {id}.");

        return result;
    }
}