// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;

namespace Nexus.DataModel;

// TODO: Remove as soon as there is framework level support (may take a while)
/// <summary>
/// A static class with extensions for <see cref="JsonElement"/>.
/// </summary>
public static class PropertiesExtensions
{
    /// <summary>
    /// Reads the value of the specified property as string if it exists.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="pathSegments">The propery path segments.</param>
    /// <returns></returns>
    public static string? GetStringValue(this IReadOnlyDictionary<string, JsonElement> properties, Span<string> pathSegments)
    {
        // TODO Maybe use params collection (Span) in future? https://github.com/dotnet/csharplang/issues/7700

        if (properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.String || element.ValueKind == JsonValueKind.Null)
                    return element.GetString();
            }

            else
            {
                return element.GetStringValue(pathSegments);
            }
        }

        return default;
    }

    /// <summary>
    /// Reads the value of the specified property as string if it exists.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="pathSegments">The propery path segments.</param>
    /// <returns></returns>
    public static string? GetStringValue(this JsonElement properties, Span<string> pathSegments)
    {
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);
        var propertyName = pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var propertyValue) &&
            (propertyValue.ValueKind == JsonValueKind.String || propertyValue.ValueKind == JsonValueKind.Null)
        )
        {
            return propertyValue.GetString();
        }

        return default;
    }

    /// <summary>
    /// Reads the value of the specified property as string array if it exists.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="pathSegments">The property path segments.</param>
    /// <returns></returns>
    public static string?[]? GetStringArray(this IReadOnlyDictionary<string, JsonElement> properties, Span<string> pathSegments)
    {
        if (properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    return element
                        .EnumerateArray()
                        .Where(current => current.ValueKind == JsonValueKind.String || current.ValueKind == JsonValueKind.Null)
                        .Select(current => current.GetString())
                        .ToArray();
                }
            }

            else
            {
                return element.GetStringArray(pathSegments);
            }
        }

        return default;
    }

    /// <summary>
    /// Reads the value of the specified property as string array if it exists.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="pathSegments">The property path segments.</param>
    /// <returns></returns>
    public static string?[]? GetStringArray(this JsonElement properties, Span<string> pathSegments)
    {
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);
        var propertyName = pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Array
        )
        {
            return propertyValue
                .EnumerateArray()
                .Where(current => current.ValueKind == JsonValueKind.String || current.ValueKind == JsonValueKind.Null)
                .Select(current => current.GetString())
                .ToArray();
        }

        return default;
    }

    internal static int? GetIntValue(this IReadOnlyDictionary<string, JsonElement> properties, Span<string> pathSegments)
    {
        if (properties.TryGetValue(pathSegments[0], out var element))
        {
            pathSegments = pathSegments[1..];

            if (pathSegments.Length == 0)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetInt32();
            }

            else
            {
                return element.GetIntValue(pathSegments);
            }
        }

        return default;
    }

    internal static int? GetIntValue(this JsonElement properties, Span<string> pathSegments)
    {
        var root = properties.GetJsonObjectFromPath(pathSegments[0..^1]);
        var propertyName = pathSegments[^1];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.Number
        )
        {
            return propertyValue.GetInt32();
        }

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
}