using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    public class InventreeHttpClient : IInventreeClient
    {
        private readonly HttpClient _httpClient;

        public InventreeHttpClient(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            // Set the auth header once — the API key never changes during the session.
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Token", apiKey);
        }

        public async Task<InventreePart?> GetPartByIpnAsync(string ipn)
        {
            // IPN comes from SolidWorks custom properties (user-controlled) — must be encoded
            // to prevent query-string injection (e.g. "ABC&limit=0").
            using var listRequest = new HttpRequestMessage(
                HttpMethod.Get, $"/api/part/?IPN={Uri.EscapeDataString(ipn)}");

            var listResponse = await _httpClient.SendAsync(listRequest).ConfigureAwait(false);

            if (!listResponse.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)listResponse.StatusCode} {listResponse.StatusCode}");

            var listJson = await listResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var listDocument = JsonDocument.Parse(listJson);
            var root = listDocument.RootElement;

            // InvenTree list endpoints return a paginated envelope:
            // { "count": N, "results": [ {...}, ... ] }
            // Fall back to treating the root itself as an array for future-proofing.
            var array = root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("results", out var resultsElement)
                ? resultsElement
                : root;

            if (array.GetArrayLength() == 0)
                return null;

            var first = array[0];
            int pk = first.TryGetProperty("pk", out var pkProp) ? pkProp.GetInt32() : 0;

            // The list endpoint omits some fields (e.g. notes). Fetch the full
            // record from the detail endpoint so we get every field we need.
            if (pk > 0)
            {
                var detail = await FetchDetailAsync(pk).ConfigureAwait(false);
                if (detail != null)
                    return detail;
            }

            // Fallback: build from list data (notes may be absent)
            return new InventreePart
            {
                Pk           = pk,
                Name         = GetString(first, "name"),
                Notes        = GetString(first, "notes"),
                Revision     = GetString(first, "revision"),
                Ipn          = GetString(first, "IPN"),
                ThumbnailUrl = GetString(first, "thumbnail") is var t && t.Length > 0 ? t : null,
            };
        }

        /// <summary>
        /// Fetches a single part record from /api/part/{pk}/.
        /// Returns null when the server returns a non-success status.
        /// </summary>
        private async Task<InventreePart?> FetchDetailAsync(int pk)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/part/{pk}/");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var detail = doc.RootElement;

            return new InventreePart
            {
                Pk           = pk,
                Name         = GetString(detail, "name"),
                Notes        = GetString(detail, "notes"),
                Revision     = GetString(detail, "revision"),
                Ipn          = GetString(detail, "IPN"),
                ThumbnailUrl = GetString(detail, "thumbnail") is var t && t.Length > 0 ? t : null,
            };
        }

        public async Task<IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId)
        {
            // When fetching children of a known parent, filter server-side.
            // When fetching root categories, fetch all and filter client-side for
            // items with no parent — avoids relying on ?parent=null which some
            // InvenTree versions reject with 400.
            var query = parentId.HasValue
                ? $"/api/part/category/?parent={parentId.Value}&limit=0"
                : "/api/part/category/?limit=0";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var array = root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("results", out var results)
                ? results
                : root;

            var list = new List<InventreeCategory>(array.GetArrayLength());
            foreach (var item in array.EnumerateArray())
            {
                var parentPk = item.TryGetProperty("parent", out var parP) && parP.ValueKind == JsonValueKind.Number
                                ? parP.GetInt32() : (int?)null;

                // When loading root categories (no parentId given), skip any item
                // that actually has a parent — they belong lower in the tree.
                if (!parentId.HasValue && parentPk.HasValue)
                    continue;

                list.Add(new InventreeCategory
                {
                    Pk          = item.TryGetProperty("pk",            out var pkP)   ? pkP.GetInt32()              : 0,
                    Name        = item.TryGetProperty("name",          out var nameP) ? nameP.GetString() ?? string.Empty : string.Empty,
                    ParentPk    = parentPk,
                    HasChildren = item.TryGetProperty("subcategories", out var subP)  && subP.ValueKind == JsonValueKind.Number
                                    ? subP.GetInt32() > 0 : false,
                });
            }
            return list;
        }

        public async Task<int> CreatePartAsync(int categoryPk, string name)
        {
            var payload = JsonSerializer.Serialize(new { category = categoryPk, name });
            var body    = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/part/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("pk", out var pkProp))
                return pkProp.GetInt32();

            throw new InvalidOperationException("InvenTree did not return a pk for the new part.");
        }

        public Task<InventreePart?> GetPartByPkAsync(int pk) =>
            FetchDetailAsync(pk);

        public async Task UpdatePartRevisionAsync(int pk, string revision)
        {
            var json = JsonSerializer.Serialize(new { revision });
            var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        public async Task UpdatePartNameAsync(int pk, string name)
        {
            var json = JsonSerializer.Serialize(new { name });
            var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        public async Task UpdatePartNotesAsync(int pk, string notes)
        {
            var json = JsonSerializer.Serialize(new { notes });
            var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        private static string GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var prop)
                ? prop.GetString() ?? string.Empty
                : string.Empty;

        public async Task<byte[]?> DownloadImageAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Reject absolute non-HTTPS URLs — the auth token must not travel over plain HTTP.
            // Relative URLs (e.g. /media/thumbnails/widget.png) are safe because BaseAddress is HTTPS.
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                !string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;   // treat any error as "no image" — caller shows placeholder

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        public async Task UploadPartImageAsync(int pk, byte[] pngData)
        {
            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(pngData);
            imageContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "image", "part_image.png");

            using var request = new HttpRequestMessage(
                new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        public async Task<InventreeServerInfo> GetServerInfoAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new InventreeServerInfo
            {
                ServerVersion = GetString(root, "version"),
                ApiVersion    = root.TryGetProperty("apiVersion", out var v) ? v.GetInt32() : 0,
            };
        }
    }
}
