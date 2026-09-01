using System.Text.Json;

namespace Nexus.UI.Core;

public record UISettings(
    string? FileType = default,
    JsonElement? RequestConfiguration = default,
    List<string?>? CatalogHidePatterns = default,
    int ChartGpuCacheBudgetMiB = 512
);
