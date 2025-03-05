#nullable enable

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Api
{
/// <summary>
/// A client for the Nexus system.
/// </summary>
public interface INexusClient
{
    /// <summary>
    /// Gets the V1 client.
    /// </summary>
    Nexus.Api.V1.IV1 V1 { get; }



    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="accessToken">The access token.</param>
    /// <returns>A task.</returns>
    void SignIn(string accessToken);

    /// <summary>
    /// Attaches configuration data to subsequent API requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    IDisposable AttachConfiguration(object configuration);

    /// <summary>
    /// Clears configuration data for all subsequent API requests.
    /// </summary>
    void ClearConfiguration();
}

/// <inheritdoc />
public class NexusClient : INexusClient, IDisposable
{
    private const string ConfigurationHeaderKey = "Nexus-Configuration";
    private const string AuthorizationHeaderKey = "Authorization";

    private string? __token;
    private HttpClient __httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="NexusClient"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    public NexusClient(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(60) })
    {
        //
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NexusClient"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    public NexusClient(HttpClient httpClient)
    {
        if (httpClient.BaseAddress is null)
            throw new Exception("The base address of the HTTP client must be set.");

        __httpClient = httpClient;

        V1 = new Nexus.Api.V1.V1(this);

    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => __token is not null;

    /// <inheritdoc />
    public Nexus.Api.V1.IV1 V1 { get; }



    /// <inheritdoc />
    public void SignIn(string accessToken)
    {
        var authorizationHeaderValue = $"Bearer {accessToken}";
        __httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        __httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        __token = accessToken;
    }

    /// <inheritdoc />
    public IDisposable AttachConfiguration(object configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        __httpClient.DefaultRequestHeaders.Remove(ConfigurationHeaderKey);
        __httpClient.DefaultRequestHeaders.Add(ConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <inheritdoc />
    public void ClearConfiguration()
    {
        __httpClient.DefaultRequestHeaders.Remove(ConfigurationHeaderKey);
    }

    internal T Invoke<T>(string method, string relativeUrl, string? acceptHeaderValue, string? contentTypeValue, HttpContent? content)
    {
        // prepare request
        using var request = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);

        // send request
        var response = __httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            var message = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
            var statusCode = $"00.{(int)response.StatusCode}";

            if (string.IsNullOrWhiteSpace(message))
                throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

            else
                throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
        }

        try
        {
            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            else if (typeof(T) == typeof(HttpResponseMessage))
            {
                return (T)(object)(response);
            }

            else
            {
                var stream = response.Content.ReadAsStream();

                try
                {
                    return JsonSerializer.Deserialize<T>(stream, Utilities.JsonOptions)!;
                }
                catch (Exception ex)
                {
                    throw new NexusException("01", "Response data could not be deserialized.", ex);
                }
            }
        }
        finally
        {
            if (typeof(T) != typeof(HttpResponseMessage))
                response.Dispose();
        }
    }

    internal async Task<T> InvokeAsync<T>(string method, string relativeUrl, string? acceptHeaderValue, string? contentTypeValue, HttpContent? content, CancellationToken cancellationToken)
    {
        // prepare request
        using var request = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);

        // send request
        var response = await __httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var statusCode = $"00.{(int)response.StatusCode}";

            if (string.IsNullOrWhiteSpace(message))
                throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

            else
                throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
        }

        try
        {
            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            else if (typeof(T) == typeof(HttpResponseMessage))
            {
                return (T)(object)(response);
            }

            else
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    return (await JsonSerializer.DeserializeAsync<T>(stream, Utilities.JsonOptions).ConfigureAwait(false))!;
                }
                catch (Exception ex)
                {
                    throw new NexusException("01", "Response data could not be deserialized.", ex);
                }
            }
        }
        finally
        {
            if (typeof(T) != typeof(HttpResponseMessage))
                response.Dispose();
        }
    }

    private static readonly HttpRequestOptionsKey<bool> WebAssemblyEnableStreamingResponseKey = new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse");

    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? content, string? contentTypeHeaderValue, string? acceptHeaderValue)
    {
        var requestMessage = new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(relativeUrl, UriKind.Relative),
            Content = content
        };

        if (contentTypeHeaderValue is not null && requestMessage.Content is not null)
            requestMessage.Content.Headers.ContentType = MediaTypeWithQualityHeaderValue.Parse(contentTypeHeaderValue);

        if (acceptHeaderValue is not null)
            requestMessage.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // For web assembly
        // https://docs.microsoft.com/de-de/dotnet/api/microsoft.aspnetcore.components.webassembly.http.webassemblyhttprequestmessageextensions.setbrowserresponsestreamingenabled?view=aspnetcore-6.0
        // https://github.com/dotnet/aspnetcore/blob/0ee742c53f2669fd7233df6da89db5e8ab944585/src/Components/WebAssembly/WebAssembly/src/Http/WebAssemblyHttpRequestMessageExtensions.cs
        requestMessage.Options.Set(WebAssemblyEnableStreamingResponseKey, true);

        return requestMessage;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        __httpClient?.Dispose();
    }

    /// <summary>
    /// This high-level methods simplifies loading multiple resources at once.
    /// </summary>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="resourcePaths">The resource paths.</param>
    /// <param name="onProgress">A callback which accepts the current progress.</param>
    public IReadOnlyDictionary<string, DataResponse> Load(
        DateTime begin, 
        DateTime end, 
        IEnumerable<string> resourcePaths,
        Action<double>? onProgress = default)
    {
        var catalogItemMap = V1.Catalogs.SearchCatalogItems(resourcePaths.ToList());
        var result = new Dictionary<string, DataResponse>();
        var progress = 0.0;

        foreach (var (resourcePath, catalogItem) in catalogItemMap)
        {
            using var responseMessage = V1.Data.GetStream(resourcePath, begin, end);

            var doubleData = ReadAsDoubleAsync(responseMessage, useAsync: false)
                .GetAwaiter()
                .GetResult();

            var resource = catalogItem.Resource;

            string? unit = default;

            if (resource.Properties is not null &&
                resource.Properties.TryGetValue("unit", out var unitElement) &&
                unitElement.ValueKind == JsonValueKind.String)
                unit = unitElement.GetString();

            string? description = default;

            if (resource.Properties is not null &&
                resource.Properties.TryGetValue("description", out var descriptionElement) &&
                descriptionElement.ValueKind == JsonValueKind.String)
                description = descriptionElement.GetString();

            var samplePeriod = catalogItem.Representation.SamplePeriod;

            result[resourcePath] = new DataResponse(
                CatalogItem: catalogItem,
                Name: resource.Id,
                Unit: unit,
                Description: description,
                SamplePeriod: samplePeriod,
                Values: doubleData
            );

            progress += 1.0 / catalogItemMap.Count;
            onProgress?.Invoke(progress);
        }

        return result;
    }

    /// <summary>
    /// This high-level methods simplifies loading multiple resources at once.
    /// </summary>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="resourcePaths">The resource paths.</param>
    /// <param name="onProgress">A callback which accepts the current progress.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public async Task<IReadOnlyDictionary<string, DataResponse>> LoadAsync(
        DateTime begin, 
        DateTime end, 
        IEnumerable<string> resourcePaths,
        Action<double>? onProgress = default,
        CancellationToken cancellationToken = default)
    {
        var catalogItemMap = await V1.Catalogs.SearchCatalogItemsAsync(resourcePaths.ToList()).ConfigureAwait(false);
        var result = new Dictionary<string, DataResponse>();
        var progress = 0.0;

        foreach (var (resourcePath, catalogItem) in catalogItemMap)
        {
            using var responseMessage = await V1.Data.GetStreamAsync(resourcePath, begin, end, cancellationToken).ConfigureAwait(false);
            var doubleData = await ReadAsDoubleAsync(responseMessage, useAsync: true, cancellationToken).ConfigureAwait(false);
            var resource = catalogItem.Resource;

            string? unit = default;

            if (resource.Properties is not null &&
                resource.Properties.TryGetValue("unit", out var unitElement) &&
                unitElement.ValueKind == JsonValueKind.String)
                unit = unitElement.GetString();

            string? description = default;

            if (resource.Properties is not null &&
                resource.Properties.TryGetValue("description", out var descriptionElement) &&
                descriptionElement.ValueKind == JsonValueKind.String)
                description = descriptionElement.GetString();

            var samplePeriod = catalogItem.Representation.SamplePeriod;

            result[resourcePath] = new DataResponse(
                CatalogItem: catalogItem,
                Name: resource.Id,
                Unit: unit,
                Description: description,
                SamplePeriod: samplePeriod,
                Values: doubleData
            );

            progress += 1.0 / catalogItemMap.Count;
            onProgress?.Invoke(progress);
        }

        return result;
    }

    private async Task<double[]> ReadAsDoubleAsync(HttpResponseMessage responseMessage, bool useAsync, CancellationToken cancellationToken = default)
    {
        int? length = default;

        if (responseMessage.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            length = contentLength;
        }

        if (!length.HasValue)
            throw new Exception("The data length is unknown.");

        if (length.Value % 8 != 0)
            throw new Exception("The data length is invalid.");

        var elementCount = length.Value / 8;
        var doubleBuffer = new double[elementCount];
        var byteBuffer = new CastMemoryManager<double, byte>(doubleBuffer).Memory;

        Stream stream = useAsync
            ? await responseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false)
            : responseMessage.Content.ReadAsStream(cancellationToken);

        var remainingBuffer = byteBuffer;

        while (!remainingBuffer.IsEmpty)
        {
            var bytesRead = await stream.ReadAsync(remainingBuffer, cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
                throw new Exception("The stream ended early.");

            remainingBuffer = remainingBuffer.Slice(bytesRead);
        }

        return doubleBuffer;
    }

    private async Task<double[]> ReadAsDoubleAsync(HttpResponseMessage responseMessage, CancellationToken cancellationToken = default)
    {
        int? length = default;

        if (responseMessage.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            length = contentLength;
        }

        if (!length.HasValue)
            throw new Exception("The data length is unknown.");

        if (length.Value % 8 != 0)
            throw new Exception("The data length is invalid.");

        var elementCount = length.Value / 8;
        var doubleBuffer = new double[elementCount];
        var byteBuffer = new CastMemoryManager<double, byte>(doubleBuffer).Memory;
        var stream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var remainingBuffer = byteBuffer;

        while (!remainingBuffer.IsEmpty)
        {
            var bytesRead = await stream.ReadAsync(remainingBuffer, cancellationToken).ConfigureAwait(false);

            if (bytesRead == 0)
                throw new Exception("The stream ended early.");

            remainingBuffer = remainingBuffer.Slice(bytesRead);
        }

        return doubleBuffer;
    }

    /// <summary>
    /// This high-level methods simplifies exporting multiple resources at once.
    /// </summary>
    /// <param name="begin">The begin date/time.</param>
    /// <param name="end">The end date/time.</param>
    /// <param name="filePeriod">The file period. Use TimeSpan.Zero to get a single file.</param>
    /// <param name="fileFormat">The target file format. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.</param>
    /// <param name="resourcePaths">The resource paths to export.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="targetFolder">The target folder for the files to extract.</param>
    /// <param name="onProgress">A callback which accepts the current progress and the progress message.</param>
    public void Export(
        DateTime begin, 
        DateTime end,
        TimeSpan filePeriod,
        string? fileFormat,
        IEnumerable<string> resourcePaths,
        IReadOnlyDictionary<string, object>? configuration,
        string targetFolder,
        Action<double, string>? onProgress = default)
    {
        var actualConfiguration = configuration is null
            ? default
            : JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(JsonSerializer.Serialize(configuration));

        var exportParameters = new V1.ExportParameters(
            begin,
            end,
            filePeriod,
            fileFormat,
            resourcePaths.ToList(),
            actualConfiguration);

        // Start Job
        var job = V1.Jobs.Export(exportParameters);

        // Wait for job to finish
        string? artifactId = default;

        while (true)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));

            var jobStatus = V1.Jobs.GetJobStatus(job.Id);

            if (jobStatus.Status == Nexus.Api.V1.TaskStatus.Canceled)
                throw new OperationCanceledException("The job has been cancelled.");

            else if (jobStatus.Status == Nexus.Api.V1.TaskStatus.Faulted)
                throw new OperationCanceledException($"The job has failed. Reason: {jobStatus.ExceptionMessage}");

            else if (jobStatus.Status == Nexus.Api.V1.TaskStatus.RanToCompletion)
            {
                if (jobStatus.Result.HasValue &&
                    jobStatus.Result.Value.ValueKind == JsonValueKind.String)
                {
                    artifactId = jobStatus.Result.Value.GetString();
                    break;
                }
            }

            if (jobStatus.Progress < 1)
                onProgress?.Invoke(jobStatus.Progress, "export");
        }

        onProgress?.Invoke(1, "export");

        if (artifactId is null)
            throw new Exception("The job result is invalid.");

        if (fileFormat is null)
            return;

        // Download zip file
        var responseMessage = V1.Artifacts.Download(artifactId);
        var sourceStream = responseMessage.Content.ReadAsStream();

        long? length = default;

        if (responseMessage.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            length = contentLength;
        }

        var tmpFilePath = Path.GetTempFileName();

        try
        {
            using (var targetStream = File.OpenWrite(tmpFilePath))
            {
                var buffer = new byte[32768];
                var consumed = 0;
                var sw = Stopwatch.StartNew();
                var maxTicks = TimeSpan.FromSeconds(1).Ticks;

                int receivedBytes;

                while ((receivedBytes = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    targetStream.Write(buffer, 0, receivedBytes);
                    consumed += receivedBytes;

                    if (sw.ElapsedTicks > maxTicks)
                    {
                        sw.Reset();

                        if (length.HasValue)
                        {
                            if (consumed < length)
                                onProgress?.Invoke(consumed / (double)length, "download");
                        }
                    }
                }
            }

            onProgress?.Invoke(1, "download");

            // Extract file (do not use stream overload: https://github.com/dotnet/runtime/issues/59027)
            ZipFile.ExtractToDirectory(tmpFilePath, targetFolder, overwriteFiles: true);
            onProgress?.Invoke(1, "extract");
        }
        finally
        {
            try
            {
                File.Delete(tmpFilePath);
            }
            catch
            {
                //
            }
        }
    }

    /// <summary>
    /// This high-level methods simplifies exporting multiple resources at once.
    /// </summary>
    /// <param name="begin">The begin date/time.</param>
    /// <param name="end">The end date/time.</param>
    /// <param name="filePeriod">The file period. Use TimeSpan.Zero to get a single file.</param>
    /// <param name="fileFormat">The target file format. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.</param>
    /// <param name="resourcePaths">The resource paths to export.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="targetFolder">The target folder for the files to extract.</param>
    /// <param name="onProgress">A callback which accepts the current progress and the progress message.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public async Task ExportAsync(
        DateTime begin, 
        DateTime end,
        TimeSpan filePeriod,
        string? fileFormat,
        IEnumerable<string> resourcePaths,
        IReadOnlyDictionary<string, object>? configuration,
        string targetFolder,
        Action<double, string>? onProgress = default,
        CancellationToken cancellationToken = default)
    {
        var actualConfiguration = configuration is null
            ? default
            : JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(JsonSerializer.Serialize(configuration));

        var exportParameters = new V1.ExportParameters(
            begin,
            end,
            filePeriod,
            fileFormat,
            resourcePaths.ToList(),
            actualConfiguration);

        // Start Job
        var job = await V1.Jobs.ExportAsync(exportParameters).ConfigureAwait(false);

        // Wait for job to finish
        string? artifactId = default;

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            var jobStatus = await V1.Jobs.GetJobStatusAsync(job.Id, cancellationToken).ConfigureAwait(false);

            if (jobStatus.Status == Nexus.Api.V1.TaskStatus.Canceled)
                throw new OperationCanceledException("The job has been cancelled.");

            else if (jobStatus.Status == Nexus.Api.V1.TaskStatus.Faulted)
                throw new OperationCanceledException($"The job has failed. Reason: {jobStatus.ExceptionMessage}");

            else if (jobStatus.Status == Nexus.Api.V1.TaskStatus.RanToCompletion)
            {
                if (jobStatus.Result.HasValue &&
                    jobStatus.Result.Value.ValueKind == JsonValueKind.String)
                {
                    artifactId = jobStatus.Result.Value.GetString();
                    break;
                }
            }

            if (jobStatus.Progress < 1)
                onProgress?.Invoke(jobStatus.Progress, "export");
        }

        onProgress?.Invoke(1, "export");

        if (artifactId is null)
            throw new Exception("The job result is invalid.");

        if (fileFormat is null)
            return;

        // Download zip file
        var responseMessage = await V1.Artifacts.DownloadAsync(artifactId, cancellationToken).ConfigureAwait(false);
        var sourceStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);

        long? length = default;

        if (responseMessage.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            length = contentLength;
        }

        var tmpFilePath = Path.GetTempFileName();

        try
        {
            using (var targetStream = File.OpenWrite(tmpFilePath))
            {
                var buffer = new byte[32768];
                var consumed = 0;
                var sw = Stopwatch.StartNew();
                var maxTicks = TimeSpan.FromSeconds(1).Ticks;

                int receivedBytes;

                while ((receivedBytes = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    targetStream.Write(buffer, 0, receivedBytes);
                    consumed += receivedBytes;

                    if (sw.ElapsedTicks > maxTicks)
                    {
                        sw.Reset();

                        if (length.HasValue)
                        {
                            if (consumed < length)
                                onProgress?.Invoke(consumed / (double)length, "download");
                        }
                    }
                }
            }

            onProgress?.Invoke(1, "download");

            // Extract file (do not use stream overload: https://github.com/dotnet/runtime/issues/59027)
            ZipFile.ExtractToDirectory(tmpFilePath, targetFolder, overwriteFiles: true);
            onProgress?.Invoke(1, "extract");
        }
        finally
        {
            try
            {
                File.Delete(tmpFilePath);
            }
            catch
            {
                //
            }
        }
    }
}

internal class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
     where TFrom : struct
     where TTo : struct
{
    private readonly Memory<TFrom> _from;

    public CastMemoryManager(Memory<TFrom> from) => _from = from;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

    public override void Unpin() => throw new NotSupportedException();
}

/// <summary>
/// A NexusException.
/// </summary>
public class NexusException : Exception
{
    internal NexusException(string statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    internal NexusException(string statusCode, string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The exception status code.
    /// </summary>
    public string StatusCode { get; }
}

internal class DisposableConfiguration : IDisposable
{
    private NexusClient ___client;

    public DisposableConfiguration(NexusClient client)
    {
        ___client = client;
    }

    public void Dispose()
    {
        ___client.ClearConfiguration();
    }
}

internal static class Utilities
{
    internal static JsonSerializerOptions JsonOptions { get; }

    static Utilities()
    {
        JsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }
}

/// <summary>
/// Result of a data request with a certain resource path.
/// </summary>
/// <param name="CatalogItem">The catalog item.</param>
/// <param name="Name">The resource name.</param>
/// <param name="Unit">The optional resource unit.</param>
/// <param name="Description">The optional resource description.</param>
/// <param name="SamplePeriod">The sample period.</param>
/// <param name="Values">The data.</param>
public record DataResponse(
    V1.CatalogItem CatalogItem, 
    string? Name,
    string? Unit,
    string? Description,
    TimeSpan SamplePeriod,
    double[] Values);
}

namespace Nexus.Api.V1
{

/// <summary>
/// A client for version V1.
/// </summary>
public interface IV1
{
    /// <summary>
    /// Gets the <see cref="IArtifactsClient"/>.
    /// </summary>
    IArtifactsClient Artifacts { get; }

    /// <summary>
    /// Gets the <see cref="ICatalogsClient"/>.
    /// </summary>
    ICatalogsClient Catalogs { get; }

    /// <summary>
    /// Gets the <see cref="IDataClient"/>.
    /// </summary>
    IDataClient Data { get; }

    /// <summary>
    /// Gets the <see cref="IJobsClient"/>.
    /// </summary>
    IJobsClient Jobs { get; }

    /// <summary>
    /// Gets the <see cref="IPackageReferencesClient"/>.
    /// </summary>
    IPackageReferencesClient PackageReferences { get; }

    /// <summary>
    /// Gets the <see cref="ISourcesClient"/>.
    /// </summary>
    ISourcesClient Sources { get; }

    /// <summary>
    /// Gets the <see cref="ISystemClient"/>.
    /// </summary>
    ISystemClient System { get; }

    /// <summary>
    /// Gets the <see cref="IUsersClient"/>.
    /// </summary>
    IUsersClient Users { get; }

    /// <summary>
    /// Gets the <see cref="IWritersClient"/>.
    /// </summary>
    IWritersClient Writers { get; }


}

/// <inheritdoc />
public class V1 : IV1
{
    /// <summary>
    /// Initializes a new instance of the <see cref="V1"/>.
    /// </summary>
    /// <param name="client">The client to use.</param>
    public V1(NexusClient client)
    {
        Artifacts = new ArtifactsClient(client);
        Catalogs = new CatalogsClient(client);
        Data = new DataClient(client);
        Jobs = new JobsClient(client);
        PackageReferences = new PackageReferencesClient(client);
        Sources = new SourcesClient(client);
        System = new SystemClient(client);
        Users = new UsersClient(client);
        Writers = new WritersClient(client);

    }

    /// <inheritdoc />
    public IArtifactsClient Artifacts { get; }

    /// <inheritdoc />
    public ICatalogsClient Catalogs { get; }

    /// <inheritdoc />
    public IDataClient Data { get; }

    /// <inheritdoc />
    public IJobsClient Jobs { get; }

    /// <inheritdoc />
    public IPackageReferencesClient PackageReferences { get; }

    /// <inheritdoc />
    public ISourcesClient Sources { get; }

    /// <inheritdoc />
    public ISystemClient System { get; }

    /// <inheritdoc />
    public IUsersClient Users { get; }

    /// <inheritdoc />
    public IWritersClient Writers { get; }


}

/// <summary>
/// Provides methods to interact with artifacts.
/// </summary>
public interface IArtifactsClient
{
    /// <summary>
    /// Gets the specified artifact.
    /// </summary>
    /// <param name="artifactId">The artifact identifier.</param>
    HttpResponseMessage Download(string artifactId);

    /// <summary>
    /// Gets the specified artifact.
    /// </summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DownloadAsync(string artifactId, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class ArtifactsClient : IArtifactsClient
{
    private NexusClient ___client;
    
    internal ArtifactsClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public HttpResponseMessage Download(string artifactId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/artifacts/{artifactId}");
        __urlBuilder.Replace("{artifactId}", Uri.EscapeDataString(artifactId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DownloadAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/artifacts/{artifactId}");
        __urlBuilder.Replace("{artifactId}", Uri.EscapeDataString(artifactId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with catalogs.
/// </summary>
public interface ICatalogsClient
{
    /// <summary>
    /// Searches for the given resource paths and returns the corresponding catalog items.
    /// </summary>
    /// <param name="resourcePaths">The list of resource paths.</param>
    IReadOnlyDictionary<string, CatalogItem> SearchCatalogItems(IReadOnlyList<string> resourcePaths);

    /// <summary>
    /// Searches for the given resource paths and returns the corresponding catalog items.
    /// </summary>
    /// <param name="resourcePaths">The list of resource paths.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, CatalogItem>> SearchCatalogItemsAsync(IReadOnlyList<string> resourcePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    ResourceCatalog Get(string catalogId);

    /// <summary>
    /// Gets the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ResourceCatalog> GetAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of child catalog info for the provided parent catalog identifier.
    /// </summary>
    /// <param name="catalogId">The parent catalog identifier.</param>
    IReadOnlyList<CatalogInfo> GetChildCatalogInfos(string catalogId);

    /// <summary>
    /// Gets a list of child catalog info for the provided parent catalog identifier.
    /// </summary>
    /// <param name="catalogId">The parent catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<CatalogInfo>> GetChildCatalogInfosAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified catalog's time range.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    CatalogTimeRange GetTimeRange(string catalogId);

    /// <summary>
    /// Gets the specified catalog's time range.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified catalog's availability.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="step">Step period.</param>
    CatalogAvailability GetAvailability(string catalogId, DateTime begin, DateTime end, TimeSpan step);

    /// <summary>
    /// Gets the specified catalog's availability.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="step">Step period.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, TimeSpan step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the license of the catalog if available.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    string? GetLicense(string catalogId);

    /// <summary>
    /// Gets the license of the catalog if available.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string?> GetLicenseAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all attachments for the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    IReadOnlyList<string> GetAttachments(string catalogId);

    /// <summary>
    /// Gets all attachments for the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<string>> GetAttachmentsAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="content">The binary file content.</param>
    HttpResponseMessage UploadAttachment(string catalogId, string attachmentId, Stream content);

    /// <summary>
    /// Uploads the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="content">The binary file content.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> UploadAttachmentAsync(string catalogId, string attachmentId, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    HttpResponseMessage DeleteAttachment(string catalogId, string attachmentId);

    /// <summary>
    /// Deletes the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteAttachmentAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    HttpResponseMessage GetAttachmentStream(string catalogId, string attachmentId);

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> GetAttachmentStreamAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    CatalogMetadata GetMetadata(string catalogId);

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogMetadata> GetMetadataAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="metadata">The catalog metadata to set.</param>
    HttpResponseMessage SetMetadata(string catalogId, CatalogMetadata metadata);

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="metadata">The catalog metadata to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class CatalogsClient : ICatalogsClient
{
    private NexusClient ___client;
    
    internal CatalogsClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, CatalogItem> SearchCatalogItems(IReadOnlyList<string> resourcePaths)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/search-items");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, CatalogItem>>("POST", __url, "application/json", "application/json", JsonContent.Create(resourcePaths, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, CatalogItem>> SearchCatalogItemsAsync(IReadOnlyList<string> resourcePaths, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/search-items");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, CatalogItem>>("POST", __url, "application/json", "application/json", JsonContent.Create(resourcePaths, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public ResourceCatalog Get(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<ResourceCatalog>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<ResourceCatalog> GetAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<ResourceCatalog>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<CatalogInfo> GetChildCatalogInfos(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/child-catalog-infos");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<CatalogInfo>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogInfo>> GetChildCatalogInfosAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/child-catalog-infos");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<CatalogInfo>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public CatalogTimeRange GetTimeRange(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/timerange");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<CatalogTimeRange>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/timerange");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<CatalogTimeRange>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public CatalogAvailability GetAvailability(string catalogId, DateTime begin, DateTime end, TimeSpan step)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/availability");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __queryValues = new Dictionary<string, string>();

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["step"] = Uri.EscapeDataString(Convert.ToString(step, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<CatalogAvailability>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, TimeSpan step, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/availability");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __queryValues = new Dictionary<string, string>();

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["step"] = Uri.EscapeDataString(Convert.ToString(step, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<CatalogAvailability>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public string? GetLicense(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/license");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string?>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<string?> GetLicenseAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/license");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string?>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAttachments(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<string>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAttachmentsAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<string>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage UploadAttachment(string catalogId, string attachmentId, Stream content)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/octet-stream", new StreamContent(content));
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> UploadAttachmentAsync(string catalogId, string attachmentId, Stream content, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/octet-stream", new StreamContent(content), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteAttachment(string catalogId, string attachmentId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteAttachmentAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage GetAttachmentStream(string catalogId, string attachmentId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAttachmentStreamAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));
        __urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(attachmentId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public CatalogMetadata GetMetadata(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<CatalogMetadata>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<CatalogMetadata> GetMetadataAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<CatalogMetadata>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage SetMetadata(string catalogId, CatalogMetadata metadata)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(metadata, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(metadata, options: Utilities.JsonOptions), cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with data.
/// </summary>
public interface IDataClient
{
    /// <summary>
    /// Gets the requested data.
    /// </summary>
    /// <param name="resourcePath">The path to the resource data to stream.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    HttpResponseMessage GetStream(string resourcePath, DateTime begin, DateTime end);

    /// <summary>
    /// Gets the requested data.
    /// </summary>
    /// <param name="resourcePath">The path to the resource data to stream.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> GetStreamAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class DataClient : IDataClient
{
    private NexusClient ___client;
    
    internal DataClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public HttpResponseMessage GetStream(string resourcePath, DateTime begin, DateTime end)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/data");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["resourcePath"] = Uri.EscapeDataString(resourcePath);

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetStreamAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/data");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["resourcePath"] = Uri.EscapeDataString(resourcePath);

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with jobs.
/// </summary>
public interface IJobsClient
{
    /// <summary>
    /// Gets a list of jobs.
    /// </summary>
    IReadOnlyList<Job> GetJobs();

    /// <summary>
    /// Gets a list of jobs.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<Job>> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    HttpResponseMessage CancelJob(Guid jobId);

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    JobStatus GetJobStatus(Guid jobId);

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="parameters">Export parameters.</param>
    Job Export(ExportParameters parameters);

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="parameters">Export parameters.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new job which reloads all extensions and resets the resource catalog.
    /// </summary>
    Job RefreshDatabase();

    /// <summary>
    /// Creates a new job which reloads all extensions and resets the resource catalog.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> RefreshDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the aggregation data cache for the specified period of time.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    Job ClearCache(string catalogId, DateTime begin, DateTime end);

    /// <summary>
    /// Clears the aggregation data cache for the specified period of time.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> ClearCacheAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class JobsClient : IJobsClient
{
    private NexusClient ___client;
    
    internal JobsClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public IReadOnlyList<Job> GetJobs()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<Job>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Job>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<Job>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage CancelJob(Guid jobId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/{jobId}");
        __urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/{jobId}");
        __urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public JobStatus GetJobStatus(Guid jobId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/{jobId}/status");
        __urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<JobStatus>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/{jobId}/status");
        __urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<JobStatus>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Job Export(ExportParameters parameters)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/export");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Job>("POST", __url, "application/json", "application/json", JsonContent.Create(parameters, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/export");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Job>("POST", __url, "application/json", "application/json", JsonContent.Create(parameters, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Job RefreshDatabase()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/refresh-database");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Job>("POST", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<Job> RefreshDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/refresh-database");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Job>("POST", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Job ClearCache(string catalogId, DateTime begin, DateTime end)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/clear-cache");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["catalogId"] = Uri.EscapeDataString(catalogId);

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Job>("POST", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<Job> ClearCacheAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/jobs/clear-cache");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["catalogId"] = Uri.EscapeDataString(catalogId);

        __queryValues["begin"] = Uri.EscapeDataString(begin.ToString("o", CultureInfo.InvariantCulture));

        __queryValues["end"] = Uri.EscapeDataString(end.ToString("o", CultureInfo.InvariantCulture));

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Job>("POST", __url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with package references.
/// </summary>
public interface IPackageReferencesClient
{
    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    IReadOnlyDictionary<string, PackageReference> Get();

    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, PackageReference>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to create.</param>
    Guid Create(PackageReference packageReference);

    /// <summary>
    /// Creates a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Guid> CreateAsync(PackageReference packageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a package reference.
    /// </summary>
    /// <param name="id">The identifier of the package reference to update.</param>
    /// <param name="packageReference">The new package reference.</param>
    HttpResponseMessage Update(PackageReference packageReference, Guid? id = default);

    /// <summary>
    /// Updates a package reference.
    /// </summary>
    /// <param name="id">The identifier of the package reference to update.</param>
    /// <param name="packageReference">The new package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> UpdateAsync(PackageReference packageReference, Guid? id = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    void Delete(Guid id);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    IReadOnlyList<string> GetVersions(Guid id);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="id">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<string>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class PackageReferencesClient : IPackageReferencesClient
{
    private NexusClient ___client;
    
    internal PackageReferencesClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PackageReference> Get()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, PackageReference>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, PackageReference>> GetAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, PackageReference>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Guid Create(PackageReference packageReference)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(packageReference, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<Guid> CreateAsync(PackageReference packageReference, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(packageReference, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage Update(PackageReference packageReference, Guid? id = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __queryValues = new Dictionary<string, string>();

        if (id is not null)
            __queryValues["id"] = Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(packageReference, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> UpdateAsync(PackageReference packageReference, Guid? id = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences");

        var __queryValues = new Dictionary<string, string>();

        if (id is not null)
            __queryValues["id"] = Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(packageReference, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public void Delete(Guid id)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{id}");
        __urlBuilder.Replace("{id}", Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        ___client.Invoke<object>("DELETE", __url, default, default, default);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{id}");
        __urlBuilder.Replace("{id}", Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<object>("DELETE", __url, default, default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVersions(Guid id)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{id}/versions");
        __urlBuilder.Replace("{id}", Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<string>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{id}/versions");
        __urlBuilder.Replace("{id}", Uri.EscapeDataString(Convert.ToString(id, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<string>>("GET", __url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with sources.
/// </summary>
public interface ISourcesClient
{
    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    IReadOnlyList<ExtensionDescription> GetDescriptions();

    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of data source pipelines.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    IReadOnlyDictionary<string, DataSourcePipeline> GetPipelines(string? userId = default);

    /// <summary>
    /// Gets the list of data source pipelines.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, DataSourcePipeline>> GetPipelinesAsync(string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a data source pipeline.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="pipeline">The pipeline to create.</param>
    Guid CreatePipeline(DataSourcePipeline pipeline, string? userId = default);

    /// <summary>
    /// Creates a data source pipeline.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="pipeline">The pipeline to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Guid> CreatePipelineAsync(DataSourcePipeline pipeline, string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to update.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="pipeline">The new pipeline.</param>
    HttpResponseMessage UpdatePipeline(Guid pipelineId, DataSourcePipeline pipeline, string? userId = default);

    /// <summary>
    /// Updates a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to update.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="pipeline">The new pipeline.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> UpdatePipelineAsync(Guid pipelineId, DataSourcePipeline pipeline, string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to delete.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    HttpResponseMessage DeletePipeline(Guid pipelineId, string? userId = default);

    /// <summary>
    /// Deletes a data source pipeline.
    /// </summary>
    /// <param name="pipelineId">The identifier of the pipeline to delete.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeletePipelineAsync(Guid pipelineId, string? userId = default, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class SourcesClient : ISourcesClient
{
    private NexusClient ___client;
    
    internal SourcesClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtensionDescription> GetDescriptions()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/descriptions");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<ExtensionDescription>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/descriptions");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<ExtensionDescription>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, DataSourcePipeline> GetPipelines(string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, DataSourcePipeline>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, DataSourcePipeline>> GetPipelinesAsync(string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, DataSourcePipeline>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Guid CreatePipeline(DataSourcePipeline pipeline, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(pipeline, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<Guid> CreatePipelineAsync(DataSourcePipeline pipeline, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(pipeline, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage UpdatePipeline(Guid pipelineId, DataSourcePipeline pipeline, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines/{pipelineId}");
        __urlBuilder.Replace("{pipelineId}", Uri.EscapeDataString(Convert.ToString(pipelineId, CultureInfo.InvariantCulture)!));

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(pipeline, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> UpdatePipelineAsync(Guid pipelineId, DataSourcePipeline pipeline, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines/{pipelineId}");
        __urlBuilder.Replace("{pipelineId}", Uri.EscapeDataString(Convert.ToString(pipelineId, CultureInfo.InvariantCulture)!));

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("PUT", __url, "application/octet-stream", "application/json", JsonContent.Create(pipeline, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeletePipeline(Guid pipelineId, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines/{pipelineId}");
        __urlBuilder.Replace("{pipelineId}", Uri.EscapeDataString(Convert.ToString(pipelineId, CultureInfo.InvariantCulture)!));

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeletePipelineAsync(Guid pipelineId, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/pipelines/{pipelineId}");
        __urlBuilder.Replace("{pipelineId}", Uri.EscapeDataString(Convert.ToString(pipelineId, CultureInfo.InvariantCulture)!));

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with system.
/// </summary>
public interface ISystemClient
{
    /// <summary>
    /// Gets the default file type.
    /// </summary>
    string GetDefaultFileType();

    /// <summary>
    /// Gets the default file type.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> GetDefaultFileTypeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the configured help link.
    /// </summary>
    string GetHelpLink();

    /// <summary>
    /// Gets the configured help link.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> GetHelpLinkAsync(CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class SystemClient : ISystemClient
{
    private NexusClient ___client;
    
    internal SystemClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public string GetDefaultFileType()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/file-type");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<string> GetDefaultFileTypeAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/file-type");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public string GetHelpLink()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/help-link");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<string> GetHelpLinkAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/help-link");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string>("GET", __url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with users.
/// </summary>
public interface IUsersClient
{
    /// <summary>
    /// Authenticates the user.
    /// </summary>
    /// <param name="scheme">The authentication scheme to challenge.</param>
    /// <param name="returnUrl">The URL to return after successful authentication.</param>
    HttpResponseMessage Authenticate(string scheme, string returnUrl);

    /// <summary>
    /// Authenticates the user.
    /// </summary>
    /// <param name="scheme">The authentication scheme to challenge.</param>
    /// <param name="returnUrl">The URL to return after successful authentication.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out the user.
    /// </summary>
    /// <param name="returnUrl">The URL to return after logout.</param>
    void SignOut(string returnUrl);

    /// <summary>
    /// Logs out the user.
    /// </summary>
    /// <param name="returnUrl">The URL to return after logout.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SignOutAsync(string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="value">The personal access token to delete.</param>
    HttpResponseMessage DeleteTokenByValue(string value);

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="value">The personal access token to delete.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteTokenByValueAsync(string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user.
    /// </summary>
    MeResponse GetMe();

    /// <summary>
    /// Gets the current user.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Allows the user to reauthenticate in case of modified claims.
    /// </summary>
    HttpResponseMessage ReAuthenticate();

    /// <summary>
    /// Allows the user to reauthenticate in case of modified claims.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> ReAuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a personal access token.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="token">The personal access token to create.</param>
    string CreateToken(PersonalAccessToken token, string? userId = default);

    /// <summary>
    /// Creates a personal access token.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="token">The personal access token to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> CreateTokenAsync(PersonalAccessToken token, string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="tokenId">The identifier of the personal access token.</param>
    HttpResponseMessage DeleteToken(Guid tokenId);

    /// <summary>
    /// Deletes a personal access token.
    /// </summary>
    /// <param name="tokenId">The identifier of the personal access token.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the license of the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    HttpResponseMessage AcceptLicense(string catalogId);

    /// <summary>
    /// Accepts the license of the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> AcceptLicenseAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    IReadOnlyDictionary<string, NexusUser> GetUsers();

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user.
    /// </summary>
    /// <param name="user">The user to create.</param>
    string CreateUser(NexusUser user);

    /// <summary>
    /// Creates a user.
    /// </summary>
    /// <param name="user">The user to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> CreateUserAsync(NexusUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    HttpResponseMessage DeleteUser(string userId);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all claims.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    IReadOnlyDictionary<string, NexusClaim> GetClaims(string userId);

    /// <summary>
    /// Gets all claims.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, NexusClaim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claim">The claim to create.</param>
    Guid CreateClaim(string userId, NexusClaim claim);

    /// <summary>
    /// Creates a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claim">The claim to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Guid> CreateClaimAsync(string userId, NexusClaim claim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a claim.
    /// </summary>
    /// <param name="claimId">The identifier of the claim.</param>
    HttpResponseMessage DeleteClaim(Guid claimId);

    /// <summary>
    /// Deletes a claim.
    /// </summary>
    /// <param name="claimId">The identifier of the claim.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteClaimAsync(Guid claimId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all personal access tokens.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    IReadOnlyDictionary<string, PersonalAccessToken> GetTokens(string userId);

    /// <summary>
    /// Gets all personal access tokens.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, PersonalAccessToken>> GetTokensAsync(string userId, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class UsersClient : IUsersClient
{
    private NexusClient ___client;
    
    internal UsersClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public HttpResponseMessage Authenticate(string scheme, string returnUrl)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/authenticate");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["scheme"] = Uri.EscapeDataString(scheme);

        __queryValues["returnUrl"] = Uri.EscapeDataString(returnUrl);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("POST", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/authenticate");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["scheme"] = Uri.EscapeDataString(scheme);

        __queryValues["returnUrl"] = Uri.EscapeDataString(returnUrl);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("POST", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public void SignOut(string returnUrl)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/signout");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["returnUrl"] = Uri.EscapeDataString(returnUrl);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        ___client.Invoke<object>("POST", __url, default, default, default);
    }

    /// <inheritdoc />
    public Task SignOutAsync(string returnUrl, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/signout");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["returnUrl"] = Uri.EscapeDataString(returnUrl);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<object>("POST", __url, default, default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteTokenByValue(string value)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/delete");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["value"] = Uri.EscapeDataString(value);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteTokenByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/delete");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["value"] = Uri.EscapeDataString(value);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public MeResponse GetMe()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/me");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<MeResponse>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/me");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<MeResponse>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage ReAuthenticate()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/reauthenticate");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> ReAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/reauthenticate");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public string CreateToken(PersonalAccessToken token, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/create");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string>("POST", __url, "application/json", "application/json", JsonContent.Create(token, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<string> CreateTokenAsync(PersonalAccessToken token, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/create");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string>("POST", __url, "application/json", "application/json", JsonContent.Create(token, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteToken(Guid tokenId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/{tokenId}");
        __urlBuilder.Replace("{tokenId}", Uri.EscapeDataString(Convert.ToString(tokenId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteTokenAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/{tokenId}");
        __urlBuilder.Replace("{tokenId}", Uri.EscapeDataString(Convert.ToString(tokenId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage AcceptLicense(string catalogId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/accept-license");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["catalogId"] = Uri.EscapeDataString(catalogId);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> AcceptLicenseAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/accept-license");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["catalogId"] = Uri.EscapeDataString(catalogId);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("GET", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, NexusUser> GetUsers()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, NexusUser>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, NexusUser>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public string CreateUser(NexusUser user)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string>("POST", __url, "application/json", "application/json", JsonContent.Create(user, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<string> CreateUserAsync(NexusUser user, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string>("POST", __url, "application/json", "application/json", JsonContent.Create(user, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteUser(string userId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, NexusClaim> GetClaims(string userId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/claims");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, NexusClaim>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, NexusClaim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/claims");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, NexusClaim>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Guid CreateClaim(string userId, NexusClaim claim)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/claims");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(claim, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<Guid> CreateClaimAsync(string userId, NexusClaim claim, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/claims");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(claim, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteClaim(Guid claimId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/claims/{claimId}");
        __urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/claims/{claimId}");
        __urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PersonalAccessToken> GetTokens(string userId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/tokens");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, PersonalAccessToken>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, PersonalAccessToken>> GetTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/tokens");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, PersonalAccessToken>>("GET", __url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with writers.
/// </summary>
public interface IWritersClient
{
    /// <summary>
    /// Gets the list of writer descriptions.
    /// </summary>
    IReadOnlyList<ExtensionDescription> GetDescriptions();

    /// <summary>
    /// Gets the list of writer descriptions.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class WritersClient : IWritersClient
{
    private NexusClient ___client;
    
    internal WritersClient(NexusClient client)
    {
        ___client = client;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExtensionDescription> GetDescriptions()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/writers/descriptions");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<ExtensionDescription>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/writers/descriptions");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<ExtensionDescription>>("GET", __url, "application/json", default, default, cancellationToken);
    }

}



/// <summary>
/// A catalog item consists of a catalog, a resource and a representation.
/// </summary>
/// <param name="Catalog">The catalog.</param>
/// <param name="Resource">The resource.</param>
/// <param name="Representation">The representation.</param>
/// <param name="Parameters">The optional dictionary of representation parameters and its arguments.</param>
public record CatalogItem(ResourceCatalog Catalog, Resource Resource, Representation Representation, IReadOnlyDictionary<string, string>? Parameters);

/// <summary>
/// A catalog is a top level element and holds a list of resources.
/// </summary>
/// <param name="Id">Gets the identifier.</param>
/// <param name="Properties">Gets the properties.</param>
/// <param name="Resources">Gets the list of representations.</param>
public record ResourceCatalog(string Id, IReadOnlyDictionary<string, JsonElement>? Properties, IReadOnlyList<Resource>? Resources);

/// <summary>
/// A resource is part of a resource catalog and holds a list of representations.
/// </summary>
/// <param name="Id">Gets the identifier.</param>
/// <param name="Properties">Gets the properties.</param>
/// <param name="Representations">Gets the list of representations.</param>
public record Resource(string Id, IReadOnlyDictionary<string, JsonElement>? Properties, IReadOnlyList<Representation>? Representations);

/// <summary>
/// A representation is part of a resource.
/// </summary>
/// <param name="DataType">The data type.</param>
/// <param name="SamplePeriod">The sample period.</param>
/// <param name="Parameters">The optional list of parameters.</param>
public record Representation(NexusDataType DataType, TimeSpan SamplePeriod, IReadOnlyDictionary<string, JsonElement>? Parameters);

/// <summary>
/// Specifies the Nexus data type.
/// </summary>
public enum NexusDataType
{
    /// <summary>
    /// UINT8
    /// </summary>
    UINT8,

    /// <summary>
    /// UINT16
    /// </summary>
    UINT16,

    /// <summary>
    /// UINT32
    /// </summary>
    UINT32,

    /// <summary>
    /// UINT64
    /// </summary>
    UINT64,

    /// <summary>
    /// INT8
    /// </summary>
    INT8,

    /// <summary>
    /// INT16
    /// </summary>
    INT16,

    /// <summary>
    /// INT32
    /// </summary>
    INT32,

    /// <summary>
    /// INT64
    /// </summary>
    INT64,

    /// <summary>
    /// FLOAT32
    /// </summary>
    FLOAT32,

    /// <summary>
    /// FLOAT64
    /// </summary>
    FLOAT64
}


/// <summary>
/// A structure for catalog information.
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="Title">A nullable title.</param>
/// <param name="Contact">A nullable contact.</param>
/// <param name="Readme">A nullable readme.</param>
/// <param name="License">A nullable license.</param>
/// <param name="IsReadable">A boolean which indicates if the catalog is accessible.</param>
/// <param name="IsWritable">A boolean which indicates if the catalog is editable.</param>
/// <param name="IsReleased">A boolean which indicates if the catalog is released.</param>
/// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
/// <param name="IsOwner">A boolean which indicates if the catalog is owned by the current user.</param>
/// <param name="PackageReferenceIds">The package reference identifiers.</param>
/// <param name="PipelineInfo">A structure for pipeline info.</param>
public record CatalogInfo(string Id, string? Title, string? Contact, string? Readme, string? License, bool IsReadable, bool IsWritable, bool IsReleased, bool IsVisible, bool IsOwner, IReadOnlyList<Guid> PackageReferenceIds, PipelineInfo PipelineInfo);

/// <summary>
/// A structure for pipeline information.
/// </summary>
/// <param name="Id">The pipeline identifier.</param>
/// <param name="Types">An array of data source types.</param>
/// <param name="InfoUrls">An array of data source info URLs.</param>
public record PipelineInfo(Guid Id, IReadOnlyList<string> Types, IReadOnlyList<string?> InfoUrls);

/// <summary>
/// A catalog time range.
/// </summary>
/// <param name="Begin">The date/time of the first data in the catalog.</param>
/// <param name="End">The date/time of the last data in the catalog.</param>
public record CatalogTimeRange(DateTime Begin, DateTime End);

/// <summary>
/// The catalog availability.
/// </summary>
/// <param name="Data">The actual availability data.</param>
public record CatalogAvailability(IReadOnlyList<double> Data);

/// <summary>
/// A structure for catalog metadata.
/// </summary>
/// <param name="Contact">The contact.</param>
/// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
/// <param name="Overrides">Overrides for the catalog.</param>
public record CatalogMetadata(string? Contact, IReadOnlyList<string>? GroupMemberships, ResourceCatalog? Overrides);

/// <summary>
/// Description of a job.
/// </summary>
/// <param name="Id">The global unique identifier.</param>
/// <param name="Type">The job type.</param>
/// <param name="Owner">The owner of the job.</param>
/// <param name="Parameters">The job parameters.</param>
public record Job(Guid Id, string Type, string Owner, JsonElement? Parameters);

/// <summary>
/// Describes the status of the job.
/// </summary>
/// <param name="Start">The start date/time.</param>
/// <param name="Status">The status.</param>
/// <param name="Progress">The progress from 0 to 1.</param>
/// <param name="ExceptionMessage">The nullable exception message.</param>
/// <param name="Result">The nullable result.</param>
public record JobStatus(DateTime Start, TaskStatus Status, double Progress, string? ExceptionMessage, JsonElement? Result);

/// <summary>
/// 
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// Created
    /// </summary>
    Created,

    /// <summary>
    /// WaitingForActivation
    /// </summary>
    WaitingForActivation,

    /// <summary>
    /// WaitingToRun
    /// </summary>
    WaitingToRun,

    /// <summary>
    /// Running
    /// </summary>
    Running,

    /// <summary>
    /// WaitingForChildrenToComplete
    /// </summary>
    WaitingForChildrenToComplete,

    /// <summary>
    /// RanToCompletion
    /// </summary>
    RanToCompletion,

    /// <summary>
    /// Canceled
    /// </summary>
    Canceled,

    /// <summary>
    /// Faulted
    /// </summary>
    Faulted
}


/// <summary>
/// A structure for export parameters.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="FilePeriod">The file period.</param>
/// <param name="Type">The writer type. If null, data will be read (and possibly cached) but not returned. This is useful for data pre-aggregation.</param>
/// <param name="ResourcePaths">The resource paths to export.</param>
/// <param name="Configuration">The configuration.</param>
public record ExportParameters(DateTime Begin, DateTime End, TimeSpan FilePeriod, string? Type, IReadOnlyList<string> ResourcePaths, IReadOnlyDictionary<string, JsonElement>? Configuration);

/// <summary>
/// A package reference.
/// </summary>
/// <param name="Provider">The provider which loads the package.</param>
/// <param name="Configuration">The configuration of the package reference.</param>
public record PackageReference(string Provider, IReadOnlyDictionary<string, string> Configuration);

/// <summary>
/// An extension description.
/// </summary>
/// <param name="Type">The extension type.</param>
/// <param name="Version">The extension version.</param>
/// <param name="Description">A nullable description.</param>
/// <param name="ProjectUrl">A nullable project website URL.</param>
/// <param name="RepositoryUrl">A nullable source repository URL.</param>
/// <param name="AdditionalInformation">Additional information about the extension.</param>
public record ExtensionDescription(string Type, string Version, string? Description, string? ProjectUrl, string? RepositoryUrl, IReadOnlyDictionary<string, JsonElement> AdditionalInformation);

/// <summary>
/// A data source pipeline.
/// </summary>
/// <param name="Registrations">The list of pipeline elements (data source registrations).</param>
/// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
/// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
public record DataSourcePipeline(IReadOnlyList<DataSourceRegistration> Registrations, string? ReleasePattern, string? VisibilityPattern);

/// <summary>
/// A data source registration.
/// </summary>
/// <param name="Type">The type of the data source.</param>
/// <param name="ResourceLocator">An optional URL which points to the data.</param>
/// <param name="Configuration">Configuration parameters for the instantiated source.</param>
/// <param name="InfoUrl">An optional info URL.</param>
public record DataSourceRegistration(string Type, Uri? ResourceLocator, JsonElement Configuration, string? InfoUrl);

/// <summary>
/// A me response.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="UserName">The user name.</param>
/// <param name="Claims">A map of claims.</param>
/// <param name="PersonalAccessTokens">A list of personal access tokens.</param>
public record MeResponse(string UserId, string UserName, IReadOnlyDictionary<string, NexusClaim> Claims, IReadOnlyDictionary<string, PersonalAccessToken> PersonalAccessTokens);

/// <summary>
/// Represents a claim.
/// </summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public record NexusClaim(string Type, string Value);

/// <summary>
/// A personal access token.
/// </summary>
/// <param name="Description">The token description.</param>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Claims">The claims that will be part of the token.</param>
public record PersonalAccessToken(string Description, DateTime Expires, IReadOnlyList<TokenClaim> Claims);

/// <summary>
/// A revoke token request.
/// </summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public record TokenClaim(string Type, string Value);

/// <summary>
/// Represents a user.
/// </summary>
/// <param name="Name">The user name.</param>
public record NexusUser(string Name);



}
