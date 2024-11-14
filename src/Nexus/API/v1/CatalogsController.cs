// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Core;
using Nexus.Core.V1;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using NJsonSchema.Annotations;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Nexus.Controllers.V1;

/// <summary>
/// Provides access to catalogs.
/// </summary>
[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
internal class CatalogsController(
    AppState appState,
    IDatabaseService databaseService,
    IDataControllerService dataControllerService) : ControllerBase
{
    // POST     /api/catalogs/search-items
    // GET      /api/catalogs/{catalogId}
    // GET      /api/catalogs/{catalogId}/child-catalog-infos
    // GET      /api/catalogs/{catalogId}/timerange
    // GET      /api/catalogs/{catalogId}/availability
    // GET      /api/catalogs/{catalogId}/license
    // GET      /api/catalogs/{catalogId}/attachments
    // PUT      /api/catalogs/{catalogId}/attachments
    // DELETE   /api/catalogs/{catalogId}/attachments/{attachmentId}
    // GET      /api/catalogs/{catalogId}/attachments/{attachmentId}/content

    // GET      /api/catalogs/{catalogId}/metadata
    // PUT      /api/catalogs/{catalogId}/metadata

    private readonly AppState _appState = appState;
    private readonly IDatabaseService _databaseService = databaseService;
    private readonly IDataControllerService _dataControllerService = dataControllerService;

    /// <summary>
    /// Searches for the given resource paths and returns the corresponding catalog items.
    /// </summary>
    /// <param name="resourcePaths">The list of resource paths.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpPost("search-items")]
    public async Task<ActionResult<Dictionary<string, CatalogItem>>>
        SearchCatalogItemsAsync(
            [FromBody] string[] resourcePaths,
            CancellationToken cancellationToken)
    {
        var root = _appState.CatalogState.Root;

        // translate resource paths to catalog item requests
        (string ResourcePath, CatalogItemRequest Request)[] resourcePathAndRequests;

        try
        {
            resourcePathAndRequests = await Task.WhenAll(resourcePaths.Distinct().Select(async resourcePath =>
            {
                var catalogItemRequest = await root
                    .TryFindAsync(resourcePath, cancellationToken)
                    ?? throw new ValidationException($"Could not find resource path {resourcePath}.");

                return (resourcePath, catalogItemRequest);
            }));
        }
        catch (ValidationException ex)
        {
            return UnprocessableEntity(ex.Message);
        }

        // authorize
        try
        {
            foreach (var group in resourcePathAndRequests.GroupBy(current => current.Request.Container.Id))
            {
                var catalogContainer = group.First().Request.Container;

                if (!AuthUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
                    throw new UnauthorizedAccessException($"The current user is not permitted to access catalog {catalogContainer.Id}.");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }

        var response = resourcePathAndRequests
            .ToDictionary(item => item.ResourcePath, item => item.Request.Item);

        return response;
    }

    /// <summary>
    /// Gets the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}")]
    public Task<ActionResult<ResourceCatalog>>
        GetAsync(
            string catalogId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = ProtectCatalogAsync<ResourceCatalog>(catalogId, ensureReadable: true, ensureWritable: false, async catalogContainer =>
        {
            var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);
            var catalog = lazyCatalogInfo.Catalog;

            return catalog;
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets a list of child catalog info for the provided parent catalog identifier.
    /// </summary>
    /// <param name="catalogId">The parent catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/child-catalog-infos")]
    public async Task<ActionResult<CatalogInfo[]>>
        GetChildCatalogInfosAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = await ProtectCatalogAsync<CatalogInfo[]>(catalogId, ensureReadable: false, ensureWritable: false, async catalogContainer =>
        {
            var childContainers = await catalogContainer.GetChildCatalogContainersAsync(cancellationToken);

            return childContainers
                .Select(childContainer =>
                {
                    // TODO: Create CatalogInfo along with CatalogContainer to improve performance and reduce GC pressure?

                    var id = childContainer.Id;
                    var title = childContainer.Title;
                    var contact = childContainer.Metadata.Contact;

                    string? readme = default;

                    if (_databaseService.TryReadAttachment(childContainer.Id, "README.md", out var readmeStream))
                    {
                        using var reader = new StreamReader(readmeStream);
                        readme = reader.ReadToEnd();
                    }

                    string? license = default;

                    if (_databaseService.TryReadAttachment(childContainer.Id, "LICENSE.md", out var licenseStream))
                    {
                        using var reader = new StreamReader(licenseStream);
                        license = reader.ReadToEnd();
                    }

                    var isReadable = AuthUtilities.IsCatalogReadable(childContainer.Id, childContainer.Metadata, childContainer.Owner, User);
                    var isWritable = AuthUtilities.IsCatalogWritable(childContainer.Id, childContainer.Metadata, User);

                    var isReleased = childContainer.Owner is null ||
                        childContainer.IsReleasable && Regex.IsMatch(id, childContainer.Pipeline.ReleasePattern ?? "");

                    var isVisible =
                        isReadable || Regex.IsMatch(id, childContainer.Pipeline.VisibilityPattern ?? "");

                    var isOwner = childContainer.Owner?.FindFirstValue(Claims.Subject) == User.FindFirstValue(Claims.Subject);
                    var packageReferenceIds = childContainer.PackageReferenceIds;

                    var pipelineInfo = new PipelineInfo(
                        childContainer.PipelineId,
                        Types: childContainer.Pipeline.Registrations.Select(x => x.Type).ToArray(),
                        InfoUrls: childContainer.Pipeline.Registrations.Select(x => x.InfoUrl).ToArray()
                    );

                    return new CatalogInfo(
                        id,
                        title,
                        contact,
                        readme,
                        license,
                        isReadable,
                        isWritable,
                        isReleased,
                        isVisible,
                        isOwner,
                        packageReferenceIds,
                        pipelineInfo
                    );
                })
                .ToArray();
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets the specified catalog's time range.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/timerange")]
    public Task<ActionResult<CatalogTimeRange>>
        GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = ProtectCatalogAsync<CatalogTimeRange>(catalogId, ensureReadable: true, ensureWritable: false, async catalogContainer =>
        {
            using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.Pipeline, cancellationToken);
            return await dataSource.GetTimeRangeAsync(catalogContainer.Id, cancellationToken);
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets the specified catalog's availability.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="step">Step period.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/availability")]
    public async Task<ActionResult<CatalogAvailability>>
        GetAvailabilityAsync(
            string catalogId,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            [BindRequired] TimeSpan step,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);
        begin = begin.ToUniversalTime();
        end = end.ToUniversalTime();

        if (begin >= end)
            return UnprocessableEntity("The end date/time must be before the begin date/time.");

        if (step <= TimeSpan.Zero)
            return UnprocessableEntity("The step must be > 0.");

        if ((end - begin).Ticks / step.Ticks > 1000)
            return UnprocessableEntity("The number of steps is too large.");

        var response = await ProtectCatalogAsync<CatalogAvailability>(catalogId, ensureReadable: true, ensureWritable: false, async catalogContainer =>
        {
            using var dataSource = await _dataControllerService.GetDataSourceControllerAsync(catalogContainer.Pipeline, cancellationToken);
            return await dataSource.GetAvailabilityAsync(catalogContainer.Id, begin, end, step, cancellationToken);
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets the license of the catalog if available.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/license")]
    [return: CanBeNull]
    public async Task<ActionResult<string?>>
        GetLicenseAsync(
            string catalogId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = await ProtectCatalogAsync<string?>(catalogId, ensureReadable: false, ensureWritable: false, async catalogContainer =>
        {
            string? license = default;

            if (_databaseService.TryReadAttachment(catalogContainer.Id, "LICENSE.md", out var licenseStream))
            {
                using var reader = new StreamReader(licenseStream);
                license = await reader.ReadToEndAsync();
            }

            if (license is null)
            {
                var catalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);
                license = catalogInfo.Catalog.Properties?.GetStringValue(DataModelExtensions.LicenseKey);
            }

            return license;
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets all attachments for the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/attachments")]
    public Task<ActionResult<string[]>>
        GetAttachmentsAsync(
            string catalogId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = ProtectCatalogAsync(catalogId, ensureReadable: true, ensureWritable: false, catalog =>
        {
            return Task.FromResult<ActionResult<string[]>>(_databaseService.EnumerateAttachments(catalogId).ToArray());
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Uploads the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="content">The binary file content.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpPut("{catalogId}/attachments/{attachmentId}")]
    [DisableRequestSizeLimit]
    public Task<ActionResult>
        UploadAttachmentAsync(
            string catalogId,
            string attachmentId,
            [FromBody] Stream content,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);
        attachmentId = WebUtility.UrlDecode(attachmentId);

        var response = ProtectCatalogNonGenericAsync(catalogId, ensureReadable: false, ensureWritable: true, async catalog =>
        {
            try
            {
                using var attachmentStream = _databaseService.WriteAttachment(catalogId, attachmentId);
                await content.CopyToAsync(attachmentStream, cancellationToken);

                return Ok();
            }
            catch (IOException ex)
            {
                return StatusCode(StatusCodes.Status423Locked, ex.Message);
            }
            catch (Exception)
            {
                try
                {
                    if (_databaseService.AttachmentExists(catalogId, attachmentId))
                        _databaseService.DeleteAttachment(catalogId, attachmentId);
                }
                catch (Exception)
                {
                    //
                }

                throw;
            }
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Deletes the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpDelete("{catalogId}/attachments/{attachmentId}")]
    public Task<ActionResult>
        DeleteAttachmentAsync(
            string catalogId,
            string attachmentId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);
        attachmentId = WebUtility.UrlDecode(attachmentId);

        var response = ProtectCatalogNonGenericAsync(catalogId, ensureReadable: false, ensureWritable: true, catalog =>
        {
            try
            {
                _databaseService.DeleteAttachment(catalogId, attachmentId);
                return Task.FromResult<ActionResult>(
                    Ok());
            }
            catch (IOException ex)
            {
                return Task.FromResult<ActionResult>(
                    StatusCode(StatusCodes.Status423Locked, ex.Message));
            }
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/attachments/{attachmentId}/content")]
    public Task<ActionResult>
        GetAttachmentStreamAsync(
            string catalogId,
            string attachmentId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);
        attachmentId = WebUtility.UrlDecode(attachmentId);

        var response = ProtectCatalogNonGenericAsync(catalogId, ensureReadable: true, ensureWritable: false, catalog =>
        {
            try
            {
                if (_databaseService.TryReadAttachment(catalogId, attachmentId, out var attachmentStream))
                {
                    Response.Headers.ContentLength = attachmentStream.Length;
                    return Task.FromResult<ActionResult>(
                        File(attachmentStream, "application/octet-stream", attachmentId));
                }
                else
                {
                    return Task.FromResult<ActionResult>(
                        NotFound($"Could not find attachment {attachmentId} for catalog {catalogId}."));
                }
            }
            catch (IOException ex)
            {
                return Task.FromResult<ActionResult>(
                    StatusCode(StatusCodes.Status423Locked, ex.Message));
            }
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpGet("{catalogId}/metadata")]
    public Task<ActionResult<CatalogMetadata>>
        GetMetadataAsync(
            string catalogId,
            CancellationToken cancellationToken)
    {
        catalogId = WebUtility.UrlDecode(catalogId);

        var response = ProtectCatalogAsync<CatalogMetadata>(catalogId, ensureReadable: true, ensureWritable: false, async catalogContainer =>
        {
            return await Task.FromResult(catalogContainer.Metadata);
        }, cancellationToken);

        return response;
    }

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="metadata">The catalog metadata to set.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    [HttpPut("{catalogId}/metadata")]
    public async Task<ActionResult<object>>
        SetMetadataAsync(
            string catalogId,
            [FromBody] CatalogMetadata metadata,
            CancellationToken cancellationToken)
    {
        if (metadata.Overrides?.Id != catalogId)
            return (ActionResult<object>)UnprocessableEntity("The catalog ID does not match the ID of the catalog to update.");

        catalogId = WebUtility.UrlDecode(catalogId);

        var response = await ProtectCatalogAsync<object>(catalogId, ensureReadable: false, ensureWritable: true, async catalogContainer =>
        {
            await catalogContainer.UpdateMetadataAsync(metadata);

            return new object();

        }, cancellationToken);

        return response;
    }

    private async Task<ActionResult<T>> ProtectCatalogAsync<T>(
        string catalogId,
        bool ensureReadable,
        bool ensureWritable,
        Func<CatalogContainer, Task<ActionResult<T>>> action,
        CancellationToken cancellationToken)
    {
        var root = _appState.CatalogState.Root;

        var catalogContainer = catalogId == CatalogContainer.RootCatalogId
            ? root
            : await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

        if (catalogContainer is not null)
        {
            if (ensureReadable && !AuthUtilities.IsCatalogReadable(
                catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    $"The current user is not permitted to read the catalog {catalogId}.");
            }

            if (ensureWritable && !AuthUtilities.IsCatalogWritable(
                catalogContainer.Id, catalogContainer.Metadata, User))
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    $"The current user is not permitted to modify the catalog {catalogId}.");
            }

            return await action.Invoke(catalogContainer);
        }
        else
        {
            return NotFound(catalogId);
        }
    }

    private async Task<ActionResult> ProtectCatalogNonGenericAsync(
        string catalogId,
        bool ensureReadable,
        bool ensureWritable,
        Func<CatalogContainer, Task<ActionResult>> action,
        CancellationToken cancellationToken)
    {
        var root = _appState.CatalogState.Root;
        var catalogContainer = await root.TryFindCatalogContainerAsync(catalogId, cancellationToken);

        if (catalogContainer is not null)
        {
            if (ensureReadable && !AuthUtilities.IsCatalogReadable(catalogContainer.Id, catalogContainer.Metadata, catalogContainer.Owner, User))
                return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to read the catalog {catalogId}.");

            if (ensureWritable && !AuthUtilities.IsCatalogWritable(catalogContainer.Id, catalogContainer.Metadata, User))
                return StatusCode(StatusCodes.Status403Forbidden, $"The current user is not permitted to modify the catalog {catalogId}.");

            return await action.Invoke(catalogContainer);
        }
        else
        {
            return NotFound(catalogId);
        }
    }
}
