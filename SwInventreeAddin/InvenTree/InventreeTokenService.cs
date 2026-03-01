using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Fetches an InvenTree API token by exchanging username + password
    /// against GET /api/user/token/ using HTTP Basic Auth.
    /// Equivalent to: requests.get(url, auth=(username, password))
    /// </summary>
    public class InventreeTokenService : IInventreeTokenService
    {
        private readonly HttpClient _httpClient;

        public InventreeTokenService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string> GetTokenAsync(string url, string username, string password)
        {
            // Defence-in-depth: reject http:// even if the caller forgot to validate.
            // Basic Auth credentials would otherwise travel in cleartext.
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// — a plain http:// connection is not secure.");

            // Build the token endpoint URL, tolerating a trailing slash or not
            var baseUri  = new Uri(url.TrimEnd('/') + "/");
            var endpoint = new Uri(baseUri, "api/user/token/");

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

            // Basic Auth header: base64("username:password")
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach the InvenTree server. Check the URL and network connection. ({ex.Message})", ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    "Wrong username or password. Check your InvenTree credentials and try again.");

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Server returned an unexpected error: {(int)response.StatusCode} {response.ReasonPhrase}");

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            string? token;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("token", out var tokenElement))
                    throw new InvalidOperationException(
                        "Server responded but did not return a token. Check the Server URL points to InvenTree.");
                token = tokenElement.GetString();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Server response was not valid JSON. ({ex.Message})", ex);
            }

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "Server returned a response but the token was empty. Contact your InvenTree administrator.");

            return token!;  // non-null guaranteed by IsNullOrWhiteSpace check above
        }
    }
}
