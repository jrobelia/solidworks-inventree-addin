using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeHttpClientTests
    {
        private const string BaseUrl = "http://inventree.example.com";
        private const string ApiKey  = "test-api-key";

        private static readonly string ValidSinglePartJson =
            @"[{ ""name"": ""Resistor 10k"", ""notes"": ""SMD 0402"", ""revision"": ""A"", ""IPN"": ""R-10K-0402"" }]";

        private static readonly string ValidServerInfoJson =
            @"{ ""server"": ""InvenTree"", ""version"": ""0.17.0"", ""apiVersion"": 117 }";

        private static InventreeHttpClient CreateClient(StubHttpMessageHandler handler)
        {
            var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
            return new InventreeHttpClient(http, ApiKey);
        }

        // --- successful response ---

        [Test]
        public async Task GetPartByIpnAsync_ValidResponse_ReturnsCorrectName()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidSinglePartJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part.Name, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task GetPartByIpnAsync_ValidResponse_ReturnsCorrectNotes()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidSinglePartJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part.Notes, Is.EqualTo("SMD 0402"));
        }

        [Test]
        public async Task GetPartByIpnAsync_ValidResponse_ReturnsCorrectRevision()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidSinglePartJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part.Revision, Is.EqualTo("A"));
        }

        // --- empty array => null ---

        [Test]
        public async Task GetPartByIpnAsync_EmptyArray_ReturnsNull()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            var part = await CreateClient(handler).GetPartByIpnAsync("NOTFOUND");
            Assert.That(part, Is.Null);
        }

        // --- error status codes ---

        [Test]
        public void GetPartByIpnAsync_UnauthorizedResponse_ThrowsHttpRequestException()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "Unauthorized");
            Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).GetPartByIpnAsync("R-10K-0402"));
        }

        [Test]
        public void GetPartByIpnAsync_ServerError_ThrowsHttpRequestException()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "Server Error");
            Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).GetPartByIpnAsync("R-10K-0402"));
        }

        // --- headers and URL ---

        [Test]
        public async Task GetPartByIpnAsync_SendsTokenAuthorizationHeader()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");

            var auth = handler.LastRequest.Headers.Authorization;
            Assert.That(auth,            Is.Not.Null,           "Authorization header must be present");
            Assert.That(auth.Scheme,     Is.EqualTo("Token"),   "Scheme must be 'Token'");
            Assert.That(auth.Parameter,  Is.EqualTo(ApiKey),    "Parameter must equal the api key");
        }

        [Test]
        public async Task GetPartByIpnAsync_ConstructsCorrectRequestUrl()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");

            Assert.That(handler.LastRequest.RequestUri.PathAndQuery,
                Is.EqualTo("/api/part/?IPN=R-10K-0402&limit=0"));
        }

        // --- GetServerInfoAsync ---

        [Test]
        public async Task GetServerInfoAsync_ValidResponse_ReturnsServerVersion()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidServerInfoJson);
            var info = await CreateClient(handler).GetServerInfoAsync();
            Assert.That(info.ServerVersion, Is.EqualTo("0.17.0"));
        }

        [Test]
        public async Task GetServerInfoAsync_ValidResponse_ReturnsApiVersion()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidServerInfoJson);
            var info = await CreateClient(handler).GetServerInfoAsync();
            Assert.That(info.ApiVersion, Is.EqualTo(117));
        }

        [Test]
        public async Task GetServerInfoAsync_ConstructsCorrectRequestUrl()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, ValidServerInfoJson);
            await CreateClient(handler).GetServerInfoAsync();
            Assert.That(handler.LastRequest.RequestUri.PathAndQuery, Is.EqualTo("/api/"));
        }

        [Test]
        public void GetServerInfoAsync_ServerError_ThrowsHttpRequestException()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
            Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).GetServerInfoAsync());
        }

        // --- GetPartWebUrl ---

        [Test]
        public void GetPartWebUrl_KnownPk_ReturnsPartUrl()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            var url = CreateClient(handler).GetPartWebUrl(42);
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Is.EqualTo("http://inventree.example.com/part/42/"));
        }

        [Test]
        public void GetPartWebUrl_BaseAddressWithTrailingSlash_ReturnsNormalizedUrl()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl + "/") };
            var client = new InventreeHttpClient(http, ApiKey);
            var url = client.GetPartWebUrl(42);
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Is.EqualTo("http://inventree.example.com/part/42/"));
        }

        [Test]
        public void GetPartWebUrl_NoBaseAddress_ReturnsNull()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            var http = new HttpClient(handler) { BaseAddress = null };
            var client = new InventreeHttpClient(http, ApiKey);
            var url = client.GetPartWebUrl(42);
            Assert.That(url, Is.Null);
        }

        [Test]
        public void GetPartWebUrl_BaseAddressWithEncodedPath_PreservesPercentEncoding()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            var http = new HttpClient(handler) { BaseAddress = new Uri("http://inventree.example.com/path%20with%20space/") };
            var client = new InventreeHttpClient(http, ApiKey);
            var url = client.GetPartWebUrl(42);
            Assert.That(url, Is.Not.Null);
            Assert.That(url!.AbsoluteUri, Is.EqualTo("http://inventree.example.com/path%20with%20space/part/42/"));
        }
    }

    // ---------------------------------------------------------------------------
    // Task 4-A: FetchDetailAsync parses in_stock / ordering / active
    // ---------------------------------------------------------------------------
    [TestFixture]
    public class InventreeHttpClientStockFieldTests
    {
        private const string BaseUrl = "http://inventree.example.com";
        private const string ApiKey  = "test-api-key";

        // List response — just enough to get a PK back
        private const string ListJson = @"[{ ""pk"": 42, ""IPN"": ""R-10K-0402"" }]";

        private static InventreeHttpClient CreateClient(MultiResponseStubHttpHandler handler)
        {
            var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
            return new InventreeHttpClient(http, ApiKey);
        }

        [Test]
        public async Task GetPartByIpnAsync_ParsesInStock()
        {
            var detailJson = @"{ ""pk"": 42, ""name"": ""R"", ""description"": """", ""notes"": """", ""revision"": """", ""IPN"": ""R-10K-0402"", ""in_stock"": 15.5, ""ordering"": 0, ""active"": true }";
            var handler = new MultiResponseStubHttpHandler(ListJson, detailJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part!.InStock, Is.EqualTo(15.5m));
        }

        [Test]
        public async Task GetPartByIpnAsync_ParsesOrdering()
        {
            var detailJson = @"{ ""pk"": 42, ""name"": ""R"", ""description"": """", ""notes"": """", ""revision"": """", ""IPN"": ""R-10K-0402"", ""in_stock"": 0, ""ordering"": 100, ""active"": true }";
            var handler = new MultiResponseStubHttpHandler(ListJson, detailJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part!.Ordering, Is.EqualTo(100m));
        }

        [Test]
        public async Task GetPartByIpnAsync_ParsesActive()
        {
            var detailJson = @"{ ""pk"": 42, ""name"": ""R"", ""description"": """", ""notes"": """", ""revision"": """", ""IPN"": ""R-10K-0402"", ""in_stock"": 0, ""ordering"": 0, ""active"": false }";
            var handler = new MultiResponseStubHttpHandler(ListJson, detailJson);
            var part = await CreateClient(handler).GetPartByIpnAsync("R-10K-0402");
            Assert.That(part!.Active, Is.False);
        }
    }

    // ---------------------------------------------------------------------------
    // Stub that returns responses from a queue — first call gets responses[0], etc.
    // ---------------------------------------------------------------------------
    internal sealed class MultiResponseStubHttpHandler : HttpMessageHandler
    {
        private readonly System.Collections.Generic.Queue<string> _bodies;

        public MultiResponseStubHttpHandler(params string[] bodies)
        {
            _bodies = new System.Collections.Generic.Queue<string>(bodies);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = _bodies.Count > 0 ? _bodies.Dequeue() : "{}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    // ---------------------------------------------------------------------------
    // Stub HTTP handler  returns a fixed status code and body, records the request
    // ---------------------------------------------------------------------------
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage LastRequest { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode   = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
