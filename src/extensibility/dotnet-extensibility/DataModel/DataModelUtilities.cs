using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nexus.DataModel;

internal static partial class DataModelUtilities
{
    /* Example resource paths:
     * 
     * /a/b/c/T1/10_ms
     * /a/b/c/T1/10_ms(abc=456)
     * /a/b/c/T1/10_ms(abc=456)#base=1s
     * /a/b/c/T1/600_s_mean
     * /a/b/c/T1/600_s_mean(abc=456)
     * /a/b/c/T1/600_s_mean#base=1s
     * /a/b/c/T1/600_s_mean(abc=456)#base=1s
     */
    // keep in sync with Nexus.UI.Core.Utilities
    private static readonly Regex _resourcePathEvaluator = ResourcePathEvaluator();

    private static string ToPascalCase(string input)
    {
        var camelCase = Regex.Replace(input, "_.", match => match.Value[1..].ToUpper());
        var pascalCase = string.Concat(camelCase[0].ToString().ToUpper(), camelCase.AsSpan(1));

        return pascalCase;
    }

    // keep in sync with Nexus.UI.Utilities
    public static bool TryParseResourcePath(
        string resourcePath,
        [NotNullWhen(returnValue: true)] out ResourcePathParseResult? parseResult)
    {
        parseResult = default;

        // match
        var match = _resourcePathEvaluator.Match(resourcePath);

        if (!match.Success)
            return false;

        // kind
        var kind = RepresentationKind.Original;

        if (match.Groups["kind"].Success)
        {
            var rawValue = match.Groups["kind"].Value;

            if (!Enum.TryParse(ToPascalCase(rawValue), out kind))
                return default;
        }

        // basePeriod
        TimeSpan? basePeriod = default;

        if (match.Groups["fragment"].Success)
        {
            var unitString = match.Groups["fragment"].Value.Split('=', count: 2)[1];
            basePeriod = DataModelExtensions.ToSamplePeriod(unitString);
        }

        // result
        parseResult = new ResourcePathParseResult(
            CatalogId: match.Groups["catalog"].Value,
            ResourceId: match.Groups["resource"].Value,
            SamplePeriod: DataModelExtensions.ToSamplePeriod(match.Groups["sample_period"].Value),
            Kind: kind,
            Parameters: match.Groups["parameters"].Success ? match.Groups["parameters"].Value : default,
            BasePeriod: basePeriod
        );

        return true;
    }

    public static string? GetRepresentationParameterString(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null)
            return default;

        var serializedParameters = parameters
            .Select(parameter => $"{parameter.Key}={parameter.Value}");

        var parametersString = $"({string.Join(',', serializedParameters)})";

        return parametersString;
    }

    public static List<Resource>? MergeResources(IReadOnlyList<Resource>? resources1, IReadOnlyList<Resource>? resources2)
    {
        if (resources1 is null && resources2 is null)
            return null;

        if (resources1 is null)
            return resources2!
                .Select(resource => resource.DeepCopy())
                .ToList();

        if (resources2 is null)
            return resources1!
                .Select(resource => resource.DeepCopy())
                .ToList();

        var mergedResources = resources1
            .Select(resource => resource.DeepCopy())
            .ToList();

        foreach (var newResource in resources2)
        {
            var index = mergedResources.FindIndex(current => current.Id == newResource.Id);

            if (index >= 0)
            {
                mergedResources[index] = mergedResources[index].Merge(newResource);
            }

            else
            {
                mergedResources.Add(newResource.DeepCopy());
            }
        }

        return mergedResources;
    }

    public static List<Representation>? MergeRepresentations(IReadOnlyList<Representation>? representations1, IReadOnlyList<Representation>? representations2)
    {
        if (representations1 is null && representations2 is null)
            return null;

        if (representations1 is null)
            return representations2!
                .Select(representation => representation.DeepCopy())
                .ToList();

        if (representations2 is null)
            return representations1!
                .Select(representation => representation.DeepCopy())
                .ToList();

        var mergedRepresentations = representations1
            .Select(representation => representation.DeepCopy())
            .ToList();

        foreach (var newRepresentation in representations2)
        {
            var index = mergedRepresentations.FindIndex(current => current.Id == newRepresentation.Id);

            if (index >= 0)
            {
                if (!newRepresentation.Equals(mergedRepresentations[index]))
                    throw new Exception("The representations to be merged are not equal.");

            }

            else
            {
                mergedRepresentations.Add(newRepresentation);
            }
        }

        return mergedRepresentations;
    }

    public static IReadOnlyDictionary<string, JsonElement>? MergeProperties(IReadOnlyDictionary<string, JsonElement>? properties1, IReadOnlyDictionary<string, JsonElement>? properties2)
    {
        if (properties1 is null)
            return properties2;

        if (properties2 is null)
            return properties1;

        var mergedProperties = properties1.ToDictionary(entry => entry.Key, entry => entry.Value);

        foreach (var entry in properties2)
        {
            if (mergedProperties.ContainsKey(entry.Key))
                mergedProperties[entry.Key] = MergeProperties(properties1[entry.Key], entry.Value);

            else
                mergedProperties[entry.Key] = entry.Value;
        }

        return mergedProperties;
    }

    public static JsonElement MergeProperties(JsonElement properties1, JsonElement properties2)
    {
        var properties1IsNotOK = properties1.ValueKind == JsonValueKind.Null;
        var properties2IsNotOK = properties2.ValueKind == JsonValueKind.Null;

        if (properties1IsNotOK)
            return properties2;

        if (properties2IsNotOK)
            return properties1;

        JsonNode mergedProperties;

        if (properties1.ValueKind == JsonValueKind.Object && properties2.ValueKind == JsonValueKind.Object)
        {
            mergedProperties = new JsonObject();
            MergeObjects((JsonObject)mergedProperties, properties1, properties2);
        }

        else if (properties1.ValueKind == JsonValueKind.Array && properties2.ValueKind == JsonValueKind.Array)
        {
            mergedProperties = new JsonArray();
            MergeArrays((JsonArray)mergedProperties, properties1, properties2);
        }

        else
        {
            return properties2;
        }

        return JsonSerializer.SerializeToElement(mergedProperties);
    }

    private static void MergeObjects(JsonObject currentObject, JsonElement root1, JsonElement root2)
    {
        foreach (var property in root1.EnumerateObject())
        {
            if (root2.TryGetProperty(property.Name, out var newValue) && newValue.ValueKind != JsonValueKind.Null)
            {
                var originalValue = property.Value;
                var originalValueKind = originalValue.ValueKind;

                if (newValue.ValueKind == JsonValueKind.Object && originalValueKind == JsonValueKind.Object)
                {
                    var newObject = new JsonObject();
                    currentObject[property.Name] = newObject;

                    MergeObjects(newObject, originalValue, newValue);
                }

                else if (newValue.ValueKind == JsonValueKind.Array && originalValueKind == JsonValueKind.Array)
                {
                    var newArray = new JsonArray();
                    currentObject[property.Name] = newArray;

                    MergeArrays(newArray, originalValue, newValue);
                }

                else
                {
                    currentObject[property.Name] = ToJsonNode(newValue);
                }
            }

            else
            {
                currentObject[property.Name] = ToJsonNode(property.Value);
            }
        }

        foreach (var property in root2.EnumerateObject())
        {
            if (!root1.TryGetProperty(property.Name, out _))
                currentObject[property.Name] = ToJsonNode(property.Value);
        }
    }

    private static void MergeArrays(JsonArray currentArray, JsonElement root1, JsonElement root2)
    {
        foreach (var element in root1.EnumerateArray())
        {
            currentArray.Add(element);
        }

        foreach (var element in root2.EnumerateArray())
        {
            currentArray.Add(element);
        }
    }

    public static JsonNode? ToJsonNode(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => JsonObject.Create(element),
            JsonValueKind.Array => JsonArray.Create(element),
            _ => JsonValue.Create(element)
        };
    }

    [GeneratedRegex(@"^(?'catalog'.*)\/(?'resource'.*)\/(?'sample_period'[0-9]+_[a-zA-Z]+)(?:_(?'kind'[^\(#\s]+))?(?:\((?'parameters'.*)\))?(?:#(?'fragment'.*))?$", RegexOptions.Compiled)]
    private static partial Regex ResourcePathEvaluator();
}