using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeHttpClientBomTests
    {
        private const string BaseUrl = "http://inventree.example.com";
        private const string ApiKey  = "test-api-key";

        private class SingleStub : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public SingleStub(HttpStatusCode status, string body) { _status = status; _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(_status)
                { Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json") });
        }

        private class SequentialStub : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode status, string body)> _q;
            public SequentialStub(params (HttpStatusCode, string)[] r) =>
                _q = new Queue<(HttpStatusCode, string)>(r);
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                var (s, b) = _q.Dequeue();
                return Task.FromResult(new HttpResponseMessage(s)
                { Content = new StringContent(b, System.Text.Encoding.UTF8, "application/json") });
            }
        }

        private class RecordingStub : HttpMessageHandler
        {
            public Uri? LastUri { get; private set; }
            private readonly string _body;
            public RecordingStub(string body) { _body = body; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            {
                LastUri = req.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json") });
            }
        }

        private InventreeHttpClient CreateClient(HttpMessageHandler handler) =>
            new InventreeHttpClient(
                new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) }, ApiKey);

        // ── GetBomAsync ────────────────────────────────────────────────────────

        [Test]
        public async Task GetBomAsync_ParsesQuantityAndFields()
        {
            var listJson = @"{ ""count"": 1, ""results"": [
                { ""pk"": 1, ""sub_part"": 10, ""quantity"": 2.0, ""reference"": ""100"",
                  ""note"": """", ""consumable"": false, ""optional"": false,
                  ""validated"": true,
                  ""substitutes"": [ { ""pk"": 5 } ] }
            ] }";
            var detailJson = @"{ ""pk"": 10, ""IPN"": ""OA-00130"", ""name"": ""Part"",
                ""revision"": """", ""notes"": """", ""description"": """",
                ""in_stock"": 0, ""ordering"": 0, ""active"": true }";
            var client = CreateClient(new SequentialStub(
                (HttpStatusCode.OK, listJson),
                (HttpStatusCode.OK, detailJson)));

            var lines = await client.GetBomAsync(42);

            Assert.That(lines.Count,         Is.EqualTo(1));
            Assert.That(lines[0].SubPartPk,  Is.EqualTo(10));
            Assert.That(lines[0].Quantity,   Is.EqualTo(2m));
            Assert.That(lines[0].Validated,  Is.True);
        }

        [Test]
        public async Task GetBomAsync_PopulatesHasSubstitutes_WhenArrayNonEmpty()
        {
            var listJson = @"{ ""count"": 1, ""results"": [
                { ""pk"": 1, ""sub_part"": 10, ""quantity"": 1.0, ""reference"": """",
                  ""note"": """", ""consumable"": false, ""optional"": false,
                  ""validated"": false,
                  ""substitutes"": [ { ""pk"": 7 } ] }
            ] }";
            var detailJson = @"{ ""pk"": 10, ""IPN"": ""OA-X"", ""name"": ""P"",
                ""revision"": """", ""notes"": """", ""description"": """",
                ""in_stock"": 0, ""ordering"": 0, ""active"": true }";
            var lines = await CreateClient(new SequentialStub(
                (HttpStatusCode.OK, listJson),
                (HttpStatusCode.OK, detailJson))).GetBomAsync(1);

            Assert.That(lines[0].HasSubstitutes, Is.True);
        }

        [Test]
        public async Task GetBomAsync_HasSubstitutesFalse_WhenArrayEmpty()
        {
            var listJson = @"{ ""count"": 1, ""results"": [
                { ""pk"": 2, ""sub_part"": 20, ""quantity"": 1.0, ""reference"": """",
                  ""note"": """", ""consumable"": false, ""optional"": false,
                  ""validated"": false, ""substitutes"": [] }
            ] }";
            var detailJson = @"{ ""pk"": 20, ""IPN"": ""OA-Y"", ""name"": ""Q"",
                ""revision"": """", ""notes"": """", ""description"": """",
                ""in_stock"": 0, ""ordering"": 0, ""active"": true }";
            var lines = await CreateClient(new SequentialStub(
                (HttpStatusCode.OK, listJson),
                (HttpStatusCode.OK, detailJson))).GetBomAsync(1);

            Assert.That(lines[0].HasSubstitutes, Is.False);
        }

        [Test]
        public async Task GetBomAsync_EmptyResults_ReturnsEmptyList()
        {
            var json = @"{ ""count"": 0, ""results"": [] }";
            var lines = await CreateClient(new SingleStub(HttpStatusCode.OK, json)).GetBomAsync(42);
            Assert.That(lines.Count, Is.EqualTo(0));
        }

        // ── UpdateBomLineAsync ─────────────────────────────────────────────────

        [Test]
        public async Task UpdateBomLineAsync_DoesNotIncludeSubstitutesField()
        {
            string? capturedBody = null;
            var capture = new LambdaStub(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(@"{ ""pk"": 5 }",
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

            await CreateClient(capture).UpdateBomLineAsync(5, 3m, "ref", "note", false, false);

            Assert.That(capturedBody, Does.Not.Contain("substitutes"),
                "UpdateBomLineAsync must not send substitutes in PATCH body");
        }

        [Test]
        public void UpdateBomLineAsync_SuccessResponse_DoesNotThrow()
        {
            var json = @"{ ""pk"": 5 }";
            Assert.DoesNotThrowAsync(() =>
                CreateClient(new SingleStub(HttpStatusCode.OK, json))
                    .UpdateBomLineAsync(5, 3m, "ref", "note", false, false));
        }

        // ── CreateBomLineAsync ─────────────────────────────────────────────────

        [Test]
        public async Task CreateBomLineAsync_ValidResponse_ReturnsPk()
        {
            var json = @"{ ""pk"": 99 }";
            var pk   = await CreateClient(new SingleStub(HttpStatusCode.OK, json))
                .CreateBomLineAsync(1, 10, 2m, "100", "note", false, true);
            Assert.That(pk, Is.EqualTo(99));
        }

        // ── GetPartsByIpnAsync ─────────────────────────────────────────────────

        [Test]
        public async Task GetPartsByIpnAsync_SingleResult_ReturnsOneItem()
        {
            var listJson   = @"{ ""count"": 1, ""results"": [{ ""pk"": 7, ""IPN"": ""ABC"", ""name"": ""P"", ""revision"": """", ""notes"": """" }] }";
            var detailJson = @"{ ""pk"": 7, ""IPN"": ""ABC"", ""name"": ""P"", ""revision"": """", ""notes"": """", ""description"": """", ""in_stock"": 0, ""ordering"": 0, ""active"": true }";
            var parts = await CreateClient(new SequentialStub(
                (HttpStatusCode.OK, listJson),
                (HttpStatusCode.OK, detailJson))).GetPartsByIpnAsync("ABC");
            Assert.That(parts.Count, Is.EqualTo(1));
            Assert.That(parts[0].Pk, Is.EqualTo(7));
        }

        [Test]
        public async Task GetPartsByIpnAsync_EmptyResult_ReturnsEmptyList()
        {
            var json = @"{ ""count"": 0, ""results"": [] }";
            var parts = await CreateClient(new SingleStub(HttpStatusCode.OK, json))
                .GetPartsByIpnAsync("MISSING");
            Assert.That(parts.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task GetPartsByIpnAsync_MultipleResults_ReturnsAll()
        {
            var json = @"{ ""count"": 2, ""results"": [
                { ""pk"": 1, ""IPN"": ""DUP"", ""name"": ""A"", ""revision"": """", ""notes"": """" },
                { ""pk"": 2, ""IPN"": ""DUP"", ""name"": ""B"", ""revision"": """", ""notes"": """" }
            ] }";
            var parts = await CreateClient(new SingleStub(HttpStatusCode.OK, json))
                .GetPartsByIpnAsync("DUP");
            Assert.That(parts.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task GetPartsByIpnAsync_UrlEncodesIpn()
        {
            var json = @"{ ""count"": 0, ""results"": [] }";
            var stub = new RecordingStub(json);
            await CreateClient(stub).GetPartsByIpnAsync("OA 001/A");
            Assert.That(stub.LastUri!.Query, Does.Contain("OA%20001%2FA"),
                "IPN must be encoded with Uri.EscapeDataString");
        }

        [Test]
        public void GetPartByIpnAsync_MultipleResults_ThrowsInvalidOperationException()
        {
            var json = @"{ ""count"": 2, ""results"": [
                { ""pk"": 1, ""IPN"": ""DUP"", ""name"": ""A"", ""revision"": """", ""notes"": """" },
                { ""pk"": 2, ""IPN"": ""DUP"", ""name"": ""B"", ""revision"": """", ""notes"": """" }
            ] }";
            Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateClient(new SingleStub(HttpStatusCode.OK, json)).GetPartByIpnAsync("DUP"));
        }

        // ── Helper: lambda handler ─────────────────────────────────────────────
        private class LambdaStub : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
            public LambdaStub(Func<HttpRequestMessage, HttpResponseMessage> fn) { _fn = fn; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
                Task.FromResult(_fn(req));
        }
    }
}
