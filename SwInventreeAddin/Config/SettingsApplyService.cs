using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public class SettingsApplyService : ISettingsApplyService
    {
        // Every probe is bounded — ~4 s, never the HttpClient default — so a
        // dead server cannot stall the Settings window.
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

        private readonly IConfigProvider _configProvider;
        private readonly IInventreeTokenService _tokenService;

        /// <summary>Uses the supplied config and token services.</summary>
        public SettingsApplyService(IConfigProvider configProvider, IInventreeTokenService tokenService)
        {
            _configProvider = configProvider;
            _tokenService = tokenService;
        }

        /// <inheritdoc/>
        public async Task<ConnectionProbeResult> ApplyAsync(SettingsApplyInput input, HttpClient client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            // A cleared URL is a legal save — "no server configured". It never
            // reaches ResolveApiKeyAsync: there is no server to resolve a
            // credential against or to probe.
            if (string.IsNullOrWhiteSpace(input.Url))
                return ApplyClear(input);

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
                _configProvider.SaveServerConfig(
                    BuildConfig(input.Url.Trim(), apiKey, input));
            }
            catch (Exception ex)
            {
                throw ConfigError(ex);
            }

            // The save already happened, so a failed probe is reported back to the
            // caller instead of throwing — it must never roll back persisted settings.
            // A credential-less save is valid ("server only" on the configuration
            // axis): report it without probing — there is nothing to test with.
            if (apiKey.Length == 0)
            {
                return new ConnectionProbeResult(
                    ConnectionProbeStatus.CredentialRejected,
                    "No credential saved — the server address was kept. Add a username and password or an API key to authenticate.");
            }

            return await ProbeAsync(input.Url.Trim(), apiKey, client, CancellationToken.None).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<ConnectionProbeResult> TestConnectionAsync(
            SettingsApplyInput input, HttpClient client, CancellationToken cancellationToken)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            string apiKey = await ResolveApiKeyAsync(input).ConfigureAwait(false);
            if (apiKey.Length == 0)
                throw new InvalidOperationException(
                    "Enter a username and password, or paste an API key.");
            return await ProbeAsync(input.Url.Trim(), apiKey, client, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task RemoveApiKeyAsync()
        {
            try
            {
                var config = _configProvider.GetServerConfig();
                if (config == null)
                    return Task.CompletedTask;

                config.ApiKey = string.Empty;
                _configProvider.SaveServerConfig(config);
            }
            catch (Exception ex)
            {
                throw RemoveError(ex);
            }

            return Task.CompletedTask;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        // The URL-clear save: the previously saved key is kept — a typed
        // credential is ignored because there is no server to resolve it
        // against — the rest of the input persists exactly as a normal save,
        // and the unconfigured outcome is reported without probing.
        private ConnectionProbeResult ApplyClear(SettingsApplyInput input)
        {
            try
            {
                var prior = _configProvider.GetServerConfig();
                _configProvider.SaveServerConfig(
                    BuildConfig(string.Empty, prior?.ApiKey ?? string.Empty, input));
            }
            catch (Exception ex)
            {
                throw ConfigError(ex);
            }

            return new ConnectionProbeResult(
                ConnectionProbeStatus.NotConfigured,
                "Server URL cleared — the saved API key was kept. Nothing was probed.");
        }

        private static ServerConfig BuildConfig(string url, string apiKey, SettingsApplyInput input) =>
            new ServerConfig
            {
                Url = url,
                ApiKey = apiKey,
                MappingSourcePath = input.SharedMappingPath,
                BomKeyword = string.IsNullOrWhiteSpace(input.BomKeyword)
                                        ? "inventree"
                                        : input.BomKeyword.Trim(),
                WaitForServerAssignedIpn = input.WaitForServerAssignedIpn,
            };

        // Apply and Test Connection share the same probe so both report the same
        // outcome for the same credential. Validation happens before this runs, so
        // a failure here always means the probe itself — never the settings.
        private static async Task<ConnectionProbeResult> ProbeAsync(
            string url, string apiKey, HttpClient client, CancellationToken cancellationToken)
        {
            client.BaseAddress = new Uri(url);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);

            // A pre-cancelled lifecycle token must propagate even when the
            // transport would answer faster than the cancellation machinery.
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                // The caller's token carries lifecycle only; the probe bound is
                // applied on top so a cancellation can be told apart from a timeout.
                using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    probeCts.CancelAfter(ProbeTimeout);
                    response = await client.GetAsync("api/part/?limit=1", probeCts.Token)
                                           .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller's lifecycle fired — propagate unclassified, never a result.
                throw;
            }
            catch (OperationCanceledException)
            {
                return new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable,
                    "Could not reach the InvenTree server — the connection timed out. Check the URL and network connection.");
            }
            catch (Exception ex)
            {
                return new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable,
                    $"Could not reach the InvenTree server. Check the URL and network connection. ({ex.Message})");
            }

            // HttpResponseMessage is IDisposable — on net48 an undisposed response
            // holds the connection until GC.
            using (response)
            {
                if (response.IsSuccessStatusCode)
                    return new ConnectionProbeResult(
                        ConnectionProbeStatus.Connected, "Connection successful.");

                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new ConnectionProbeResult(
                        ConnectionProbeStatus.CredentialRejected,
                        $"The server rejected the API key ({(int)response.StatusCode} {response.ReasonPhrase}).");
                }

                return new ConnectionProbeResult(
                    ConnectionProbeStatus.ServerError,
                    $"Server responded: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }

        private async Task<string> ResolveApiKeyAsync(SettingsApplyInput input)
        {
            var url = input.Url.Trim();

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is required.");

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// — a plain http:// connection is not secure.");

            // Validation must guarantee the probe can run: an unparsable URL would
            // otherwise throw from Uri construction after Apply already persisted.
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                throw new InvalidOperationException(
                    "Enter a valid server URL, e.g. https://inventree.example.com");

            var username = input.Username.Trim();
            var password = input.Password;
            var rawKey = input.RawApiKey.Trim();

            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Enter both username and password.");

                return await _tokenService.GetTokenAsync(url, username, password)
                                          .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            // No credential at all is not an error here — Apply persists the
            // URL-only config. Callers that require a credential (Test
            // Connection) check for the empty result themselves.
            return string.Empty;
        }

        private static SettingsApplyException ConfigError(Exception ex)
            => new SettingsApplyException($"Failed to save server settings: {ex.Message}", ex);

        private static SettingsApplyException RemoveError(Exception ex)
            => new SettingsApplyException($"Failed to remove the API key: {ex.Message}", ex);
    }
}
