using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

// keep in sync with DataModelUtilities
public record ResourcePathParseResult(
    string CatalogId,
    string ResourceId,
    TimeSpan SamplePeriod,
    RepresentationKind Kind,
    string? Parameters,
    TimeSpan? BasePeriod
);

public static partial class Utilities
{
    public static string ToSpaceFilledCatalogId(string catalogId)
        => catalogId.TrimStart('/').Replace("/", " / ");

    public static string EscapeDataString(string catalogId)
        => Uri.EscapeDataString(catalogId);

    // keep in sync with DataModelExtensions ...
    private const int NS_PER_TICK = 100;
    private static readonly long[] _nanoseconds = [(long)1e0, (long)1e3, (long)1e6, (long)1e9, (long)60e9, (long)3600e9, (long)86400e9];
    private static readonly int[] _quotients = [1000, 1000, 1000, 60, 60, 24, 1];
    private static readonly string[] _postFixes = ["ns", "us", "ms", "s", "min", "h", "d"];
    // ... except this line
    private static readonly Regex _unitStringEvaluator = UnitStringEvaluator();

    public static string ToUnitString(this TimeSpan samplePeriod, bool withUnderScore = false)
    {
        var fillValue = withUnderScore
            ? "_"
            : " ";

        var currentValue = samplePeriod.Ticks * NS_PER_TICK;

        for (int i = 0; i < _postFixes.Length; i++)
        {
            var quotient = Math.DivRem(currentValue, _quotients[i], out var remainder);

            if (remainder != 0)
                return $"{currentValue}{fillValue}{_postFixes[i]}";

            else
                currentValue = quotient;
        }

        return $"{(int)currentValue}{fillValue}{_postFixes.Last()}";
    }

    public static TimeSpan ToPeriod(string unitString)
    {
        var match = _unitStringEvaluator.Match(unitString);

        if (!match.Success)
            throw new Exception("The provided unit string is invalid.");

        var unitIndex = Array.IndexOf(_postFixes, match.Groups[2].Value.ToLowerInvariant());

        if (unitIndex == -1)
            throw new Exception("The provided unit is invalid.");

        var totalNanoSeconds = long.Parse(match.Groups[1].Value) * _nanoseconds[unitIndex];

        if (totalNanoSeconds % NS_PER_TICK != 0)
            throw new Exception("The sample period must be a multiple of 100 ns.");

        var ticks = totalNanoSeconds / NS_PER_TICK;

        return new TimeSpan(ticks);
    }

    public static long GetElementCount(DateTime begin, DateTime end, TimeSpan samplePeriod)
    {
        return (long)((end - begin).Ticks / samplePeriod.Ticks);
    }

    public static long GetByteCount(long elementCount, IEnumerable<CatalogItemSelectionViewModel> selectedatalogItems)
    {
        var elementSize = 8;

        var representationCount = selectedatalogItems
            .Sum(item => item.Kinds.Count);

        return elementCount * elementSize * representationCount;
    }

    // keep in sync with DataModelUtilities
    private static readonly Regex _resourcePathEvaluator = new(@"^(?'catalog'.*)\/(?'resource'.*)\/(?'sample_period'[0-9]+_[a-zA-Z]+)(?:_(?'kind'[^\(#\s]+))?(?:\((?'parameters'.*)\))?(?:#(?'fragment'.*))?$", RegexOptions.Compiled);

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
            kind = Utilities.StringToKind(rawValue);
        }

        // basePeriod
        TimeSpan? basePeriod = default;

        if (match.Groups["fragment"].Success)
        {
            var unitString = match.Groups["fragment"].Value.Split('=', count: 2)[1];
            basePeriod = Utilities.ToPeriod(unitString);
        }

        // result
        parseResult = new ResourcePathParseResult(
            CatalogId: match.Groups["catalog"].Value,
            ResourceId: match.Groups["resource"].Value,
            SamplePeriod: Utilities.ToPeriod(match.Groups["sample_period"].Value),
            Kind: kind,
            Parameters: match.Groups["parameters"].Success ? match.Groups["parameters"].Value : default,
            BasePeriod: basePeriod
        );

        return true;
    }

    public static void ParseResourcePath(
        string resourcePath,
        out ResourcePathParseResult parseResult,
        out IReadOnlyDictionary<string, string>? parameters)
    {
        if (!TryParseResourcePath(resourcePath, out parseResult!))
            throw new Exception("The resource path is malformed.");

        /* parameters (same logic as in ResourceCatalog.TryFind) */
        parameters = default;

        if (parseResult.Parameters is not null)
        {
            var _matchSingleParametersExpression = new Regex(@"\s*(.+?)\s*=\s*([^,\)]+)\s*,?");

            var matches = _matchSingleParametersExpression
                .Matches(parseResult.Parameters);

            if (matches.Count != 0)
            {
                parameters = new ReadOnlyDictionary<string, string>(matches
                    .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
                    .ToDictionary(tuple => tuple.Item1, tuple => tuple.Item2));
            }
        }
    }

    private static readonly Regex _snakeCaseEvaluator = new("(?<=[a-z])([A-Z])", RegexOptions.Compiled);

    public static string? KindToString(RepresentationKind kind)
    {
        var snakeCaseKind = kind == RepresentationKind.Original
            ? null
            : _snakeCaseEvaluator.Replace(kind.ToString(), "_$1").Trim().ToLower();

        return snakeCaseKind;
    }

    public static RepresentationKind StringToKind(string rawKind)
    {
        var camelCase = Regex.Replace(rawKind, "_.", match => match.Value[1..].ToUpper());
        var pascalCase = string.Concat(camelCase[0].ToString().ToUpper(), camelCase.AsSpan(1));
        var kind = Enum.Parse<RepresentationKind>(pascalCase);

        return kind;
    }

    public static bool TryGetIntegerValue(
        this JsonElement properties, string propertyName, [NotNullWhen(returnValue: true)] out int? value)
    {
        value = default;

        if (properties.ValueKind == JsonValueKind.Object &&
            properties.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Number)
            value = propertyValue.GetInt32();

        return value is not null;
    }

    public static bool TryGetStringValue(
        this JsonElement properties, string propertyName, [NotNullWhen(returnValue: true)] out string? value)
    {
        value = properties.GetStringValue(propertyName);
        return value is not null;
    }

    public static string? GetStringValue(this IReadOnlyDictionary<string, JsonElement>? properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();

        if (properties is not null &&
            properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.String || element.ValueKind == JsonValueKind.Null)
                    return element.GetString();
            }

            else
            {
                var newPropertyPath = string.Join('/', pathSegments.ToArray());
                return element.GetStringValue(newPropertyPath);
            }
        }

        return default;
    }

    public static string? GetStringValue(this JsonElement properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);

        var propertyName = pathSegments.Length == 0
            ? propertyPath
            : pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var propertyValue) &&
            (propertyValue.ValueKind == JsonValueKind.String || propertyValue.ValueKind == JsonValueKind.Null))
            return propertyValue.GetString();

        return default;
    }

    public static string?[]? GetStringArray(this IReadOnlyDictionary<string, JsonElement>? properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();

        if (properties is not null &&
            properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.Array)
                    return element
                        .EnumerateArray()
                        .Where(current => current.ValueKind == JsonValueKind.String || current.ValueKind == JsonValueKind.Null)
                        .Select(current => current.GetString())
                        .ToArray();
            }

            else
            {
                var newPropertyPath = string.Join('/', pathSegments.ToArray());
                return element.GetStringArray(newPropertyPath);
            }
        }

        return default;
    }

    public static string?[]? GetStringArray(this JsonElement properties, string propertyPath)
    {
        var pathSegments = propertyPath.Split('/').AsSpan();
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);

        var propertyName = pathSegments.Length == 0
            ? propertyPath
            : pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var propertyValue) &&
                propertyValue.ValueKind == JsonValueKind.Array)
            return propertyValue
                .EnumerateArray()
                .Where(current => current.ValueKind == JsonValueKind.String || current.ValueKind == JsonValueKind.Null)
                .Select(current => current.GetString())
                .ToArray();

        return default;
    }

    private static JsonElement GetJsonObjectFromPath(this JsonElement root, Span<string> pathSegements)
    {
        if (pathSegements.Length == 0)
            return root;

        var current = root;

        foreach (var pathSegement in pathSegements)
        {
            if (current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(pathSegement, out current))
            {
                // do nothing   
            }
            else
            {
                return default;
            }
        }

        return current;
    }

    public static Dictionary<string, string>? GetStringDictionary(this JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Object)
            return propertyValue
                .EnumerateObject()
                .Where(current => current.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(current => current.Name, current => current.Value.GetString()!);

        return default;
    }

    [GeneratedRegex(@"^\s*([0-9]+)[\s_]*([a-zA-Z]+)\s*$", RegexOptions.Compiled)]
    private static partial Regex UnitStringEvaluator();
}