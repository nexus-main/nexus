using Nexus.Api;
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

    private static List<ResourceCatalogViewModel> PrepareChildCatalogs(
        IReadOnlyList<CatalogInfo> childCatalogInfos,
        string id,
        INexusClient client,
        AppState appState)
    {
        /* This methods creates intermediate fake catalogs (marked with a *) 
         * to group child catalogs. Example:
         *
         *   /A/A/A
         *   /A/A/B
         *   /A/B
         *   -> /* + /A* (/A/A/A, /A/A/B, /A/B) + /A/A* (/A/A/A, /A/A/B) + /A/A/A, /A/A/B, /A/B
         */

        id = id == "/" ? "" : id;

        var result = new List<ResourceCatalogViewModel>();

        var groupedPublishedInfos = childCatalogInfos
            .Where(info => (info.IsReleased && info.IsVisible) || info.IsOwner)
            .GroupBy(childInfo => childInfo.Id[id.Length..].Split('/', count: 3)[1]);

        foreach (var group in groupedPublishedInfos)
        {
            if (!group.Any())
            {
                // do nothing
            }

            else if (group.Count() == 1)
            {
                var childInfo = group.First();
                result.Add(new RealResourceCatalogViewModel(childInfo, id, client, appState));
            }

            else
            {
                var childId = id + "/" + group.Key;

                var childInfo = new CatalogInfo(
                    Id: childId,
                    Title: default!,
                    Contact: default,
                    Readme: default,
                    License: default,
                    IsReadable: true,
                    IsWritable: false,
                    IsReleased: true,
                    IsVisible: true,
                    IsOwner: false,
                    DataSourceInfoUrl: default,
                    DataSourceType: "Nexus.FakeSource",
                    DataSourceRegistrationId: default,
                    PackageReferenceId: default);

                var childCatalogInfosTask = Task.FromResult((IReadOnlyList<CatalogInfo>)group.ToList());
                result.Add(new FakeResourceCatalogViewModel(childInfo, id, client, appState, childCatalogInfosTask));
            }
        }

        result = result
            .OrderBy(catalog => catalog.Id)
            .ToList();

        return result;
    }
}
