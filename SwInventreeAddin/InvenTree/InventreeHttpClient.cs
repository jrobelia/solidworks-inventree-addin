using System;
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
                using var detailRequest = new HttpRequestMessage(
                    HttpMethod.Get, $"/api/part/{pk}/");

                var detailResponse = await _httpClient.SendAsync(detailRequest).ConfigureAwait(false);

                if (detailResponse.IsSuccessStatusCode)
                {
                    var detailJson = await detailResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var detailDocument = JsonDocument.Parse(detailJson);
                    var detail = detailDocument.RootElement;

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
            }

            // Fallback: build from list data (notes may be absent)
            return new InventreePart
            {
                Pk           = pk,
                Name         = GetString(first, "name"),
                Notes        = GetString(first, "notes"),
                Revision     = GetString(first, "revision"),
                Ipn          = GetString(first, "IPN"),
                ThumbnailUrl = GetString(first, "thumbnail") is var t2 && t2.Length > 0 ? t2 : null,
            };
        }

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
    }
}
