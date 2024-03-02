using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using Moq.Protected;
using Xunit;

namespace Nexus.Api.Tests
{
    public class ClientTests
    {
        public const string NexusConfigurationHeaderKey = "Nexus-Configuration";

        [Fact]
        public async Task CanAuthenticateAndRefreshAsync()
        {
            // Arrange
            var messageHandlerMock = new Mock<HttpMessageHandler>();
            var refreshToken = Guid.NewGuid().ToString();

            // -> refresh token 1
            var refreshTokenTryCount = 0;

            var tokenPair1 = new TokenPair(
                AccessToken: "111",
                RefreshToken: "222"
            );

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().EndsWith("tokens/refresh") && refreshTokenTryCount == 0),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var refreshTokenRequest = JsonSerializer.Deserialize<RefreshTokenRequest>(requestMessage.Content!.ReadAsStream(cancellationToken));
                    Assert.Equal(refreshToken, refreshTokenRequest!.RefreshToken);
                    refreshTokenTryCount++;
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(tokenPair1), Encoding.UTF8, "application/json")
                });

            // -> get catalogs (1st try)
            var catalogTryCount = 0;

            var catalogsResponseMessage1 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.Unauthorized
            };

            catalogsResponseMessage1.Headers.Add("WWW-Authenticate", "Bearer The token expired at ...");

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("catalogs") && catalogTryCount == 0),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var actual = requestMessage.Headers.Authorization!;
                    Assert.Equal($"Bearer {tokenPair1.AccessToken}", $"{actual.Scheme} {actual.Parameter}");
                    catalogsResponseMessage1.RequestMessage = requestMessage;
                    catalogTryCount++;
                })
                .ReturnsAsync(catalogsResponseMessage1);

            // -> refresh token 2
            var tokenPair2 = new TokenPair(
                AccessToken: "333",
                RefreshToken: "444"
            );

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().EndsWith("tokens/refresh") && refreshTokenTryCount == 1),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var refreshTokenRequest = JsonSerializer.Deserialize<RefreshTokenRequest>(requestMessage.Content!.ReadAsStream(cancellationToken));
                    Assert.Equal(tokenPair1.RefreshToken, refreshTokenRequest!.RefreshToken);
                })
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(tokenPair2), Encoding.UTF8, "application/json")
                });

            // -> get catalogs (2nd try)
            var catalogId = "my-catalog-id";
            var expectedCatalog = new ResourceCatalog(Id: catalogId, default, default);

            var catalogsResponseMessage2 = new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(expectedCatalog), Encoding.UTF8, "application/json"),
            };

            messageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(x => x.RequestUri!.ToString().Contains("catalogs") && catalogTryCount == 1),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((requestMessage, cancellationToken) =>
                {
                    var actual = requestMessage.Headers.Authorization!;
                    Assert.Equal($"Bearer {tokenPair2.AccessToken}", $"{actual.Scheme} {actual.Parameter}");
                })
                .ReturnsAsync(catalogsResponseMessage2);

            // -> http client
            var httpClient = new HttpClient(messageHandlerMock.Object)
            {
                BaseAddress = new Uri("http://localhost")
            };

            // -> API client
            var client = new NexusClient(httpClient);

            // Act
            await client.SignInAsync(refreshToken);
            var actualCatalog = await client.Catalogs.GetAsync(catalogId);

            // Assert
            Assert.Equal(
                JsonSerializer.Serialize(expectedCatalog),
                JsonSerializer.Serialize(actualCatalog));
        }

        [Fact]
        public async Task CanAddConfigurationAsync()
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
            _ = await client.Catalogs.GetAsync(catalogId);

            using (var disposable = client.AttachConfiguration(configuration))
            {
                _ = await client.Catalogs.GetAsync(catalogId);
            }

            _ = await client.Catalogs.GetAsync(catalogId);

            // Assert
            var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

            Assert.Collection(actualHeaders,
                headers => Assert.Null(headers),
                headers => {
                    Assert.NotNull(headers);
                    Assert.Collection(headers, header => Assert.Equal(encodedJson, header));
                },
                headers => Assert.Null(headers));
        }
    }
}
