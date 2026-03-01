using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// WPF settings dialog.
    /// Primary path: enter URL + username + password — Save fetches the API token automatically.
    /// Advanced path: expand "Advanced" and paste a raw API key (existing token or manual override).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider _configProvider;
        private readonly IInventreeTokenService _tokenService;

        public SettingsWindow(IConfigProvider configProvider)
            : this(configProvider, new InventreeTokenService(new HttpClient())) { }

        internal SettingsWindow(IConfigProvider configProvider, IInventreeTokenService tokenService)
        {
            _configProvider = configProvider;
            _tokenService   = tokenService;
            InitializeComponent();

            // Try to centre over the SolidWorks main window.
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* cosmetic */ }

            // Pre-fill: URL from saved config; username/password always blank;
            // Advanced expander shows the saved token so user knows one exists.
            // If the settings file is corrupt, open the dialog empty rather than crashing.
            try
            {
                var config = _configProvider.GetServerConfig();
                if (config != null)
                {
                    UrlBox.Text = config.Url    ?? string.Empty;
                    ApiBox.Text = config.ApiKey ?? string.Empty;
                }
            }
            catch { /* corrupt settings — user can re-enter; swallowing is intentional */ }
        }

        // ── Shared helper ─────────────────────────────────────────────────────
        // Returns the API key to use, either by fetching it via username/password
        // or by reading the raw key from the Advanced field.
        // Throws InvalidOperationException with a user-readable message on failure.
        private async System.Threading.Tasks.Task<string> ResolveApiKeyAsync()
        {
            var url      = UrlBox.Text.Trim();
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;   // PasswordBox has no .Text
            var rawKey   = ApiBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is required.");

            // Require HTTPS — Basic Auth credentials travel in base64 (not encrypted);
            // an http:// URL would expose the username, password, and token in plaintext.
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// — a plain http:// connection is not secure.");

            // Sign-in path: username + password provided
            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Enter both username and password.");

                SetStatus("Signing in\u2026", error: false);
                return await _tokenService.GetTokenAsync(url, username, password)
                                          .ConfigureAwait(true);
            }

            // Advanced path: raw key pasted directly
            if (!string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            throw new InvalidOperationException(
                "Enter a username and password, or expand Advanced and paste an API key.");
        }

        // ── Save ──────────────────────────────────────────────────────────────
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string apiKey;
            try
            {
                apiKey = await ResolveApiKeyAsync().ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message, error: true);
                return;
            }

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url    = UrlBox.Text.Trim(),
                    ApiKey = apiKey,
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to save settings: {ex.Message}", error: true);
                return;
            }

            DialogResult = true;
        }

        // ── Cancel ────────────────────────────────────────────────────────────
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // ── Test Connection ───────────────────────────────────────────────────
        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            string apiKey;
            try
            {
                apiKey = await ResolveApiKeyAsync().ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message, error: true);
                return;
            }

            SetStatus("Testing\u2026", error: false);

            try
            {
                var url = UrlBox.Text.Trim();
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(url);
                    // Use AuthenticationHeaderValue — validates the token value and rejects
                    // any CR/LF characters that could cause header injection (CWE-113).
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);
                    var response = await client.GetAsync("api/part/?limit=1").ConfigureAwait(true);

                    if (response.IsSuccessStatusCode)
                        SetStatus("\u2713  Connection successful.", error: false, success: true);
                    else
                        SetStatus($"Server responded: {(int)response.StatusCode} {response.ReasonPhrase}", error: true);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Connection failed: {ex.Message}", error: true);
            }
        }

        // ── Status bar ────────────────────────────────────────────────────────
        private void SetStatus(string text, bool error, bool success = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = error   ? new SolidColorBrush(Color.FromRgb(180, 40, 0))
                                  : success ? new SolidColorBrush(Color.FromRgb(0, 130, 60))
                                  :           (Brush)FindResource("BrushSubtle");
        }
    }
}
