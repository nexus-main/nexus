using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nexus.Api;

namespace Nexus.UI.Core;

public class NexusDemoClient : INexusClient
{
    public IArtifactsClient Artifacts => throw new NotImplementedException();

    public ICatalogsClient Catalogs => new CatalogsDemoClient();

    public IDataClient Data => new DataDemoClient();

    public IJobsClient Jobs => throw new NotImplementedException();

    public IPackageReferencesClient PackageReferences => throw new NotImplementedException();

    public ISourcesClient Sources => throw new NotImplementedException();

    public ISystemClient System => new SystemDemoClient();

    public IUsersClient Users => new UsersDemoClient();

    public IWritersClient Writers => new WritersDemoClient();

    public IDisposable AttachConfiguration(object configuration)
    {
        throw new NotImplementedException();
    }

    public void ClearConfiguration()
    {
        throw new NotImplementedException();
    }

    public void SignIn(string accessToken)
    {
        throw new NotImplementedException();
    }
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
        throw new NotImplementedException();
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
                DataSourceInfoUrl: default,
                DataSourceType: "Nexus.FakeSource",
                DataSourceRegistrationId: Guid.NewGuid(),
                PackageReferenceId: Guid.NewGuid()
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
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, CatalogItem>> SearchCatalogItemsAsync(IReadOnlyList<string> resourcePaths, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public void SetMetadata(string catalogId, CatalogMetadata metadata)
    {
        throw new NotImplementedException();
    }

    public Task SetMetadataAsync(string catalogId, CatalogMetadata metadata, CancellationToken cancellationToken = default)
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
        var length = (end - begin).Ticks / TimeSpan.FromSeconds(1).Ticks;
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
        var user = new NexusUser(
            Name: "Star Lord"
        );

        var meResponse = new MeResponse(
            UserId: "test@nexus",
            User: user,
            IsAdmin: false,
            PersonalAccessTokens: new Dictionary<string, PersonalAccessToken>()
        );

        return Task.FromResult(meResponse);
    }

    public IReadOnlyDictionary<string, PersonalAccessToken> GetTokens(string userId)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyDictionary<string, PersonalAccessToken>> GetTokensAsync(string userId, CancellationToken cancellationToken = default)
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
        var additionalInformation = JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>?>(DESCRIPTION);

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