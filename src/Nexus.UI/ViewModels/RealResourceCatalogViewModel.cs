using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class RealResourceCatalogViewModel : ResourceCatalogViewModel
{
    private readonly INexusClient _client;

    public RealResourceCatalogViewModel(CatalogInfo info, string parentId, INexusClient client, AppState appState)
        : base(info, parentId, appState)
    {
        _client = client;

        async Task<List<ResourceCatalogViewModel>> func()
        {
            var childCatalogInfos = await client.Catalogs.GetChildCatalogInfosAsync(Id, CancellationToken.None);

            return childCatalogInfos
                .Where(childInfo => (childInfo.IsReleased && childInfo.IsVisible) || childInfo.IsOwner)
                .Select(childInfo => (ResourceCatalogViewModel)new RealResourceCatalogViewModel(childInfo, Id, client, appState))
                .ToList();
        }

        ChildrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => client.Catalogs.GetAsync(Id, CancellationToken.None));
        LicenseTask = new Lazy<Task<string?>>(() => client.Catalogs.GetLicenseAsync(Id, CancellationToken.None));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => client.Catalogs.GetTimeRangeAsync(Id, CancellationToken.None));
    }

    public void ResetCatalogTask()
    {
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => _client.Catalogs.GetAsync(Id, CancellationToken.None));
    }
}