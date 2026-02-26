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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/part/?IPN={ipn}");

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized). " +
                    "Please generate a new token in InvenTree → Account Settings → API Tokens " +
                    "and update inventree_servers.json next to the add-in DLL.");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();

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
            var body = new StringContent(
                $"{{\"revision\":\"{revision}\"}}",
                System.Text.Encoding.UTF8,
                "application/json");

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request);

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
    }
}
