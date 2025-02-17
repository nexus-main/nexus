// MIT License
// Copyright (c) [2024] [nexus-main]

using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public abstract class ResourceCatalogViewModel
{
    public const string ROOT_CATALOG_ID = "/";

    private readonly AppState _appState;

    public ResourceCatalogViewModel(CatalogInfo info, string parentId, AppState appState)
    {
        Info = info;
        Id = info.Id;
        DisplayName = Utilities.ToSpaceFilledCatalogId(Id[parentId.Length..]);

        _appState = appState;
    }

    public string Id { get; }

    public CatalogInfo Info { get; }

    public string DisplayName { get; }

    public bool IsSelected { get; private set; }

    public bool IsOpen { get; set; }

    public ResourceCatalog? Catalog { get; private set; }

    public List<ResourceCatalogViewModel>? Children { get; private set; }

    protected Lazy<Task<List<ResourceCatalogViewModel>>> ChildrenTask { get; set; } = default!;

    public Lazy<Task<ResourceCatalog>> CatalogTask { get; protected set; } = default!;

    public Lazy<Task<string?>> LicenseTask { get; protected set; } = default!;

    public Lazy<Task<CatalogTimeRange>> TimeRangeTask { get; protected set; } = default!;

    public async Task SelectCatalogAsync(string catalogId)
    {
        IsSelected = catalogId == Id;

        var isOpen = false;

        if (IsSelected)
        {
            if (Info.IsReadable)
                Catalog = await CatalogTask.Value;

            Children = await ChildrenTask.Value;
            isOpen = !IsOpen;
            _appState.SelectedCatalog = this;
        }

        if (Children is null && catalogId.StartsWith(Id))
            Children = await ChildrenTask.Value;

        if (Children is not null)
        {
            foreach (var child in Children)
            {
                await child.SelectCatalogAsync(catalogId);
                isOpen |= child.IsOpen;
            }
        }

        IsOpen = isOpen;
    }

    public static List<ResourceCatalogViewModel> PrepareChildCatalogs(
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
         *   -> /* + /A* (/A/A, /A/B) + /A/A* (/A/A/A, /A/A/B)
         */

        id = id == "/"
            ? ""
            : id;

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
                    PackageReferenceIds: default!,
                    PipelineInfo: default!);

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