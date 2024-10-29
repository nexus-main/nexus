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
}