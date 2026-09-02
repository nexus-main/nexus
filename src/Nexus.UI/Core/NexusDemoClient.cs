// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Net;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nexus.Api;
using Nexus.Api.V1;

namespace Nexus.UI.Core;

public class NexusDemoClient : INexusClient
{
    private static readonly TimeSpan SamplePeriod = TimeSpan.FromMinutes(1);
    private readonly V1 _v1 = new();
    private readonly V2 _v2 = new();

    public IV1 V1 => _v1;

    public Api.V2.IV2 V2 => _v2;

    public void SignIn(string accessToken)
    {
        throw new NotImplementedException();
    }

    public IDisposable AttachConfiguration(object configuration)
    {
        throw new NotImplementedException();
    }

    public void ClearConfiguration()
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<string, DataResponse> Load(
        DateTime begin,
        DateTime end,
        IEnumerable<string> resourcePaths,
        Action<double>? onProgress = default)
    {
        return LoadAsync(begin, end, resourcePaths, onProgress).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyDictionary<string, DataResponse>> LoadAsync(
        DateTime begin,
        DateTime end,
        IEnumerable<string> resourcePaths,
        Action<double>? onProgress = default,
        CancellationToken cancellationToken = default)
    {
        var resourcePathList = resourcePaths.ToList();
        var catalogItemMap = await V1.Catalogs.SearchCatalogItemsAsync(resourcePathList, cancellationToken);
        var session = await V2.Data.RegisterBatchStreamAsync(
            new Api.V2.BatchStreamRequest(begin, end, resourcePathList), cancellationToken);

        var result = new Dictionary<string, DataResponse>();

        for (var i = 0; i < session.Channels.Count; i++)
        {
            var channel = session.Channels[i];
            var response = await V2.Data.GetBatchStreamChannelAsync(session.SessionId, channel.ChannelId, cancellationToken);

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var length = (end - begin).Ticks / SamplePeriod.Ticks;
                var data = new byte[length * 8];
                await stream.ReadExactlyAsync(data, cancellationToken);

                var doubleData = MemoryMarshal.Cast<byte, double>(data).ToArray();
                var catalogItem = catalogItemMap[channel.ResourcePath];
                var resource = catalogItem.Resource;

                onProgress?.Invoke((i + 1) / (double)session.Channels.Count);

                result[channel.ResourcePath] = new DataResponse(
                    CatalogItem: catalogItem,
                    Name: resource.Id,
                    Unit: GetStringProperty(resource, "unit"),
                    Description: GetStringProperty(resource, "description"),
                    SamplePeriod: catalogItem.Representation.SamplePeriod,
                    Values: doubleData);
            }
            finally
            {
                response.Dispose();
            }
        }

        return result;

        static string? GetStringProperty(Resource resource, string name)
        {
            return resource.Properties is not null &&
                resource.Properties.TryGetValue(name, out var value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
    }
}

public class V2 : Api.V2.IV2
{
    private readonly DataV2DemoClient _data = new();

    public Api.V2.IDataClient Data => _data;
}

public class V1 : IV1
{
    private readonly CatalogsDemoClient _catalogs = new();

    public IArtifactsClient Artifacts => throw new NotImplementedException();

    public ICatalogsClient Catalogs => _catalogs;

    public IDataClient Data => new DataDemoClient();

    public IJobsClient Jobs => throw new NotImplementedException();

    public IPackageReferencesClient PackageReferences => throw new NotImplementedException();

    public ISourcesClient Sources => throw new NotImplementedException();

    public ISystemClient System => new SystemDemoClient();

    public IUsersClient Users => new UsersDemoClient();

    public IWritersClient Writers => new WritersDemoClient();
}

public class CatalogsDemoClient : ICatalogsClient
{
    public HttpResponseMessage DeleteAttachment(string catalogId, string attachmentId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> DeleteAttachmentAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public ResourceCatalog Get(string catalogId)
    {
        return GetAsync(catalogId).GetAwaiter().GetResult();
    }

    public Task<ResourceCatalog> GetAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        if (catalogId == "/SAMPLE/LOCAL")
        {
            var properties1 = new Dictionary<string, JsonElement>()
            {
                ["unit"] = JsonSerializer.SerializeToElement("°C"),
                ["groups"] = JsonSerializer.SerializeToElement(new List<string>() { "Environment" }),
                ["description"] = JsonSerializer.SerializeToElement("A description for the temperature resource.")
            };

            var resource1 = new Resource(
                Id: "temperature",
                Properties: properties1,
                Representations: new List<Representation>() { new(NexusDataType.FLOAT64, TimeSpan.FromMinutes(1), default) }
            );

            var properties2 = new Dictionary<string, JsonElement>()
            {
                ["unit"] = JsonSerializer.SerializeToElement("m/s"),
                ["groups"] = JsonSerializer.SerializeToElement(new List<string>() { "Environment" }),
                ["description"] = JsonSerializer.SerializeToElement("A description for the wind speed resource.")
            };

            var resource2 = new Resource(
                Id: "wind_speed",
                Properties: properties2,
                Representations: new List<Representation>() { new(NexusDataType.FLOAT64, TimeSpan.FromMinutes(1), default) }
            );

            var resources = new List<Resource>() { resource1, resource2 };

            var catalog = new ResourceCatalog(
                Id: "/SAMPLE/LOCAL",
                Properties: default,
                Resources: resources
            );

            return Task.FromResult(catalog);
        }

        else
        {
            throw new Exception("This should never happen.");
        }
    }

    public IReadOnlyList<string> GetAttachments(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<string>> GetAttachmentsAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage GetAttachmentStream(string catalogId, string attachmentId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetAttachmentStreamAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public CatalogAvailability GetAvailability(string catalogId, DateTime begin, DateTime end, TimeSpan step)
    {
        throw new NotImplementedException();
    }

    public Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, TimeSpan step, CancellationToken cancellationToken = default)
    {
        var random = new Random();
        var length = (int)((end - begin).Ticks / step.Ticks);

        var result = Enumerable
            .Range(0, length)
            .Select(i => 0.5 + random.NextDouble() / 2)
            .ToList();

        return Task.FromResult(new CatalogAvailability(result));
    }

    public IReadOnlyList<CatalogInfo> GetChildCatalogInfos(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<CatalogInfo>> GetChildCatalogInfosAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        if (catalogId == "/")
        {
            var readme = @"
# Welcome to Nexus!
This is a demo instance running fully within your browser - no server is involved. That's why the list of features is limited here. But what you can do is:

- Open this sample catalog
- Select one or more resources
- Plot their data (random data in this demo)
- Save your current settings to your disk and load them back later

The non-demo version of Nexus allows you to additionally
- export data to different file formats
- edit catalog and resource metadata
- load or export data via Python / C# / Matlab clients
- manage catalog attachments
- ...

We hope you enjoy it!
";

            var catalogInfo = new CatalogInfo(
                Id: "/SAMPLE/LOCAL",
                Title: "Click me to open the sample catalog!",
                Contact: default,
                Readme: readme,
                License: "This is a sample license.",
                IsReadable: true,
                IsWritable: false,
                IsReleased: true,
                IsVisible: true,
                IsOwner: false,
                PackageReferenceIds: [Guid.NewGuid()],
                PipelineInfo: new PipelineInfo(
                    Id: Guid.NewGuid(),
                    Types: ["Nexus.FakeSource"],
                    InfoUrls: [default]
                )
            );

            return Task.FromResult((IReadOnlyList<CatalogInfo>)[catalogInfo]);
        }

        else
        {
            return Task.FromResult((IReadOnlyList<CatalogInfo>)[]);
        }
    }

    public string? GetLicense(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<string?> GetLicenseAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(string));
    }

    public CatalogMetadata GetMetadata(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<CatalogMetadata> GetMetadataAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public CatalogTimeRange GetTimeRange(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CatalogTimeRange(Begin: DateTime.UtcNow.Date.AddYears(-1), End: DateTime.UtcNow.Date));
    }

    public IReadOnlyDictionary<string, CatalogItem> SearchCatalogItems(IReadOnlyList<string> resourcePaths)
    {
        var catalog = Get("/SAMPLE/LOCAL");

        return resourcePaths.ToDictionary(
            resourcePath => resourcePath,
            resourcePath =>
            {
                var parts = resourcePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var resource = catalog.Resources!.Single(current => current.Id == parts[^2]);
                var representation = resource.Representations!.Single(current => current.SamplePeriod == TimeSpan.FromMinutes(1));
                return new CatalogItem(catalog, resource, representation, Parameters: null);
            });
    }

    public Task<IReadOnlyDictionary<string, CatalogItem>> SearchCatalogItemsAsync(IReadOnlyList<string> resourcePaths, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SearchCatalogItems(resourcePaths));
    }

    public HttpResponseMessage SetMetadata(string catalogId, CatalogMetadata metadata)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage UploadAttachment(string catalogId, string attachmentId, Stream content)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> UploadAttachmentAsync(string catalogId, string attachmentId, Stream content, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

public class DataDemoClient : IDataClient
{
    private static readonly TimeSpan SamplePeriod = TimeSpan.FromMinutes(1);

    public HttpResponseMessage GetStream(string resourcePath, DateTime begin, DateTime end)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> GetStreamAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var offset = resourcePath.Contains("temperature")
            ? 7
            : 12;

        var factor = resourcePath.Contains("temperature")
            ? 0.3
            : 3;

        var random = new Random();
        var length = (end - begin).Ticks / SamplePeriod.Ticks;
        var data = new byte[length * 8];
        var doubleData = MemoryMarshal.Cast<byte, double>(data);

        for (int i = 0; i < length; i++)
        {
            doubleData[i] = offset + random.NextDouble() * factor;
        }

        var content = new ByteArrayContent(data);

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        };

        return Task.FromResult(responseMessage);
    }
}

public class DataV2DemoClient : Api.V2.IDataClient
{
    private static readonly TimeSpan SamplePeriod = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<Guid, (Guid SessionId, string ResourcePath, DateTime Begin, DateTime End)> _channelMap = new();

    public Api.V2.BatchStreamResponse RegisterBatchStream(Api.V2.BatchStreamRequest request)
    {
        return RegisterBatchStreamAsync(request).GetAwaiter().GetResult();
    }

    public Task<Api.V2.BatchStreamResponse> RegisterBatchStreamAsync(Api.V2.BatchStreamRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var channels = request.ResourcePaths
            .Select(resourcePath =>
            {
                var channelId = Guid.NewGuid();
                _channelMap[channelId] = (sessionId, resourcePath, request.Begin, request.End);
                return new Api.V2.BatchStreamChannel(channelId, resourcePath);
            })
            .ToArray();

        _ = RemoveAfterTimeoutAsync(channels);
        return Task.FromResult(new Api.V2.BatchStreamResponse(sessionId, channels));

        async Task RemoveAfterTimeoutAsync(Api.V2.BatchStreamChannel[] sessionChannels)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));

            foreach (var channel in sessionChannels)
                _channelMap.TryRemove(channel.ChannelId, out _);
        }
    }

    public HttpResponseMessage GetBatchStreamChannel(Guid sessionId, Guid channelId)
    {
        return GetBatchStreamChannelAsync(sessionId, channelId).GetAwaiter().GetResult();
    }

    public Task<HttpResponseMessage> GetBatchStreamChannelAsync(Guid sessionId, Guid channelId, CancellationToken cancellationToken = default)
    {
        if (!_channelMap.TryGetValue(channelId, out var channel) || channel.SessionId != sessionId)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        _channelMap.TryRemove(channelId, out _);

        var offset = channel.ResourcePath.Contains("temperature")
            ? 7
            : 12;

        var factor = channel.ResourcePath.Contains("temperature")
            ? 0.3
            : 3;

        var random = new Random();
        var length = (channel.End - channel.Begin).Ticks / SamplePeriod.Ticks;
        var data = new byte[length * 8];
        var doubleData = MemoryMarshal.Cast<byte, double>(data);

        for (int i = 0; i < length; i++)
        {
            doubleData[i] = offset + random.NextDouble() * factor;
        }

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };

        return Task.FromResult(responseMessage);
    }

    public Api.V2.BatchStreamSessionStatus GetBatchStreamSessionStatus(Guid sessionId)
    {
        return GetBatchStreamSessionStatusAsync(sessionId).GetAwaiter().GetResult();
    }

    public Task<Api.V2.BatchStreamSessionStatus> GetBatchStreamSessionStatusAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Api.V2.BatchStreamSessionStatus(
            Api.V2.BatchStreamSessionState.Completed,
            FaultedChannelId: null,
            FaultedChannelResourcePath: null,
            FaultReason: null));
    }
}

public class SystemDemoClient : ISystemClient
{
    public IReadOnlyDictionary<string, JsonElement>? GetConfiguration()
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, JsonElement>?> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public string GetDefaultFileType()
    {
        throw new NotImplementedException();
    }

    public Task<string> GetDefaultFileTypeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(default(string)!);
    }

    public string GetHelpLink()
    {
        throw new NotImplementedException();
    }

    public Task<string> GetHelpLinkAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult("https://github.com/nexus-main/nexus");
    }

    public void SetConfiguration(IReadOnlyDictionary<string, JsonElement>? configuration)
    {
        throw new NotImplementedException();
    }

    public Task SetConfigurationAsync(IReadOnlyDictionary<string, JsonElement>? configuration, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}

public class UsersDemoClient : IUsersClient
{
    public HttpResponseMessage AcceptLicense(string catalogId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> AcceptLicenseAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage Authenticate(string scheme, string returnUrl)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Guid CreateClaim(string userId, NexusClaim claim)
    {
        throw new NotImplementedException();
    }

    public Task<Guid> CreateClaimAsync(string userId, NexusClaim claim, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public string CreateToken(PersonalAccessToken token, string? userId = null)
    {
        throw new NotImplementedException();
    }

    public Task<string> CreateTokenAsync(PersonalAccessToken token, string? userId = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public string CreateUser(NexusUser user)
    {
        throw new NotImplementedException();
    }

    public Task<string> CreateUserAsync(NexusUser user, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage DeleteClaim(Guid claimId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> DeleteClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage DeleteToken(Guid tokenId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> DeleteTokenAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage DeleteTokenByValue(string value)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> DeleteTokenByValueAsync(string value, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage DeleteUser(string userId)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<string, NexusClaim> GetClaims(string userId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, NexusClaim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public MeResponse GetMe()
    {
        throw new NotImplementedException();
    }

    public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var meResponse = new MeResponse(
            UserId: "test@nexus",
            new NexusUser("Star Lord", Enumerable.Empty<NexusClaim>().ToList())
        );

        return Task.FromResult(meResponse);
    }

    public IReadOnlyDictionary<string, PersonalAccessToken> GetTokens(string? userId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, PersonalAccessToken>> GetTokensAsync(string? userId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<string, NexusUser> GetUsers()
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public HttpResponseMessage ReAuthenticate()
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseMessage> ReAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void SignOut(string returnUrl)
    {
        throw new NotImplementedException();
    }

    public Task SignOutAsync(string returnUrl, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class WritersDemoClient : IWritersClient
{
    private const string DESCRIPTION = @"
{
  ""label"": ""CSV + Schema (*.csv)"",
  ""options"": {
    ""row-index-format"": {
      ""type"": ""select"",
      ""label"": ""Row index format"",
      ""default"": ""excel"",
      ""items"": {
        ""excel"": ""Excel time"",
        ""index"": ""Index-based"",
        ""unix"": ""Unix time"",
        ""iso-8601"": ""ISO 8601""
      }
    },
    ""significant-figures"": {
      ""type"": ""input-integer"",
      ""label"": ""Significant figures"",
      ""default"": 4,
      ""minimum"": 0,
      ""maximum"": 30
    }
  }
}
        ";

    public IReadOnlyList<ExtensionDescription> GetDescriptions()
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var additionalInformation = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(DESCRIPTION, JsonSerializerOptions.Web)!;

        var description = new ExtensionDescription(
            Type: "Nexus.Writers.Csv",
            Version: "1.0.0",
            Description: "Exports comma-separated values following the frictionless data standard",
            ProjectUrl: "https://github.com/nexus-main/nexus",
            RepositoryUrl: "https://github.com/nexus-main/nexus/blob/master/src/Nexus/Extensions/Writers/Csv.cs",
            AdditionalInformation: additionalInformation
        );

        return Task.FromResult((IReadOnlyList<ExtensionDescription>)[description]);
    }
}
