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
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"/api/part/?IPN={Uri.EscapeDataString(ipn)}");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

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
            return new InventreePart
            {
                Pk       = first.TryGetProperty("pk", out var pkProp) ? pkProp.GetInt32() : 0,
                Name     = GetString(first, "name"),
                Notes    = GetString(first, "notes"),
                Revision = GetString(first, "revision"),
                Ipn      = GetString(first, "IPN"),
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
