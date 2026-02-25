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
        private readonly string _apiKey;

        public InventreeHttpClient(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            _apiKey     = apiKey;
        }

        public async Task<InventreePart> GetPartByIpnAsync(string ipn)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Token", _apiKey);

            var response = await _httpClient.GetAsync($"/api/part/?IPN={ipn}");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json     = await response.Content.ReadAsStringAsync();
            var document = JsonDocument.Parse(json);
            var array    = document.RootElement;

            if (array.GetArrayLength() == 0)
                return null!;

            var first = array[0];
            return new InventreePart
            {
                Name     = first.GetProperty("name").GetString()     ?? string.Empty,
                Notes    = first.GetProperty("notes").GetString()    ?? string.Empty,
                Revision = first.GetProperty("revision").GetString() ?? string.Empty,
                Ipn      = first.GetProperty("IPN").GetString()      ?? string.Empty
            };
        }
    }
}
