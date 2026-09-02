// MIT License
// Copyright (c) [2024] [nexus-main]

using System.Net;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using Moq.Protected;
using Nexus.Api.V1;
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
    public async Task CanLoadInterleavedFrames()
    {
        var paths = new[] { "/A/B/C", "/A/B/D" };
        var requests = new List<HttpRequestMessage>();
        var catalogItems = paths.ToDictionary(path => path, path => CreateCatalogItemMap(path)[path]);
        var content = Frame(1, 3, 4).Concat(Frame(0, 1, 2)).ToArray();
        var client = new NexusClient(CreateHttpClient((request, _) =>
        {
            requests.Add(request);
            return request.RequestUri!.AbsolutePath == "/api/v1/catalogs/search-items"
                ? JsonResponse(catalogItems, CreateJsonOptions())
                : BinaryResponse(content);
        }));

        var result = await client.LoadAsync(DateTime.UnixEpoch, DateTime.UnixEpoch.AddSeconds(2), paths);

        Assert.Equal([1d, 2d], result[paths[0]].Values);
        Assert.Equal([3d, 4d], result[paths[1]].Values);
        Assert.Single(requests, current => current.RequestUri!.AbsolutePath == "/api/v2/data");
    }

    [Fact]
    public async Task RejectsInvalidBatchFrame()
    {
        var path = "/A/B/C";
        var catalogItems = CreateCatalogItemMap(path);
        var invalidFrame = Frame(1, 1);
        var client = new NexusClient(CreateHttpClient((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/v1/catalogs/search-items"
                ? JsonResponse(catalogItems, CreateJsonOptions())
                : BinaryResponse(invalidFrame)));

        await Assert.ThrowsAsync<Exception>(() => client.LoadAsync(
            DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1),
            [path]));
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static Dictionary<string, CatalogItem> CreateCatalogItemMap(string resourcePath)
    {
        return new Dictionary<string, CatalogItem>
        {
            [resourcePath] = new CatalogItem(
                new ResourceCatalog("my-catalog", default, default),
                new Resource(resourcePath.Split('/')[^1], default, default),
                new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
                default)
        };
    }

    private static HttpResponseMessage JsonResponse<T>(T value, JsonSerializerOptions options)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, options), Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BinaryResponse(byte[] value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    }

    private static byte[] Frame(int resourceIndex, params double[] values)
    {
        var result = new byte[8 + values.Length * sizeof(double)];
        BinaryPrimitives.WriteInt32LittleEndian(result, resourceIndex);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);

        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(8 + index * sizeof(double)), BitConverter.DoubleToInt64Bits(values[index]));

        return result;
    }
}
