// MIT License
// Copyright (c) [2024] [nexus-main]

using System.ComponentModel;
using System.Text.Json;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.Api.V1;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private TimeSpan _samplePeriod = TimeSpan.FromSeconds(1);
    private readonly AppState _appState;
    private readonly INexusClient _client;
    private readonly IJSInProcessRuntime _jsRuntime;
    private List<CatalogItemSelectionViewModel> _selectedCatalogItems = [];

    public SettingsViewModel(AppState appState, IJSInProcessRuntime jsRuntime, INexusClient client)
    {
        _appState = appState;
        _jsRuntime = jsRuntime;
        _client = client;

        InitializeTask = new Lazy<Task>(InitializeAsync);
    }

    private string DefaultFileType { get; set; } = default!;

    public DateTime Begin
    {
        get
        {
            return _appState.ExportParameters.Begin;
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { Begin = DateTime.SpecifyKind(value, DateTimeKind.Utc) };

            if (_appState.ExportParameters.Begin >= _appState.ExportParameters.End)
                _appState.ExportParameters = _appState.ExportParameters with { End = _appState.ExportParameters.Begin };

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Begin)));
            CanExportChanged();
            CanVisualizeChanged();
        }
    }

    public DateTime End
    {
        get
        {
            return _appState.ExportParameters.End;
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { End = DateTime.SpecifyKind(value, DateTimeKind.Utc) };

            if (_appState.ExportParameters.End <= _appState.ExportParameters.Begin)
                _appState.ExportParameters = _appState.ExportParameters with { Begin = _appState.ExportParameters.End };

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(End)));
            CanExportChanged();
            CanVisualizeChanged();
        }
    }

    public Period SamplePeriod
    {
        get => new(_samplePeriod);
        set
        {
            _samplePeriod = value.Value == default
                ? TimeSpan.FromSeconds(1)
                : value.Value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SamplePeriod)));
            CanExportChanged();
            CanVisualizeChanged();
        }
    }

    public bool CanModifySamplePeriod { get; set; } = true;

    public Period FilePeriod
    {
        get
        {
            return new Period(_appState.ExportParameters.FilePeriod);
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { FilePeriod = value.Value };
            CanExportChanged();
        }
    }

    public string FileType
    {
        get
        {
            return _appState.ExportParameters.Type!;
        }
        set
        {
            WriterDescription = WriterDescriptions.First(description => description.Type == value);
            _appState.ExportParameters = _appState.ExportParameters with { Type = value };
            _jsRuntime.InvokeVoid("nexus.util.saveSetting", Constants.UI_FILE_TYPE_KEY, value);
        }
    }

    public IList<ExtensionDescription> WriterDescriptions { get; private set; } = default!;

    public ExtensionDescription? WriterDescription { get; private set; }

    public IDictionary<string, string> Configuration { get; } = new Dictionary<string, string>();

    public IReadOnlyList<CatalogItemSelectionViewModel> SelectedCatalogItems => _selectedCatalogItems;

    public Dictionary<string, string> WriterTypeToNameMap { get; private set; } = default!;

    public Lazy<Task> InitializeTask { get; }

    public bool CanExport
    {
        get
        {
            var result =
                CanVisualize &&
                (FilePeriod.Value == TimeSpan.Zero || FilePeriod.Value.Ticks % SamplePeriod.Value.Ticks == 0);

            return result;
        }
    }

    public bool CanVisualize
    {
        get
        {
            var canVisualize =
                Begin < End &&
                Begin.Ticks % SamplePeriod.Value.Ticks == 0 &&
                End.Ticks % SamplePeriod.Value.Ticks == 0 &&
                SelectedCatalogItems.Any() &&
                SelectedCatalogItems.All(item => item.IsValid(SamplePeriod));

            if (!canVisualize && _appState.ViewState == ViewState.Data)
                _appState.ViewState = ViewState.Normal;

            return canVisualize;
        }
    }

    public void CanExportChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExport)));
    }

    public void CanVisualizeChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanVisualize)));
    }

    public long GetTotalByteCount()
    {
        var elementCount = Utilities.GetElementCount(
            _appState.Settings.Begin,
            _appState.Settings.End,
            _appState.Settings.SamplePeriod.Value);

        var byteCount = Utilities.GetByteCount(elementCount, _appState.Settings.SelectedCatalogItems);

        return byteCount;
    }

    public ExportParameters GetExportParameters()
    {
        var samplePeriod = SamplePeriod.Value;

        var resourcePaths = SelectedCatalogItems
            .SelectMany(item => item.Kinds.Select(kind => item.GetResourcePath(kind, samplePeriod)))
            .ToList();

        var actualParameters = _appState.ExportParameters with
        {
            ResourcePaths = resourcePaths,
            Configuration = _appState.Settings.Configuration
                .ToDictionary(
                    entry => entry.Key,
                    entry => JsonSerializer.SerializeToElement(entry.Value)
                )
        };

        return actualParameters;
    }

    public bool IsSelected(CatalogItemViewModel catalogItem)
    {
        return TryFindSelectedCatalogItem(catalogItem, default) is not null;
    }

    public void SetSelectedCatalogItems(List<CatalogItemSelectionViewModel> selectedCatalogItems)
    {
        _selectedCatalogItems = selectedCatalogItems;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalogItems)));
        CanExportChanged();
        CanVisualizeChanged();
    }

    public void ToggleCatalogItemSelection(CatalogItemSelectionViewModel selection)
    {
        var reference = TryFindSelectedCatalogItem(selection.BaseItem, selection.Parameters);

        if (reference is null)
        {
            if (CanModifySamplePeriod && _selectedCatalogItems.Count == 0)
                SamplePeriod = new Period(selection.BaseItem.Representation.SamplePeriod);

            EnsureDefaultRepresentationKind(selection);
            _selectedCatalogItems.Add(selection);
        }

        else
        {
            _selectedCatalogItems.Remove(reference);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalogItems)));
        CanExportChanged();
        CanVisualizeChanged();
    }

    private CatalogItemSelectionViewModel? TryFindSelectedCatalogItem(
        CatalogItemViewModel catalogItem,
        IDictionary<string, string>? parameters)
    {
        var emptySequence = Enumerable.Empty<KeyValuePair<string, string>>();

        return SelectedCatalogItems.FirstOrDefault(current =>
            current.BaseItem.Catalog.Id == catalogItem.Catalog.Id &&
            current.BaseItem.Resource.Id == catalogItem.Resource.Id &&
            current.BaseItem.Representation.SamplePeriod == catalogItem.Representation.SamplePeriod &&
            (current.Parameters ?? emptySequence).SequenceEqual(parameters ?? emptySequence));
    }

    private void EnsureDefaultRepresentationKind(CatalogItemSelectionViewModel selectedItem)
    {
        var baseItem = selectedItem.BaseItem;
        var baseSamplePeriod = baseItem.Representation.SamplePeriod;

        if (selectedItem.Kinds.Count == 0)
        {
            if (SamplePeriod.Value < baseSamplePeriod)
                selectedItem.Kinds.Add(RepresentationKind.Resampled);

            else if (SamplePeriod.Value > baseSamplePeriod)
                selectedItem.Kinds.Add(RepresentationKind.Mean);

            else
                selectedItem.Kinds.Add(RepresentationKind.Original);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            DefaultFileType = await _client.V1.System.GetDefaultFileTypeAsync();

            var writerDescriptions = (await _client.V1.Writers
                .GetDescriptionsAsync(CancellationToken.None))
                .Where(description => description.AdditionalInformation.GetStringValue(Constants.DATA_WRITER_LABEL_KEY) is not null)
                .ToList();

            if (writerDescriptions.Count != 0)
            {
                string? actualFileType = default;

                // try restore saved file type
                var expectedFileType = _jsRuntime.Invoke<string?>("nexus.util.loadSetting", Constants.UI_FILE_TYPE_KEY);

                if (!string.IsNullOrWhiteSpace(expectedFileType) &&
                    writerDescriptions.Any(writerDescription => writerDescription.Type == expectedFileType))
                    actualFileType = expectedFileType;

                // try restore default file type
                else if (!string.IsNullOrWhiteSpace(DefaultFileType) &&
                    writerDescriptions.Any(writerDescription => writerDescription.Type == DefaultFileType))
                    actualFileType = DefaultFileType;

                // fall-back to first available file type
                else
                    actualFileType = writerDescriptions.First().Type;

                // apply
                _appState.ExportParameters = _appState.ExportParameters with
                {
                    Type = actualFileType
                };
            }

            WriterTypeToNameMap = writerDescriptions.ToDictionary(
                description => description.Type,
                description => description.AdditionalInformation.GetStringValue(Constants.DATA_WRITER_LABEL_KEY)!);

            WriterDescriptions = writerDescriptions;
            WriterDescription = writerDescriptions.First(description => description.Type == FileType);
        }
        catch (Exception ex)
        {
            _appState.AddError(ex, default);
        }
    }
}