using System.Linq;
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
        public async Task GetCategoriesAsync_RootLevel_FetchesAllAndFiltersClientSide()
        {
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, CategoriesJson);
            await CreateClient(handler).GetCategoriesAsync(null);

            // No parent filter in query — fetches all then filters client-side.
            Assert.That(handler.LastRequest.RequestUri.Query, Does.Not.Contain("parent="));
        }

        [Test]
        public async Task GetCategoriesAsync_RootLevel_ExcludesItemsWithParent()
        {
            // Mix of root and child items returned by server.
            var mixedJson =
                @"{""count"":3,""results"":[" +
                @"{""pk"":1,""name"":""Root A"",""parent"":null,""subcategories"":1}," +
                @"{""pk"":2,""name"":""Child of Root A"",""parent"":1,""subcategories"":0}," +
                @"{""pk"":3,""name"":""Root B"",""parent"":null,""subcategories"":0}" +
                @"]}";
            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, mixedJson);
            var cats    = await CreateClient(handler).GetCategoriesAsync(null);

            Assert.That(cats.Count, Is.EqualTo(2));
            Assert.That(cats.Select(c => c.Pk), Is.EquivalentTo(new[] { 1, 3 }));
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

        [Test]
        public void CreatePartAsync_NonSuccessStatus_ExceptionContainsResponseBody()
        {
            var errorJson = @"{""ipn"": [""Part with this IPN already exists.""]}";
            var handler   = new StubHttpMessageHandler(HttpStatusCode.BadRequest, errorJson);

            var ex = Assert.ThrowsAsync<HttpRequestException>(() =>
                CreateClient(handler).CreatePartAsync(7, "New Part", "DUP-001"));

            Assert.That(ex!.Message, Does.Contain("Part with this IPN already exists."));
        }

        [Test]
        public async Task CreatePartAsync_WithIpn_SendsIpnInBody()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);

            await CreateClient(handler).CreatePartAsync(7, "New Part", "ABC-001");

            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.That(doc.RootElement.GetProperty("ipn").GetString(), Is.EqualTo("ABC-001"));
        }

        [Test]
        public async Task CreatePartAsync_NoIpn_OmitsIpnFromBody()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);

            await CreateClient(handler).CreatePartAsync(7, "New Part");

            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.That(doc.RootElement.TryGetProperty("ipn", out _), Is.False);
        }

        [Test]
        public async Task CreatePartAsync_WithFlags_SendsAllFlagFieldsInBody()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);
            var flags = new PartCreationFlags
            {
                Assembly              = true,
                Component             = false,
                Purchaseable          = true,
                Salable               = false,
                Trackable             = true,
                Testable              = false,
                CopyCategoryParameters = true,
            };

            await CreateClient(handler).CreatePartAsync(7, "New Part", null, flags);

            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.That(doc.RootElement.GetProperty("assembly").GetBoolean(),                Is.True);
            Assert.That(doc.RootElement.GetProperty("component").GetBoolean(),               Is.False);
            Assert.That(doc.RootElement.GetProperty("purchaseable").GetBoolean(),            Is.True);
            Assert.That(doc.RootElement.GetProperty("salable").GetBoolean(),                 Is.False);
            Assert.That(doc.RootElement.GetProperty("trackable").GetBoolean(),               Is.True);
            Assert.That(doc.RootElement.GetProperty("testable").GetBoolean(),                Is.False);
            Assert.That(doc.RootElement.GetProperty("copy_category_parameters").GetBoolean(), Is.True);
        }

        [Test]
        public async Task CreatePartAsync_NoFlags_OmitsFlagFieldsFromBody()
        {
            var responseJson = @"{""pk"":99,""name"":""New Part""}";
            var handler      = new StubHttpMessageHandler(HttpStatusCode.OK, responseJson);

            await CreateClient(handler).CreatePartAsync(7, "New Part");

            using var doc = JsonDocument.Parse(handler.LastRequestBody);
            Assert.That(doc.RootElement.TryGetProperty("assembly", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("component", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("purchaseable", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("salable", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("trackable", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("testable", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("copy_category_parameters", out _), Is.False);
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
