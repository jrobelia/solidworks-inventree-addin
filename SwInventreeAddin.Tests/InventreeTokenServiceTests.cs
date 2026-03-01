using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    // ── Fake handler lets us control HTTP responses without a real server ──────
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHttpMessageHandler(Exception ex) => _exception = ex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────
    [TestFixture]
    public class InventreeTokenServiceTests
    {
        private const string BaseUrl  = "https://inventree.example.com";
        private const string Username = "engineer";
        private const string Password = "correct-password";

        private static HttpClient MakeClient(HttpMessageHandler handler)
            => new HttpClient(handler);

        [Test]
        public async Task GetTokenAsync_ValidCredentials_ReturnsToken()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"abc123xyz\"}")
                });
            var svc = new InventreeTokenService(MakeClient(handler));

            var token = await svc.GetTokenAsync(BaseUrl, Username, Password);

            Assert.That(token, Is.EqualTo("abc123xyz"));
        }

        [Test]
        public void GetTokenAsync_BadCredentials_ThrowsWithHelpfulMessage()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var svc = new InventreeTokenService(MakeClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync(BaseUrl, Username, "wrong-password"));

            Assert.That(ex.Message, Does.Contain("username or password").IgnoreCase);
        }

        [Test]
        public void GetTokenAsync_NetworkFailure_ThrowsWithHelpfulMessage()
        {
            var handler = new ThrowingHttpMessageHandler(
                new HttpRequestException("Connection refused"));
            var svc = new InventreeTokenService(MakeClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync(BaseUrl, Username, Password));

            Assert.That(ex.Message, Does.Contain("could not reach").IgnoreCase
                .Or.Contains("network").IgnoreCase
                .Or.Contains("connection").IgnoreCase);
        }

        [Test]
        public void GetTokenAsync_MalformedJson_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("this is not json")
                });
            var svc = new InventreeTokenService(MakeClient(handler));

            Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync(BaseUrl, Username, Password));
        }

        [Test]
        public void GetTokenAsync_JsonMissingTokenKey_Throws()
        {
            // Valid JSON but no "token" property — e.g. server returns {"error":"..."}
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            var svc = new InventreeTokenService(MakeClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync(BaseUrl, Username, Password));

            Assert.That(ex.Message, Does.Contain("token").IgnoreCase);
        }

        [Test]
        public void GetTokenAsync_EmptyTokenInResponse_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"\"}")
                });
            var svc = new InventreeTokenService(MakeClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync(BaseUrl, Username, Password));

            Assert.That(ex.Message, Does.Contain("token").IgnoreCase);
        }

        [Test]
        public void GetTokenAsync_HttpUrl_ThrowsBeforeNetworkCall()
        {
            // http:// must be rejected immediately — credentials must never travel over plaintext.
            // The fake handler should never be called; the HTTPS check fires first.
            var handler = new FakeHttpMessageHandler(_ =>
                throw new InvalidOperationException("Handler should not be reached"));
            var svc = new InventreeTokenService(MakeClient(handler));

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.GetTokenAsync("http://inventree.example.com", Username, Password));

            Assert.That(ex.Message, Does.Contain("https").IgnoreCase);
        }
    }
}
