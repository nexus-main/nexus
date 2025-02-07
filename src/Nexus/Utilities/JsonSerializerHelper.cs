// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Text.Json;

namespace Nexus.Utilities;

internal static class JsonSerializerHelper
{
    private static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true
    };

    public static string SerializeIndented<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static void SerializeIndented<T>(Stream utf8Json, T value)
    {
        JsonSerializer.Serialize(utf8Json, value, Options);
    }

    public static Task SerializeIndentedAsync<T>(Stream utf8Json, T value)
    {
        return JsonSerializer.SerializeAsync(utf8Json, value, Options);
    }
}
