// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class FakeResourceCatalogViewModel : ResourceCatalogViewModel
{
    public FakeResourceCatalogViewModel(CatalogInfo info, string parentId, INexusClient client, AppState appState, Task<IReadOnlyList<CatalogInfo>> childCatalogInfosTask)
        : base(info, parentId, appState)
    {
        var id = Id;

        async Task<List<ResourceCatalogViewModel>> func()
        {
            var childCatalogInfo = await childCatalogInfosTask;
            return PrepareChildCatalogs(childCatalogInfo, id, client, appState);
        }

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => Task.FromResult(new ResourceCatalog(id, default, default)));
        LicenseTask = new Lazy<Task<string?>>(() => Task.FromResult(default(string?)));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => Task.FromResult(new CatalogTimeRange(default, default)));
    }
}
