using System;
using System.Net.Http;
using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public class SettingsApplyService : ISettingsApplyService
    {
        private readonly IConfigProvider          _configProvider;
        private readonly IInventreeTokenService   _tokenService;

        /// <summary>Uses the supplied config and token services.</summary>
        public SettingsApplyService(IConfigProvider configProvider, IInventreeTokenService tokenService)
        {
            _configProvider = configProvider;
            _tokenService   = tokenService;
        }

        /// <inheritdoc/>
        public async Task ApplyAsync(SettingsApplyInput input)
        {
            string apiKey;
            try
            {
                apiKey = await ResolveApiKeyAsync(input).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw ConfigError(ex);
            }

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url               = input.Url.Trim(),
                    ApiKey            = apiKey,
                    MappingSourcePath = input.SharedMappingPath,
                    BomKeyword            = string.IsNullOrWhiteSpace(input.BomKeyword)
                                            ? "inventree"
                                            : input.BomKeyword.Trim(),
                    WaitForServerAssignedIpn = input.WaitForServerAssignedIpn,
                });
            }
            catch (Exception ex)
            {
                throw ConfigError(ex);
            }
        }

        /// <inheritdoc/>
        public async Task TestConnectionAsync(SettingsApplyInput input, HttpClient client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            string apiKey = await ResolveApiKeyAsync(input).ConfigureAwait(false);

            client.BaseAddress = new Uri(input.Url.Trim());
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync("api/part/?limit=1").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach the InvenTree server. Check the URL and network connection. ({ex.Message})",
                    ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Server responded: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<string> ResolveApiKeyAsync(SettingsApplyInput input)
        {
            var url = input.Url.Trim();

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is required.");

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// — a plain http:// connection is not secure.");

            var username = input.Username.Trim();
            var password = input.Password;
            var rawKey   = input.RawApiKey.Trim();

            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Enter both username and password.");

                return await _tokenService.GetTokenAsync(url, username, password)
                                          .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            throw new InvalidOperationException(
                "Enter a username and password, or expand Advanced and paste an API key.");
        }

        private static SettingsApplyException ConfigError(Exception ex)
            => new SettingsApplyException($"Failed to save server settings: {ex.Message}", ex);
    }
}
