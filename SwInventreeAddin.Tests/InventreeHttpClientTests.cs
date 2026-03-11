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
                Is.EqualTo("/api/part/?IPN=R-10K-0402"));
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
    }

    // ---------------------------------------------------------------------------
    // Stub HTTP handler  returns a fixed status code and body, records the request
    // ---------------------------------------------------------------------------
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public HttpRequestMessage LastRequest { get; private set; }

        public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode   = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
