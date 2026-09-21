// MIT License
// Copyright (c) [2024] [nexus-main]

using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Services;

internal interface IAcceptedLicenseService
{
    Task AcceptAsync(string userId, string catalogId, string license);

    Task<string?> GetLicenseAsync(CatalogContainer catalogContainer, CancellationToken cancellationToken);

    bool HasAccepted(ClaimsPrincipal user, string catalogId, string? license);
}

internal record AcceptedLicense(
    string CatalogId,
    DateTime AcceptedAtUtc,
    string LicenseText
);

internal class AcceptedLicenseService(
    IOptions<PathsOptions> pathsOptions,
    IDatabaseService databaseService) : IAcceptedLicenseService
{
    private const string USERS = "users";

    private const string ACCEPTED_LICENSES = "accepted-licenses";

    private const string FILE_EXTENSION = ".json";

    private readonly PathsOptions _pathsOptions = pathsOptions.Value;

    private readonly IDatabaseService _databaseService = databaseService;

    public async Task AcceptAsync(string userId, string catalogId, string license)
    {
        var filePath = GetFilePath(userId, catalogId, license);
        var folderPath = Path.GetDirectoryName(filePath)!;

        Directory.CreateDirectory(folderPath);

        var acceptedLicense = new AcceptedLicense(
            catalogId,
            DateTime.UtcNow,
            license
        );

        await using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
        JsonSerializerHelper.SerializeIndented(stream, acceptedLicense);
    }

    public async Task<string?> GetLicenseAsync(CatalogContainer catalogContainer, CancellationToken cancellationToken)
    {
        string? license = default;

        if (_databaseService.TryReadAttachment(catalogContainer.Id, "LICENSE.md", out var licenseStream))
        {
            using var reader = new StreamReader(licenseStream);
            license = await reader.ReadToEndAsync(cancellationToken);
        }

        if (license is null)
        {
            var catalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(cancellationToken);
            license = catalogInfo.Catalog.Properties?.GetStringValue(DataModelExtensions.LicenseKey);
        }

        return license;
    }

    public bool HasAccepted(ClaimsPrincipal user, string catalogId, string? license)
    {
        var userId = user.FindFirst(NexusClaimTypes.Subject)?.Value;

        return userId is not null && HasAccepted(userId, catalogId, license);
    }

    internal bool HasAccepted(string userId, string catalogId, string? license)
    {
        return !string.IsNullOrEmpty(license) && File.Exists(GetFilePath(userId, catalogId, license));
    }

    private string GetFilePath(string userId, string catalogId, string license)
    {
        var folderPath = Path.Combine(
            _pathsOptions.Config,
            USERS,
            Hash(userId),
            ACCEPTED_LICENSES,
            Hash(catalogId)
        );

        var fileName = Hash(license) + FILE_EXTENSION;

        return SafePathCombine(folderPath, fileName);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SafePathCombine(string basePath, string relativePath)
    {
        var filePath = Path.GetFullPath(Path.Combine(basePath, relativePath));
        var normalizedBasePath = Path.GetFullPath(basePath);

        if (!filePath.StartsWith(normalizedBasePath, StringComparison.Ordinal))
            throw new Exception("Invalid path.");

        return filePath;
    }
}
