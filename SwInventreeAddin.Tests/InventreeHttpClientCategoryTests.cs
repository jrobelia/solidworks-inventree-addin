using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeHttpClientCategoryCreateTests
    {
        private const string BaseUrl = "http://inventree.example.com";
        private const string ApiKey  = "test-api-key";

        private static InventreeHttpClient CreateClient(StubHttpMessageHandler handler)
        {
            var http = new System.Net.Http.HttpClient(handler)
            {
                BaseAddress = new System.Uri(BaseUrl)
            };
            return new InventreeHttpClient(http, ApiKey);
        }

        private static readonly string CategoriesJson =
            @"{""count"":2,""results"":[" +
            @"{""pk"":7,""name"":""Resistors"",""parent"":null,""subcategories"":3}," +
            @"{""pk"":8,""name"":""Capacitors"",""parent"":null,""subcategories"":0}" +
            @"]}";

        private static readonly string SinglePartDetailJson =
            @"{""pk"":42,""name"":""10K SMD"",""notes"":""0402"",""revision"":""A"",""IPN"":""R-10K"",""thumbnail"":""""}";

        // ── GetCategoriesAsync ──────────────────────────────────────────────────

        [Test]
        public async Task GetCategoriesAsync_RootLevel_ReturnsTopLevelCategories()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, CategoriesJson);
            var cats    = await CreateClient(handler).GetCategoriesAsync(null);

            Assert.That(cats.Count, Is.EqualTo(2));
            Assert.That(cats[0].Pk,          Is.EqualTo(7));
            Assert.That(cats[0].Name,        Is.EqualTo("Resistors"));
            Assert.That(cats[0].HasChildren, Is.True);
            Assert.That(cats[1].HasChildren, Is.False);
        }

        [Test]
        public async Task GetCategoriesAsync_WithParent_SendsParentIdInQuery()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, CategoriesJson);
            await CreateClient(handler).GetCategoriesAsync(7);

            Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("parent=7"));
        }

        [Test]
        public async Task GetCategoriesAsync_RootLevel_SendsParentNullQuery()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, CategoriesJson);
            await CreateClient(handler).GetCategoriesAsync(null);

            Assert.That(handler.LastRequest.RequestUri.Query, Does.Contain("parent=null"));
        }

        [Test]
        public void GetCategoriesAsync_NonSuccessStatus_Throws()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
            Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).GetCategoriesAsync(null));
        }

        // ── CreatePartAsync ─────────────────────────────────────────────────────

        [Test]
        public async Task CreatePartAsync_Success_ReturnsPk()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);

            var pk = await CreateClient(handler).CreatePartAsync(7, "New Part");

            Assert.That(pk, Is.EqualTo(99));
        }

        [Test]
        public async Task CreatePartAsync_Success_SendsCorrectBodyFields()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);

            await CreateClient(handler).CreatePartAsync(7, "New Part");

            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.That(doc.RootElement.GetProperty("category").GetInt32(), Is.EqualTo(7));
            Assert.That(doc.RootElement.GetProperty("name").GetString(),    Is.EqualTo("New Part"));
        }

        [Test]
        public void CreatePartAsync_NonSuccessStatus_Throws()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "error");
            Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).CreatePartAsync(7, "New Part"));
        }

        // ── GetPartByPkAsync ────────────────────────────────────────────────────

        [Test]
        public async Task GetPartByPkAsync_Found_ReturnsPart()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SinglePartDetailJson);
            var part    = await CreateClient(handler).GetPartByPkAsync(42);

            Assert.That(part,          Is.Not.Null);
            Assert.That(part!.Pk,      Is.EqualTo(42));
            Assert.That(part.Name,     Is.EqualTo("10K SMD"));
            Assert.That(part.Ipn,      Is.EqualTo("R-10K"));
            Assert.That(part.Notes,    Is.EqualTo("0402"));
            Assert.That(part.Revision, Is.EqualTo("A"));
        }

        [Test]
        public async Task GetPartByPkAsync_NotFound_ReturnsNull()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.NotFound, "not found");
            var part    = await CreateClient(handler).GetPartByPkAsync(42);

            Assert.That(part, Is.Null);
        }
    }
}
