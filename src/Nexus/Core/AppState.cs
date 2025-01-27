// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Core.V1;
using Nexus.DataModel;
using System.Collections.Concurrent;
using System.Reflection;

namespace Nexus.Core;

internal class AppState
{
    public AppState()
    {
        var entryAssembly = Assembly.GetEntryAssembly()!;
        var version = entryAssembly.GetName().Version!;

        Version = version.ToString();
    }

    public ConcurrentDictionary<CatalogContainer, Task<Resource[]>> ResourceCache { get; }
        = new ConcurrentDictionary<CatalogContainer, Task<Resource[]>>();

    public string Version { get; }

    public Task? ReloadPackagesTask { get; set; }

    public CatalogState CatalogState { get; set; } = default!;

    public List<ExtensionDescription> DataWriterDescriptions { get; set; } = default!;
}
