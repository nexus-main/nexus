using Nexus.DataModel;
using Nexus.Extensibility;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

// Bug?: https://github.com/frictionlessdata/frictionless-py/issues/991
// Schema: https://frictionlessdata.io
// Comparíson: https://discuss.okfn.org/t/w3c-csv-for-the-web-how-does-it-relate-to-data-packages/1715/2
// Why not CSV on the web? https://twitter.com/readdavid/status/1195315653449793536
// Linting: https://csvlint.io/ and https://ruby-rdf.github.io/rdf-tabular/

namespace Nexus.Writers;

[DataWriterDescription(DESCRIPTION)]

[ExtensionDescription(
    "Exports comma-separated values following the frictionless data standard",
    "https://github.com/malstroem-labs/nexus",
    "https://github.com/malstroem-labs/nexus/blob/master/src/Nexus/Extensions/Writers/Csv.cs")]
internal class Csv : IDataWriter, IDisposable
{
    private const string DESCRIPTION = """
    {
        "label":"CSV + Schema (*.csv)",
        "options":{
            "row-index-format":{
                "type":"select",
                "label":"Row index format",
                "default":"excel",
                "items":{
                    "excel":"Excel time",
                    "index":"Index-based",
                    "unix":"Unix time",
                    "iso-8601":"ISO 8601"
                }
            },
            "significant-figures":{
                "type":"input-integer",
                "label":"Significant figures",
                "default":4,
                "minimum":0,
                "maximum":30
            }
        }
    }
    """;

    private static readonly DateTime _unixEpoch = new(1970, 01, 01);

    private static readonly NumberFormatInfo _nfi = new()
    {
        NumberDecimalSeparator = ".",
        NumberGroupSeparator = string.Empty
    };

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private double _unixStart;
    private double _excelStart;
    private DateTime _lastFileBegin;
    private TimeSpan _lastSamplePeriod;
    private readonly Dictionary<string, CsvResource> _resourceMap = new();

    private DataWriterContext Context { get; set; } = default!;

    public Task SetContextAsync(
        DataWriterContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public async Task OpenAsync(
        DateTime fileBegin,
        TimeSpan filePeriod,
        TimeSpan samplePeriod,
        CatalogItem[] catalogItems,
        CancellationToken cancellationToken)
    {
        _lastFileBegin = fileBegin;
        _lastSamplePeriod = samplePeriod;
        _unixStart = (fileBegin - _unixEpoch).TotalSeconds;
        _excelStart = fileBegin.ToOADate();

        foreach (var catalogItemGroup in catalogItems.GroupBy(catalogItem => catalogItem.Catalog))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var catalog = catalogItemGroup.Key;
            var physicalId = catalog.Id.TrimStart('/').Replace('/', '_');
            var root = Context.ResourceLocator.ToPath();

            /* metadata */
            var resourceFileNameWithoutExtension = $"{physicalId}_{samplePeriod.ToUnitString()}";
            var resourceFileName = $"{resourceFileNameWithoutExtension}.resource.json";
            var resourceFilePath = Path.Combine(root, resourceFileName);

            if (!_resourceMap.TryGetValue(resourceFilePath, out var resource))
            {
                var rowIndexFormat = Context.RequestConfiguration?.GetStringValue("row-index-format") ?? "index";
                var constraints = new Constraints(Required: true);

                var timestampField = rowIndexFormat switch
                {
                    "index" => new Field("Index", "integer", constraints, default),
                    "unix" => new Field("Unix time", "number", constraints, default),
                    "excel" => new Field("Excel time", "number", constraints, default),
                    "iso-8601" => new Field("ISO 8601 time", "datetime", constraints, default),
                    _ => throw new NotSupportedException($"The row index format {rowIndexFormat} is not supported.")
                };

                var layout = new Layout()
                {
                    HeaderRows = new[] { 4 }
                };

                var fields = new[] { timestampField }.Concat(catalogItemGroup.Select(catalogItem =>
                {
                    var fieldName = GetFieldName(catalogItem);

                    return new Field(
                        Name: fieldName,
                        Type: "number",
                        Constraints: constraints,
                        Properties: catalogItem.Resource.Properties);
                })).ToArray();

                var schema = new Schema(
                    PrimaryKey: timestampField.Name,
                    Fields: fields,
                    Properties: catalog.Properties
                );

                resource = new CsvResource(
                    Encoding: "utf-8-sig",
                    Format: "csv",
                    Hashing: "md5",
                    Name: resourceFileNameWithoutExtension.ToLower(),
                    Profile: "tabular-data-resource",
                    Scheme: "multipart",
                    Path: new List<string>(),
                    Layout: layout,
                    Schema: schema);

                _resourceMap[resourceFilePath] = resource;
            }

            /* data */
            var dataFileName = $"{physicalId}_{ToISO8601(fileBegin)}_{samplePeriod.ToUnitString()}.csv";
            var dataFilePath = Path.Combine(root, dataFileName);

            if (!File.Exists(dataFilePath))
            {
                var stringBuilder = new StringBuilder();

                using var streamWriter = new StreamWriter(File.Open(dataFilePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

                /* header values */
                // TODO: use .ToString("o") instead?
                stringBuilder.Append($"# date_time: {ToISO8601(fileBegin)}");
                AppendWindowsNewLine(stringBuilder);

                stringBuilder.Append($"# sample_period: {samplePeriod.ToUnitString()}");
                AppendWindowsNewLine(stringBuilder);

                stringBuilder.Append($"# catalog_id: {catalog.Id}");
                AppendWindowsNewLine(stringBuilder);

                /* field name */
                var timestampField = resource.Schema.Fields.First();
                stringBuilder.Append($"{timestampField.Name},");

                foreach (var catalogItem in catalogItemGroup)
                {
                    var fieldName = GetFieldName(catalogItem);
                    stringBuilder.Append($"{fieldName},");
                }

                stringBuilder.Remove(stringBuilder.Length - 1, 1);
                AppendWindowsNewLine(stringBuilder);

                await streamWriter.WriteAsync(stringBuilder, cancellationToken);

                resource.Path.Add(dataFileName);
            }
        }
    }

    public async Task WriteAsync(TimeSpan fileOffset, WriteRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var offset = fileOffset.Ticks / _lastSamplePeriod.Ticks;

        var requestGroups = requests
            .GroupBy(request => request.CatalogItem.Catalog.Id)
            .ToList();

        var rowIndexFormat = Context.RequestConfiguration?.GetStringValue("row-index-format") ?? "index";

        var significantFigures = int.Parse(Context.RequestConfiguration?.GetStringValue("significant-figures") ?? "4");
        significantFigures = Math.Clamp(significantFigures, 0, 30);

        var groupIndex = 0;
        var consumedLength = 0UL;
        var stringBuilder = new StringBuilder();

        foreach (var requestGroup in requestGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var catalogId = requestGroup.Key;
            var writeRequests = requestGroup.ToArray();
            var physicalId = catalogId.TrimStart('/').Replace('/', '_');
            var root = Context.ResourceLocator.ToPath();
            var filePath = Path.Combine(root, $"{physicalId}_{ToISO8601(_lastFileBegin)}_{_lastSamplePeriod.ToUnitString()}.csv");

            using var streamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.Write), Encoding.UTF8);

            var dateTimeStart = _lastFileBegin + fileOffset;

            var unixStart = _unixStart + fileOffset.TotalSeconds;
            var unixScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromSeconds(1).Ticks;

            var excelStart = _excelStart + fileOffset.TotalDays;
            var excelScalingFactor = (double)_lastSamplePeriod.Ticks / TimeSpan.FromDays(1).Ticks;

            var rowLength = writeRequests.First().Data.Length;

            for (int rowIndex = 0; rowIndex < rowLength; rowIndex++)
            {
                stringBuilder.Clear();

                switch (rowIndexFormat)
                {
                    case "index":
                        stringBuilder.Append($"{string.Format(_nfi, "{0:N0}", offset + rowIndex)},");
                        break;

                    case "unix":
                        stringBuilder.Append($"{string.Format(_nfi, "{0:N5}", unixStart + rowIndex * unixScalingFactor)},");
                        break;

                    case "excel":
                        stringBuilder.Append($"{string.Format(_nfi, "{0:N9}", excelStart + rowIndex * excelScalingFactor)},");
                        break;

                    case "iso-8601":
                        stringBuilder.Append($"{(dateTimeStart + (rowIndex * _lastSamplePeriod)).ToString("o")},");
                        break;

                    default:
                        throw new NotSupportedException($"The row index format {rowIndexFormat} is not supported.");
                }

                for (int i = 0; i < writeRequests.Length; i++)
                {
                    var value = writeRequests[i].Data.Span[rowIndex];
                    stringBuilder.Append($"{string.Format(_nfi, $"{{0:G{significantFigures}}}", value)},");
                }

                stringBuilder.Remove(stringBuilder.Length - 1, 1);
                AppendWindowsNewLine(stringBuilder);

                await streamWriter.WriteAsync(stringBuilder, cancellationToken);

                consumedLength += (ulong)writeRequests.Length;

                if (consumedLength >= 10000)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report((groupIndex + rowIndex / (double)rowLength) / requestGroups.Count);
                    consumedLength = 0;
                }
            }

            groupIndex++;
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendWindowsNewLine(StringBuilder stringBuilder)
    {
        stringBuilder.Append("\r\n");
    }

    private static string GetFieldName(CatalogItem catalogItem)
    {
        var unit = catalogItem.Resource.Properties?
            .GetStringValue(DataModelExtensions.UnitKey);

        var fieldName = $"{catalogItem.Resource.Id}_{catalogItem.Representation.Id}{DataModelUtilities.GetRepresentationParameterString(catalogItem.Parameters)}";

        fieldName += unit is null
            ? ""
            : $" ({unit})";

        return fieldName;
    }

    private static string ToISO8601(DateTime dateTime)
    {
        return dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH-mm-ssZ");
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                foreach (var (path, resource) in _resourceMap)
                {
                    var jsonString = JsonSerializer.Serialize(resource, _options);
                    File.WriteAllText(path, jsonString);
                }
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
