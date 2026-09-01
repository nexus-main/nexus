using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.UI.Core;

public record UISettings(
    string? FileType = default,
    JsonElement? RequestConfiguration = default,
    List<string?>? CatalogHidePatterns = default,
    int ChartGpuCacheBudgetMiB = 512
)
{
    [JsonIgnore]
    public int EffectiveChartGpuCacheBudgetMiB => ChartGpuCacheBudgetMiB <= 0
        ? 512
        : Math.Max(16, ChartGpuCacheBudgetMiB);
}
