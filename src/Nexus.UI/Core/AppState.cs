using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using MudBlazor;
using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public class AppState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private ResourceCatalogViewModel? _selectedCatalog;
    private ViewState _viewState = ViewState.Normal;
    private ExportParameters _exportParameters = default!;
    private readonly INexusClient _client;
    private readonly List<(DateTime, Exception)> _errors = new();
    private readonly Dictionary<string, Dictionary<EditModeItem, string?>> _editModeCatalogMap = new();
    private bool _beginAtZero;
    private string? _searchString;
    private const string GROUP_KEY = "groups";
    private readonly IJSInProcessRuntime _jsRuntime;
    private IDisposable? _requestConfiguration;

    public AppState(
        bool isDemo,
        IReadOnlyList<AuthenticationSchemeDescription> authenticationSchemes,
        INexusClient client,
        IJSInProcessRuntime jsRuntime)
    {
        IsDemo = isDemo;
        AuthenticationSchemes = authenticationSchemes;
        _client = client;
        _jsRuntime = jsRuntime;
        Settings = new SettingsViewModel(this, jsRuntime, client);

        var childCatalogInfosTask = client.Catalogs.GetChildCatalogInfosAsync(ResourceCatalogViewModel.ROOT_CATALOG_ID, CancellationToken.None);

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
            DataSourceInfoUrl: default,
            DataSourceType: default!,
            DataSourceRegistrationId: default,
            PackageReferenceId: default);

        RootCatalog = new FakeResourceCatalogViewModel(rootInfo, "", client, this, childCatalogInfosTask);

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
        var configuration = _jsRuntime.Invoke<JsonElement?>("nexus.util.loadSetting", Constants.REQUEST_CONFIGURATION_KEY);

        if (configuration is not null)
            _requestConfiguration = _client.AttachConfiguration(configuration);

        // demo
        if (isDemo)
            BeginAtZero = true;
    }

    public bool IsDemo { get; }

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

    public IReadOnlyList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

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

    public ObservableCollection<JobViewModel> Jobs { get; set; } = new ObservableCollection<JobViewModel>();

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

        if (job.Status is null || job.Status.Status < Api.TaskStatus.RanToCompletion)
            _ = _client.Jobs.CancelJobAsync(job.Id);
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
        _editModeCatalogMap.Add(catalogId, new Dictionary<EditModeItem, string?>());
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditModeCatalogMap)));
    }

    public async Task SaveAndRemoveEditModeCatalogAsync(string catalogId, ISnackbar snackbar)
    {
        try
        {
            if (EditModeCatalogMap.TryGetValue(catalogId, out var map))
            {
                var metadata = await _client.Catalogs.GetMetadataAsync(catalogId);

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
                        resources = new JsonArray();

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
                            properties = new JsonObject();

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
                        Overrides = JsonSerializer.Deserialize<ResourceCatalog>(overrides)
                    };

                    await _client.Catalogs.SetMetadataAsync(catalogId, metadata, CancellationToken.None);

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
                .GetStringArray(GROUP_KEY) ?? new string[] { "default" })
                .Where(groupName => groupName is not null)
                .Cast<string>();

            if (!groupNames.Any())
                groupNames = new string[] { "default" };

            foreach (var groupName in groupNames)
            {
                var success = catalogItemsMap.TryGetValue(groupName, out var group);

                if (!success)
                {
                    group = new List<CatalogItemViewModel>();
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
        _jsRuntime.InvokeVoid("nexus.util.saveSetting", Constants.REQUEST_CONFIGURATION_KEY, configuration);
        _requestConfiguration = _client.AttachConfiguration(configuration);
    }

    public void ClearRequestConfiguration()
    {
        _jsRuntime.InvokeVoid("nexus.util.clearSetting", Constants.REQUEST_CONFIGURATION_KEY);
    }
}