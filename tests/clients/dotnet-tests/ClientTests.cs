// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using Moq.Protected;
using Nexus.Api.V1;
using Nexus.Api.V2;
using Xunit;

namespace Nexus.Api.Tests;

public class ClientTests
{
    public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

    [Fact]
    public async Task CanAddConfiguration()
    {
        // Arrange
        var messageHandlerMock = new Mock<HttpMessageHandler>();
        var catalogId = "my-catalog-id";
        var expectedCatalog = new ResourceCatalog(Id: catalogId, default, default);

        var actualHeaders = new List<IEnumerable<string>?>();

        messageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
            {
                requestMessage.Headers.TryGetValues(NexusConfigurationHeaderKey, out var headers);
                actualHeaders.Add(headers);
            })
            .ReturnsAsync(() =>
            {
                return new HttpResponseMessage()
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
                };
            });

        // -> http client
        var httpClient = new HttpClient(messageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };

        // -> API client
        var client = new NexusClient(httpClient);

        // -> configuration
        var configuration = new
        {
            foo1 = "bar1",
            foo2 = "bar2"
        };

        // Act
        _ = await client.V1.Catalogs.GetAsync(catalogId);

        using (var disposable = client.AttachConfiguration(configuration))
        {
            _ = await client.V1.Catalogs.GetAsync(catalogId);
        }

        _ = await client.V1.Catalogs.GetAsync(catalogId);

        // Assert
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        Assert.Collection(actualHeaders,
            Assert.Null,
            headers =>
            {
                Assert.NotNull(headers);
                var header = Assert.Single(headers);
                Assert.Equal(encodedJson, header);
            },
            Assert.Null);
    }

    [Fact]
    public async Task CanLoadAsyncWithChannelFault()
    {
        // Arrange
        var sessionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var resourcePath = "/A/B/C";
        var faultReason = "The data source could not read the resource.";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var catalogItemMap = new Dictionary<string, CatalogItem>
        {
            [resourcePath] = new CatalogItem(
                new ResourceCatalog("my-catalog", default, default),
                new Resource("C", default, default),
                new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
                default)
        };

        var batchStreamResponse = new BatchStreamResponse(
            sessionId,
            new[] { new BatchStreamChannel(channelId, resourcePath) });

        var faultedStatus = new BatchStreamSessionStatus(
            BatchStreamSessionState.Faulted,
            channelId,
            resourcePath,
            faultReason);

        var httpClient = CreateHttpClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v1/catalogs/search-items")
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(catalogItemMap, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (path == "/api/v2/data/streams/batch")
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(batchStreamResponse, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (path.Contains("/channel/"))
            {
                var content = new ByteArrayContent(Array.Empty<byte>());
                content.Headers.ContentLength = 16;

                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = content
                };
            }
            else if (path.EndsWith("/status"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(faultedStatus, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
        });

        var client = new NexusClient(httpClient);

        // Act
        var ex = await Assert.ThrowsAsync<NexusException>(() => client.LoadAsync(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            new[] { resourcePath }));

        // Assert
        Assert.Equal("N02", ex.StatusCode);
        Assert.Contains(resourcePath, ex.Message);
        Assert.Contains(faultReason, ex.Message);
    }

    [Fact]
    public async Task CanLoadAsyncWithUserCancel()
    {
        // Arrange
        var sessionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var channelId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var resourcePath = "/A/B/C";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var catalogItemMap = new Dictionary<string, CatalogItem>
        {
            [resourcePath] = new CatalogItem(
                new ResourceCatalog("my-catalog", default, default),
                new Resource("C", default, default),
                new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
                default)
        };

        var batchStreamResponse = new BatchStreamResponse(
            sessionId,
            new[] { new BatchStreamChannel(channelId, resourcePath) });

        var cts = new CancellationTokenSource();

        var httpClient = CreateHttpClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v1/catalogs/search-items")
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(catalogItemMap, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (path == "/api/v2/data/streams/batch")
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        JsonSerializer.Serialize(batchStreamResponse, jsonOptions),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (path.Contains("/channel/"))
            {
                cts.Cancel();

                var content = new ByteArrayContent(new byte[16]);
                content.Headers.ContentLength = 16;

                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = content
                };
            }
            else
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
        });

        var client = new NexusClient(httpClient);

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LoadAsync(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 1, 1, 0, 0, 1, DateTimeKind.Utc),
            new[] { resourcePath },
            cancellationToken: cts.Token));
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        var messageHandlerMock = new Mock<HttpMessageHandler>();

        messageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken cancellationToken) =>
                handler(request, cancellationToken));

        return new HttpClient(messageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }
}
