// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Sources;
using System.Text;

namespace Nexus.Services;

internal class DevelopmentSampleLicenseSeeder(IDatabaseService databaseService)
{
    internal const string LicenseAttachmentId = "LICENSE.md";

    internal const string LicenseText =
"""
# Development Sample License

This is a development-only sample license for testing the Nexus license acceptance flow.

Accepting this license is required before accessing this catalog.
""";

    private readonly IDatabaseService _databaseService = databaseService;

    public void Seed()
    {
        if (_databaseService.AttachmentExists(Sample.LicensedCatalogId, LicenseAttachmentId))
            return;

        using var stream = _databaseService.WriteAttachment(Sample.LicensedCatalogId, LicenseAttachmentId);
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        writer.Write(LicenseText);
    }
}
