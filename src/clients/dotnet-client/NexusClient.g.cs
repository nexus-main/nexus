#nullable enable

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Api;

/// <summary>
/// A client for the Nexus system.
/// </summary>
public interface INexusClient
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



    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <returns>A task.</returns>
    void SignIn(string refreshToken);

    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>A task.</returns>
    Task SignInAsync(string refreshToken, CancellationToken cancellationToken);

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

    private static string _tokenFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "", "tokens");
    private static SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(initialCount: 1, maxCount: 1);

    private TokenPair? _tokenPair;
    private string? _tokenFilePath;
    private HttpClient _httpClient;

    private ArtifactsClient _artifacts;
    private CatalogsClient _catalogs;
    private DataClient _data;
    private JobsClient _jobs;
    private PackageReferencesClient _packageReferences;
    private SourcesClient _sources;
    private SystemClient _system;
    private UsersClient _users;
    private WritersClient _writers;

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

        _httpClient = httpClient;

        _artifacts = new ArtifactsClient(this);
        _catalogs = new CatalogsClient(this);
        _data = new DataClient(this);
        _jobs = new JobsClient(this);
        _packageReferences = new PackageReferencesClient(this);
        _sources = new SourcesClient(this);
        _system = new SystemClient(this);
        _users = new UsersClient(this);
        _writers = new WritersClient(this);

    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _tokenPair is not null;

    /// <inheritdoc />
    public IArtifactsClient Artifacts => _artifacts;

    /// <inheritdoc />
    public ICatalogsClient Catalogs => _catalogs;

    /// <inheritdoc />
    public IDataClient Data => _data;

    /// <inheritdoc />
    public IJobsClient Jobs => _jobs;

    /// <inheritdoc />
    public IPackageReferencesClient PackageReferences => _packageReferences;

    /// <inheritdoc />
    public ISourcesClient Sources => _sources;

    /// <inheritdoc />
    public ISystemClient System => _system;

    /// <inheritdoc />
    public IUsersClient Users => _users;

    /// <inheritdoc />
    public IWritersClient Writers => _writers;



    /// <inheritdoc />
    public void SignIn(string refreshToken)
    {
        string actualRefreshToken;

        var byteHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        var refreshTokenHash = BitConverter.ToString(byteHash).Replace("-", "");
        _tokenFilePath = Path.Combine(_tokenFolderPath, refreshTokenHash + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            actualRefreshToken = File.ReadAllText(_tokenFilePath);
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, refreshToken);
            actualRefreshToken = refreshToken;
        }

        RefreshToken(actualRefreshToken);
    }

    /// <inheritdoc />
    public async Task SignInAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        string actualRefreshToken;

        var byteHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        var refreshTokenHash = BitConverter.ToString(byteHash).Replace("-", "");
        _tokenFilePath = Path.Combine(_tokenFolderPath, refreshTokenHash + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            actualRefreshToken = File.ReadAllText(_tokenFilePath);
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, refreshToken);
            actualRefreshToken = refreshToken;
        }

        await RefreshTokenAsync(actualRefreshToken, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IDisposable AttachConfiguration(object configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        _httpClient.DefaultRequestHeaders.Remove(ConfigurationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(ConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <inheritdoc />
    public void ClearConfiguration()
    {
        _httpClient.DefaultRequestHeaders.Remove(ConfigurationHeaderKey);
    }

    internal T Invoke<T>(string method, string relativeUrl, string? acceptHeaderValue, string? contentTypeValue, HttpContent? content)
    {
        // prepare request
        using var request = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);

        // send request
        var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized && _tokenPair is not null)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        try
                        {
                            RefreshToken(_tokenPair.RefreshToken);

                            using var newRequest = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);
                            var newResponse = _httpClient.Send(newRequest, HttpCompletionOption.ResponseHeadersRead);

                            if (newResponse is not null)
                            {
                                response.Dispose();
                                response = newResponse;
                                signOut = false;
                            }
                        }
                        catch
                        {
                            //
                        }
                    }
                }

                if (signOut)
                    SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
                var statusCode = $"N00.{(int)response.StatusCode}";

                if (string.IsNullOrWhiteSpace(message))
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

                else
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
            }
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
                    throw new NexusException("N01", "Response data could not be deserialized.", ex);
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
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized && _tokenPair is not null)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        try
                        {
                            await RefreshTokenAsync(_tokenPair.RefreshToken, cancellationToken).ConfigureAwait(false);

                            using var newRequest = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);
                            var newResponse = await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                            if (newResponse is not null)
                            {
                                response.Dispose();
                                response = newResponse;
                                signOut = false;
                            }
                        }
                        catch
                        {
                            //
                        }
                    }
                }

                if (signOut)
                    SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var statusCode = $"N00.{(int)response.StatusCode}";

                if (string.IsNullOrWhiteSpace(message))
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

                else
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
            }
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
                    throw new NexusException("N01", "Response data could not be deserialized.", ex);
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

    private void RefreshToken(string refreshToken)
    {
        _semaphoreSlim.Wait();

        try
        {
            // make sure the refresh token has not already been redeemed
            if (_tokenPair is not null && refreshToken != _tokenPair.RefreshToken)
                return;

            // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

            var refreshRequest = new RefreshTokenRequest(refreshToken);
            var tokenPair = Users.RefreshToken(refreshRequest);

            if (_tokenFilePath is not null)
            {
                Directory.CreateDirectory(_tokenFolderPath);
                File.WriteAllText(_tokenFilePath, tokenPair.RefreshToken);
            }

            var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
            _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
            _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

            _tokenPair = tokenPair;

        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

        try
        {
            // make sure the refresh token has not already been redeemed
            if (_tokenPair is not null && refreshToken != _tokenPair.RefreshToken)
                return;

            // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

            var refreshRequest = new RefreshTokenRequest(refreshToken);
            var tokenPair = await Users.RefreshTokenAsync(refreshRequest, cancellationToken).ConfigureAwait(false);

            if (_tokenFilePath is not null)
            {
                Directory.CreateDirectory(_tokenFolderPath);
                File.WriteAllText(_tokenFilePath, tokenPair.RefreshToken);
            }

            var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
            _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
            _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

            _tokenPair = tokenPair;

        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _tokenPair = default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient?.Dispose();
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
        var catalogItemMap = Catalogs.SearchCatalogItems(resourcePaths.ToList());
        var result = new Dictionary<string, DataResponse>();
        var progress = 0.0;

        foreach (var (resourcePath, catalogItem) in catalogItemMap)
        {
            using var responseMessage = Data.GetStream(resourcePath, begin, end);

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
        var catalogItemMap = await Catalogs.SearchCatalogItemsAsync(resourcePaths.ToList()).ConfigureAwait(false);
        var result = new Dictionary<string, DataResponse>();
        var progress = 0.0;

        foreach (var (resourcePath, catalogItem) in catalogItemMap)
        {
            using var responseMessage = await Data.GetStreamAsync(resourcePath, begin, end, cancellationToken).ConfigureAwait(false);
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

        var exportParameters = new ExportParameters(
            begin,
            end,
            filePeriod,
            fileFormat,
            resourcePaths.ToList(),
            actualConfiguration);

        // Start Job
        var job = Jobs.Export(exportParameters);

        // Wait for job to finish
        string? artifactId = default;

        while (true)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));

            var jobStatus = Jobs.GetJobStatus(job.Id);

            if (jobStatus.Status == TaskStatus.Canceled)
                throw new OperationCanceledException("The job has been cancelled.");

            else if (jobStatus.Status == TaskStatus.Faulted)
                throw new OperationCanceledException($"The job has failed. Reason: {jobStatus.ExceptionMessage}");

            else if (jobStatus.Status == TaskStatus.RanToCompletion)
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
        var responseMessage = Artifacts.Download(artifactId);
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

        var exportParameters = new ExportParameters(
            begin,
            end,
            filePeriod,
            fileFormat,
            resourcePaths.ToList(),
            actualConfiguration);

        // Start Job
        var job = await Jobs.ExportAsync(exportParameters).ConfigureAwait(false);

        // Wait for job to finish
        string? artifactId = default;

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            var jobStatus = await Jobs.GetJobStatusAsync(job.Id, cancellationToken).ConfigureAwait(false);

            if (jobStatus.Status == TaskStatus.Canceled)
                throw new OperationCanceledException("The job has been cancelled.");

            else if (jobStatus.Status == TaskStatus.Faulted)
                throw new OperationCanceledException($"The job has failed. Reason: {jobStatus.ExceptionMessage}");

            else if (jobStatus.Status == TaskStatus.RanToCompletion)
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
        var responseMessage = await Artifacts.DownloadAsync(artifactId, cancellationToken).ConfigureAwait(false);
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
    void SetMetadata(string catalogId, CatalogMetadata metadata);

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="metadata">The catalog metadata to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default);

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
    public void SetMetadata(string catalogId, CatalogMetadata metadata)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        ___client.Invoke<object>("PUT", __url, default, "application/json", JsonContent.Create(metadata, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        __urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(catalogId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<object>("PUT", __url, default, "application/json", JsonContent.Create(metadata, options: Utilities.JsonOptions), cancellationToken);
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
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    void Delete(Guid packageReferenceId);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task DeleteAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    IReadOnlyList<string> GetVersions(Guid packageReferenceId);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<string>> GetVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

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
    public void Delete(Guid packageReferenceId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}");
        __urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        ___client.Invoke<object>("DELETE", __url, default, default, default);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}");
        __urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<object>("DELETE", __url, default, default, default, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetVersions(Guid packageReferenceId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}/versions");
        __urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<string>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}/versions");
        __urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

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
    /// Gets the list of data source registrations.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    IReadOnlyDictionary<string, DataSourceRegistration> GetRegistrations(string? userId = default);

    /// <summary>
    /// Gets the list of data source registrations.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, DataSourceRegistration>> GetRegistrationsAsync(string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a data source registration.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="registration">The registration to create.</param>
    Guid CreateRegistration(DataSourceRegistration registration, string? userId = default);

    /// <summary>
    /// Creates a data source registration.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="registration">The registration to create.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Guid> CreateRegistrationAsync(DataSourceRegistration registration, string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a data source registration.
    /// </summary>
    /// <param name="registrationId">The identifier of the registration.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    HttpResponseMessage DeleteRegistration(Guid registrationId, string? userId = default);

    /// <summary>
    /// Deletes a data source registration.
    /// </summary>
    /// <param name="registrationId">The identifier of the registration.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteRegistrationAsync(Guid registrationId, string? userId = default, CancellationToken cancellationToken = default);

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
    public IReadOnlyDictionary<string, DataSourceRegistration> GetRegistrations(string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, DataSourceRegistration>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, DataSourceRegistration>> GetRegistrationsAsync(string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, DataSourceRegistration>>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Guid CreateRegistration(DataSourceRegistration registration, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(registration, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<Guid> CreateRegistrationAsync(DataSourceRegistration registration, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations");

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<Guid>("POST", __url, "application/json", "application/json", JsonContent.Create(registration, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage DeleteRegistration(Guid registrationId, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations/{registrationId}");
        __urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)!));

        var __queryValues = new Dictionary<string, string>();

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteRegistrationAsync(Guid registrationId, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/sources/registrations/{registrationId}");
        __urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)!));

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

    /// <summary>
    /// Gets the system configuration.
    /// </summary>
    IReadOnlyDictionary<string, JsonElement>? GetConfiguration();

    /// <summary>
    /// Gets the system configuration.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, JsonElement>?> GetConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the system configuration.
    /// </summary>
    /// <param name="configuration"></param>
    void SetConfiguration(IReadOnlyDictionary<string, JsonElement>? configuration);

    /// <summary>
    /// Sets the system configuration.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SetConfigurationAsync(IReadOnlyDictionary<string, JsonElement>? configuration, CancellationToken cancellationToken = default);

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

    /// <inheritdoc />
    public IReadOnlyDictionary<string, JsonElement>? GetConfiguration()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/configuration");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, JsonElement>?>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, JsonElement>?> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/configuration");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, JsonElement>?>("GET", __url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public void SetConfiguration(IReadOnlyDictionary<string, JsonElement>? configuration)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/configuration");

        var __url = __urlBuilder.ToString();
        ___client.Invoke<object>("PUT", __url, default, "application/json", JsonContent.Create(configuration, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task SetConfigurationAsync(IReadOnlyDictionary<string, JsonElement>? configuration, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/system/configuration");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<object>("PUT", __url, default, "application/json", JsonContent.Create(configuration, options: Utilities.JsonOptions), cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with users.
/// </summary>
public interface IUsersClient
{
    /// <summary>
    /// Returns a list of available authentication schemes.
    /// </summary>
    IReadOnlyList<AuthenticationSchemeDescription> GetAuthenticationSchemes();

    /// <summary>
    /// Returns a list of available authentication schemes.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyList<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default);

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
    /// Refreshes the JWT token.
    /// </summary>
    /// <param name="request">The refresh token request.</param>
    TokenPair RefreshToken(RefreshTokenRequest request);

    /// <summary>
    /// Refreshes the JWT token.
    /// </summary>
    /// <param name="request">The refresh token request.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<TokenPair> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    /// <param name="request">The revoke token request.</param>
    HttpResponseMessage RevokeToken(RevokeTokenRequest request);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    /// <param name="request">The revoke token request.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);

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
    /// Generates a refresh token.
    /// </summary>
    /// <param name="description">The refresh token description.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    string GenerateRefreshToken(string description, string? userId = default);

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    /// <param name="description">The refresh token description.</param>
    /// <param name="userId">The optional user identifier. If not specified, the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> GenerateRefreshTokenAsync(string description, string? userId = default, CancellationToken cancellationToken = default);

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
    /// Deletes a refresh token.
    /// </summary>
    /// <param name="tokenId">The identifier of the refresh token.</param>
    HttpResponseMessage DeleteRefreshToken(Guid tokenId);

    /// <summary>
    /// Deletes a refresh token.
    /// </summary>
    /// <param name="tokenId">The identifier of the refresh token.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<HttpResponseMessage> DeleteRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken = default);

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
    /// Gets all refresh tokens.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    IReadOnlyDictionary<string, RefreshToken> GetRefreshTokens(string userId);

    /// <summary>
    /// Gets all refresh tokens.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IReadOnlyDictionary<string, RefreshToken>> GetRefreshTokensAsync(string userId, CancellationToken cancellationToken = default);

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
    public IReadOnlyList<AuthenticationSchemeDescription> GetAuthenticationSchemes()
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/authentication-schemes");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyList<AuthenticationSchemeDescription>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/authentication-schemes");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyList<AuthenticationSchemeDescription>>("GET", __url, "application/json", default, default, cancellationToken);
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
    public TokenPair RefreshToken(RefreshTokenRequest request)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/refresh");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<TokenPair>("POST", __url, "application/json", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<TokenPair> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/refresh");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<TokenPair>("POST", __url, "application/json", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public HttpResponseMessage RevokeToken(RevokeTokenRequest request)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/revoke");

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("POST", __url, "application/octet-stream", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions));
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/revoke");

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("POST", __url, "application/octet-stream", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions), cancellationToken);
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
    public string GenerateRefreshToken(string description, string? userId = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/generate");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["description"] = Uri.EscapeDataString(description);

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<string>("POST", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<string> GenerateRefreshTokenAsync(string description, string? userId = default, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/generate");

        var __queryValues = new Dictionary<string, string>();

        __queryValues["description"] = Uri.EscapeDataString(description);

        if (userId is not null)
            __queryValues["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!);

        var __query = "?" + string.Join('&', __queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        __urlBuilder.Append(__query);

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<string>("POST", __url, "application/json", default, default, cancellationToken);
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
    public HttpResponseMessage DeleteRefreshToken(Guid tokenId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/{tokenId}");
        __urlBuilder.Replace("{tokenId}", Uri.EscapeDataString(Convert.ToString(tokenId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteRefreshTokenAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/tokens/{tokenId}");
        __urlBuilder.Replace("{tokenId}", Uri.EscapeDataString(Convert.ToString(tokenId, CultureInfo.InvariantCulture)!));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<HttpResponseMessage>("DELETE", __url, "application/octet-stream", default, default, cancellationToken);
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
    public IReadOnlyDictionary<string, RefreshToken> GetRefreshTokens(string userId)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/tokens");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.Invoke<IReadOnlyDictionary<string, RefreshToken>>("GET", __url, "application/json", default, default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, RefreshToken>> GetRefreshTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        var __urlBuilder = new StringBuilder();
        __urlBuilder.Append("/api/v1/users/{userId}/tokens");
        __urlBuilder.Replace("{userId}", Uri.EscapeDataString(userId));

        var __url = __urlBuilder.ToString();
        return ___client.InvokeAsync<IReadOnlyDictionary<string, RefreshToken>>("GET", __url, "application/json", default, default, cancellationToken);
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
/// <param name="DataSourceInfoUrl">A nullable info URL of the data source.</param>
/// <param name="DataSourceType">The data source type.</param>
/// <param name="DataSourceRegistrationId">The data source registration identifier.</param>
/// <param name="PackageReferenceId">The package reference identifier.</param>
public record CatalogInfo(string Id, string? Title, string? Contact, string? Readme, string? License, bool IsReadable, bool IsWritable, bool IsReleased, bool IsVisible, bool IsOwner, string? DataSourceInfoUrl, string DataSourceType, Guid DataSourceRegistrationId, Guid PackageReferenceId);

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
public record ExtensionDescription(string Type, string Version, string? Description, string? ProjectUrl, string? RepositoryUrl, IReadOnlyDictionary<string, JsonElement>? AdditionalInformation);

/// <summary>
/// A data source registration.
/// </summary>
/// <param name="Type">The type of the data source.</param>
/// <param name="ResourceLocator">An optional URL which points to the data.</param>
/// <param name="Configuration">Configuration parameters for the instantiated source.</param>
/// <param name="InfoUrl">An optional info URL.</param>
/// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
/// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
public record DataSourceRegistration(string Type, Uri? ResourceLocator, IReadOnlyDictionary<string, JsonElement>? Configuration, string? InfoUrl, string? ReleasePattern, string? VisibilityPattern);

/// <summary>
/// Describes an OpenID connect provider.
/// </summary>
/// <param name="Scheme">The scheme.</param>
/// <param name="DisplayName">The display name.</param>
public record AuthenticationSchemeDescription(string Scheme, string DisplayName);

/// <summary>
/// A token pair.
/// </summary>
/// <param name="AccessToken">The JWT token.</param>
/// <param name="RefreshToken">The refresh token.</param>
public record TokenPair(string AccessToken, string RefreshToken);

/// <summary>
/// A refresh token request.
/// </summary>
/// <param name="RefreshToken">The refresh token.</param>
public record RefreshTokenRequest(string RefreshToken);

/// <summary>
/// A revoke token request.
/// </summary>
/// <param name="RefreshToken">The refresh token.</param>
public record RevokeTokenRequest(string RefreshToken);

/// <summary>
/// A me response.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="User">The user.</param>
/// <param name="IsAdmin">A boolean which indicates if the user is an administrator.</param>
/// <param name="RefreshTokens">A list of currently present refresh tokens.</param>
public record MeResponse(string UserId, NexusUser User, bool IsAdmin, IReadOnlyDictionary<string, RefreshToken> RefreshTokens);

/// <summary>
/// Represents a user.
/// </summary>
/// <param name="Name">The user name.</param>
public record NexusUser(string Name);

/// <summary>
/// A refresh token.
/// </summary>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Description">The token description.</param>
public record RefreshToken(DateTime Expires, string Description);

/// <summary>
/// Represents a claim.
/// </summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public record NexusClaim(string Type, string Value);



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
    CatalogItem CatalogItem, 
    string? Name,
    string? Unit,
    string? Description,
    TimeSpan SamplePeriod,
    double[] Values);
