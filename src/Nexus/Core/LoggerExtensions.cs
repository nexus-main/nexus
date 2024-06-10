// MIT License
// Copyright (c) [2024] [nexus-main]

namespace Nexus.Core;

internal static class LoggerExtensions
{
    public static IDisposable
        BeginNamedScope<T>(this ILogger<T> logger, string name, params ValueTuple<string, object>[] stateProperties)
    {
        var dictionary = stateProperties.ToDictionary(entry => entry.Item1, entry => entry.Item2);
        dictionary[name + "_scope"] = Guid.NewGuid();
        return logger.BeginScope(dictionary) ?? throw new Exception("The scope is null.");
    }

    public static IDisposable
        BeginNamedScope<T>(this ILogger<T> logger, string name, IDictionary<string, object> stateProperties)
    {
        var dictionary = stateProperties;
        dictionary[name + "_scope"] = Guid.NewGuid();
        return logger.BeginScope(dictionary) ?? throw new Exception("The scope is null.");
    }
}
