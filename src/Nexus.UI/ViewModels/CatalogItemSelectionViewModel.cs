using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class CatalogItemSelectionViewModel(
    CatalogItemViewModel baseItem,
    IDictionary<string, string>? parameters)
{
    public CatalogItemViewModel BaseItem { get; } = baseItem;
    public IDictionary<string, string>? Parameters { get; } = parameters;
    public List<RepresentationKind> Kinds { get; } = [];

    public string GetResourcePath(RepresentationKind kind, TimeSpan samplePeriod)
    {
        var baseItem = BaseItem;
        var samplePeriodString = Utilities.ToUnitString(samplePeriod, withUnderScore: true);
        var baseSamplePeriodString = Utilities.ToUnitString(baseItem.Representation.SamplePeriod, withUnderScore: true);
        var snakeCaseKind = Utilities.KindToString(kind);

        var representationId = snakeCaseKind is not null
            ? $"{samplePeriodString}_{snakeCaseKind}"
            : $"{samplePeriodString}";

        var serializedParameters = GetRepresentationParameterString(Parameters);
        var resourcePath = $"{baseItem.Catalog.Id}/{baseItem.Resource.Id}/{representationId}{serializedParameters}#base={baseSamplePeriodString}";

        return resourcePath;
    }

    public string AugmentedResourceId
    {
        get
        {
            return BaseItem.Resource.Id + GetRepresentationParameterString(Parameters);
        }
    }

    private static string? GetRepresentationParameterString(IDictionary<string, string>? parameters)
    {
        if (parameters is null || !parameters.Any())
            return default;

        var serializedParameters = parameters.Select(parameter => $"{parameter.Key}={parameter.Value}");
        var parametersString = $"({string.Join(',', serializedParameters)})";

        return parametersString;
    }

    public bool IsValid(Period samplePeriod)
    {
        return Kinds.All(kind => IsValid(kind, samplePeriod));
    }

    public bool IsValid(RepresentationKind kind, Period samplePeriod)
    {
        var baseSamplePeriod = BaseItem.Representation.SamplePeriod;

        return kind switch
        {
            RepresentationKind.Resampled =>
                samplePeriod.Value < baseSamplePeriod &&
                baseSamplePeriod.Ticks % samplePeriod.Value.Ticks == 0,

            RepresentationKind.Original =>
                samplePeriod.Value == baseSamplePeriod,

            _ =>
                baseSamplePeriod < samplePeriod.Value &&
                samplePeriod.Value.Ticks % baseSamplePeriod.Ticks == 0
        };
    }
}