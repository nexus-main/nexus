// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using MudBlazor;
using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Services;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState
{
    ViewState ViewState { get; set; }

    ExportParameters ExportParameters { get; set; }

    SettingsViewModel Settings { get; }

    ResourceCatalogViewModel RootCatalog { get; }

    ResourceCatalogViewModel? SelectedCatalog { get; set; }

    SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; }

    List<CatalogItemViewModel>? CatalogItemsGroup { get; set; }

    IReadOnlyList<(DateTime, Exception)> Errors { get; }

    IReadOnlyDictionary<string, Dictionary<EditModeItem, string?>> EditModeCatalogMap { get; }

    bool IsHamburgerMenuOpen { get; set; }

    bool IsDemo { get; }

    bool HasUnreadErrors { get; set; }

    bool BeginAtZero { get; set; }

    string? SearchString { get; set; }

    ObservableCollection<JobViewModel> Jobs { get; set; }

    event PropertyChangedEventHandler? PropertyChanged;

    void AddEditModeCatalog(string catalogId);

    void AddError(Exception error, ISnackbar? snackbar);

    void AddJob(JobViewModel job);

    void CancelJob(JobViewModel job);

    void ClearRequestConfiguration();

    Task SaveAndRemoveEditModeCatalogAsync(string catalogId, ISnackbar snackbar);

    Task SelectCatalogAsync(string? catalogId);

    void SetRequestConfiguration(JsonElement configuration);
}

public class AppState : INotifyPropertyChanged, IAppState
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private ResourceCatalogViewModel? _selectedCatalog;

    private ViewState _viewState = ViewState.Normal;

    private ExportParameters _exportParameters = default!;

    private readonly INexusClient _client;

    private readonly List<(DateTime, Exception)> _errors = [];

    private readonly Dictionary<string, Dictionary<EditModeItem, string?>> _editModeCatalogMap = [];

    private bool _beginAtZero;

    private UISettings? _uiSettings;

    private string? _searchString;

    private const string GROUP_KEY = "groups";

    private readonly NexusJSInterop _jsInterop;

    private IDisposable? _requestConfiguration;

    public AppState(
        bool isDemo,
        INexusClient client,
        NexusJSInterop jsInterop
    )
    {
        IsDemo = isDemo;
        _client = client;
        _jsInterop = jsInterop;
        Settings = new SettingsViewModel(this, jsInterop, client);

        var childCatalogInfosTask = client.V1.Catalogs.GetChildCatalogInfosAsync(ResourceCatalogViewModel.ROOT_CATALOG_ID, CancellationToken.None);

        var rootInfo = new CatalogInfo(
            Id: ResourceCatalogViewModel.ROOT_CATALOG_ID,
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

        RootCatalog = new FakeResourceCatalogViewModel(
            rootInfo,
            "",
            UISettings.CatalogHidePatterns,
            client,
            this,
            childCatalogInfosTask
        );

        // export parameters
        ExportParameters = new ExportParameters(
            Begin: DateTime.UtcNow.Date.AddDays(-2),
            End: DateTime.UtcNow.Date.AddDays(-1),
            FilePeriod: default,
            Type: default,
            ResourcePaths: new List<string>(),
            Configuration: default
        );

        // request configuration
        var configuration = UISettings.RequestConfiguration;

        if (configuration is not null)
            _requestConfiguration = _client.AttachConfiguration(configuration);

        // demo
        if (isDemo)
            BeginAtZero = true;
    }

    public bool IsHamburgerMenuOpen { get; set; }

    public bool IsDemo { get; }

    public UISettings UISettings
    {
        get
        {
            _uiSettings ??= _jsInterop.LoadSetting<UISettings>("ui-settings")
                ?? new UISettings();

            return _uiSettings;
        }
        set
        {
            _jsInterop.SaveSetting("ui-settings", value);
            _uiSettings = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UISettings)));
        }
    }


    public ViewState ViewState
    {
        get
        {
            return _viewState;
        }
        set
        {
            if (_viewState != value)
            {
                _viewState = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewState)));
            }
        }
    }

    public ExportParameters ExportParameters
    {
        get
        {
            return _exportParameters;
        }
        set
        {
            if (_exportParameters != value)
            {
                _exportParameters = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExportParameters)));
            }
        }
    }

    public SettingsViewModel Settings { get; }

    public ResourceCatalogViewModel RootCatalog { get; }

    public ResourceCatalogViewModel? SelectedCatalog
    {
        get
        {
            return _selectedCatalog;
        }
        set
        {
            if (_selectedCatalog != value)
            {
                _selectedCatalog = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalog)));
            }
        }
    }

    public SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; private set; }

    public List<CatalogItemViewModel>? CatalogItemsGroup { get; set; }

    public IReadOnlyList<(DateTime, Exception)> Errors => _errors;

    public IReadOnlyDictionary<string, Dictionary<EditModeItem, string?>> EditModeCatalogMap => _editModeCatalogMap;

    public bool HasUnreadErrors { get; set; }

    public bool BeginAtZero
    {
        get
        {
            return _beginAtZero;
        }
        set
        {
            if (value != _beginAtZero)
            {
                _beginAtZero = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BeginAtZero)));
            }
        }
    }

    public string? SearchString
    {
        get
        {
            return _searchString;
        }
        set
        {
            if (value != _searchString)
            {
                _searchString = value;

                CatalogItemsMap = GroupCatalogItems(SelectedCatalog!.Catalog!);
                CatalogItemsGroup = CatalogItemsMap?.Values.FirstOrDefault();

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchString)));
            }
        }
    }

    public ObservableCollection<JobViewModel> Jobs { get; set; } = [];

    public void AddJob(JobViewModel job)
    {
        if (Jobs.Count >= 20)
            Jobs.RemoveAt(0);

        Jobs.Add(job);
    }

    public void CancelJob(JobViewModel job)
    {
        if (Jobs.Count >= 20)
            Jobs.RemoveAt(0);

        if (job.Status is null || job.Status.Status < Api.V1.TaskStatus.RanToCompletion)
            _ = _client.V1.Jobs.CancelJobAsync(job.Id);
    }

    public async Task SelectCatalogAsync(string? catalogId)
    {
        _searchString = default;

        catalogId ??= ResourceCatalogViewModel.ROOT_CATALOG_ID;

        await RootCatalog.SelectCatalogAsync(catalogId);

        if (SelectedCatalog is null || SelectedCatalog.Catalog is null)
        {
            CatalogItemsMap = default;
            CatalogItemsGroup = default;
        }

        else
        {
            CatalogItemsMap = GroupCatalogItems(SelectedCatalog.Catalog);
            CatalogItemsGroup = CatalogItemsMap?.Values.FirstOrDefault();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CatalogItemsMap)));

        if (SelectedCatalog is FakeResourceCatalogViewModel)
            ViewState = ViewState.Normal;
    }

    public void AddError(Exception error, ISnackbar? snackbar)
    {
        _errors.Add((DateTime.UtcNow, error));
        HasUnreadErrors = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Errors)));

        snackbar?.Add($"An error occured: {error.Message}", Severity.Error);
    }

    public void AddEditModeCatalog(string catalogId)
    {
        _editModeCatalogMap.Add(catalogId, []);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditModeCatalogMap)));
    }

    public async Task SaveAndRemoveEditModeCatalogAsync(string catalogId, ISnackbar snackbar)
    {
        try
        {
            if (EditModeCatalogMap.TryGetValue(catalogId, out var map))
            {
                var metadata = await _client.V1.Catalogs.GetMetadataAsync(catalogId);

                // overrides
                var overrides = (JsonObject?)JsonSerializer.SerializeToNode(metadata.Overrides);

                overrides ??= new JsonObject() { ["Id"] = catalogId };

                if (map.Any())
                {
                    // resources
                    JsonArray resources;

                    if (overrides.TryGetPropertyValue("Resources", out var resourcesNode) && resourcesNode is not null)
                        resources = resourcesNode.AsArray();

                    else
                        resources = [];

                    overrides["Resources"] = resources;

                    // for each entry
                    foreach (var (key, value) in map)
                    {
                        // resource
                        var resource = resources
                            .FirstOrDefault(current => current!.AsObject()["Id"]!.GetValue<string>() == key.ResourceId)?
                            .AsObject();

                        if (resource is null)
                        {
                            resource = new JsonObject() { ["Id"] = key.ResourceId };
                            resources.Add(resource);
                        }

                        // resource properties
                        JsonObject properties;

                        if (resource.TryGetPropertyValue("Properties", out var propertiesNode) && propertiesNode is not null)
                            properties = propertiesNode.AsObject();

                        else
                            properties = [];

                        resource["Properties"] = properties;

                        // add or remove
                        if (string.IsNullOrWhiteSpace(value))
                            properties.Remove(key.PropertyKey);

                        else
                            properties[key.PropertyKey] = value;
                    }

                    // save changes
                    metadata = metadata with
                    {
                        Overrides = JsonSerializer.Deserialize<ResourceCatalog>(overrides, JsonSerializerOptions.Web)
                    };

                    await _client.V1.Catalogs.SetMetadataAsync(catalogId, metadata, CancellationToken.None);

                    // update view
                    if (SelectedCatalog is RealResourceCatalogViewModel realCatalog)
                        realCatalog.ResetCatalogTask();

                    await SelectCatalogAsync(catalogId);

                    snackbar.Add("The metadata were successfully updated!", Severity.Success);
                }
            }
        }
        catch (Exception ex)
        {
            AddError(ex, snackbar);
        }
        finally
        {
            _editModeCatalogMap.Remove(catalogId);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditModeCatalogMap)));
        }
    }

    private SortedDictionary<string, List<CatalogItemViewModel>>? GroupCatalogItems(ResourceCatalog catalog)
    {
        if (catalog.Resources is null)
            return null;

        var catalogItemsMap = new SortedDictionary<string, List<CatalogItemViewModel>>();

        foreach (var resource in catalog.Resources)
        {
            if (resource.Representations is null || !ResourceMatchesFilter(resource))
                continue;

            var groupNames = (resource
                .Properties
                .GetStringArray(GROUP_KEY) ?? ["default"])
                .Where(groupName => groupName is not null)
                .Cast<string>();

            if (!groupNames.Any())
                groupNames = ["default"];

            foreach (var groupName in groupNames)
            {
                var success = catalogItemsMap.TryGetValue(groupName, out var group);

                if (!success)
                {
                    group = [];
                    catalogItemsMap[groupName] = group;
                }

                if (resource.Representations is not null)
                {
                    foreach (var representation in resource.Representations)
                    {
                        group!.Add(new CatalogItemViewModel(catalog, resource, representation));
                    }
                }
            }
        }

        return catalogItemsMap;
    }

    private bool ResourceMatchesFilter(Resource resource)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
            return true;

        var description = resource.Properties.GetStringValue(CatalogItemViewModel.DESCRIPTION_KEY);

        if (resource.Id.Contains(SearchString, StringComparison.OrdinalIgnoreCase) ||
            description is not null && description.Contains(SearchString, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public void SetRequestConfiguration(JsonElement configuration)
    {
        _requestConfiguration?.Dispose();
        UISettings = UISettings with { RequestConfiguration = configuration };
        _requestConfiguration = _client.AttachConfiguration(configuration);
    }

    public void ClearRequestConfiguration()
    {
        UISettings = UISettings with { RequestConfiguration = default };
    }
}