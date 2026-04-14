using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Settings dialog — server credentials (upper section) + property mapping (lower section).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider          _configProvider;
        private readonly IInventreeTokenService   _tokenService;
        private IPropertyMappingProvider _mappingProvider;

        /// <summary>
        /// Raised after Apply successfully saves settings, so the caller can update
        /// the live mapping provider without waiting for the dialog to close.
        /// </summary>
        public event EventHandler<IPropertyMappingProvider>? MappingApplied;

        public SettingsWindow(IConfigProvider configProvider,
                              IPropertyMappingProvider mappingProvider)
            : this(configProvider, mappingProvider,
                   new InventreeTokenService(new HttpClient())) { }

        internal SettingsWindow(IConfigProvider configProvider,
                                IPropertyMappingProvider mappingProvider,
                                IInventreeTokenService tokenService)
        {
            _configProvider  = configProvider;
            _mappingProvider = mappingProvider;
            _tokenService    = tokenService;
            InitializeComponent();

            // Centre over SolidWorks main window
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* cosmetic */ }

            // Pre-fill server credentials
            try
            {
                var config = _configProvider.GetServerConfig();
                if (config != null)
                {
                    UrlBox.Text = config.Url    ?? string.Empty;
                    ApiBox.Text = config.ApiKey ?? string.Empty;

                    if (!string.IsNullOrEmpty(config.MappingSourcePath))
                        SharedPathBox.Text = config.MappingSourcePath;

                    BomKeywordBox.Text = config.BomKeyword ?? "inventree";
                }
            }
            catch { /* corrupt settings — user can re-enter */ }

            // Show local path (read-only, copyable)
            LocalPathBox.Text = _mappingProvider.LocalFilePath;

            // Set Edit Mappings button state and mapping status bar
            RefreshMappingStatus();
        }

        // ── Radio button handlers ──────────────────────────────────────────────

        private void SharedRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SharedPathBox == null) return;   // guard during InitializeComponent

            SharedPathBox.IsReadOnly = false;
            SharedPathBox.Background = System.Windows.Media.Brushes.White;
            // EditMappingsButton.IsEnabled is NOT set here — it is controlled
            // exclusively by RefreshMappingStatus() based on _mappingProvider.IsReadOnly.
        }

        private void LocalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SharedPathBox == null) return;   // guard during InitializeComponent

            SharedPathBox.IsReadOnly = true;
            SharedPathBox.Background = (Brush)FindResource("BrushSectionHeader");
            // EditMappingsButton.IsEnabled is NOT set here — it is controlled
            // exclusively by RefreshMappingStatus() based on _mappingProvider.IsReadOnly.
        }

        // ── Browse ────────────────────────────────────────────────────────────

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title            = "Select shared mapping file",
                Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists  = true,
            };

            if (!string.IsNullOrEmpty(SharedPathBox.Text))
                try { dlg.InitialDirectory = System.IO.Path.GetDirectoryName(SharedPathBox.Text); }
                catch { /* ignore bad path */ }

            if (dlg.ShowDialog() == true)
                SharedPathBox.Text = dlg.FileName;
        }

        // ── Edit Mappings ─────────────────────────────────────────────────────

        private void EditMappings_Click(object sender, RoutedEventArgs e)
        {
            var editor = new PropertyMappingEditorWindow(_mappingProvider) { Owner = this };
            editor.ShowDialog();
            RefreshMappingStatus();
        }

        // ── Mapping status bar ────────────────────────────────────────────────

        private void RefreshMappingStatus()
        {
            EditMappingsButton.IsEnabled = !_mappingProvider.IsReadOnly;

            // Show the appropriate radio checked state
            var config = TryGetConfig();
            bool hasSharedPath = config != null && !string.IsNullOrEmpty(config.MappingSourcePath);
            SharedRadio.IsChecked = hasSharedPath;
            LocalRadio.IsChecked  = !hasSharedPath;

            // Set stripe colour and status text
            Brush stripeColor;
            string statusText;

            // Check for schema version mismatch
            var mapping = _mappingProvider.GetMapping();
            bool schemaMismatch = mapping.SchemaVersion != PropertyMappingConfig.CurrentSchemaVersion;

            if (schemaMismatch)
            {
                stripeColor = (Brush)FindResource("BrushStatusWarning");
                statusText  = "Schema version mismatch \u2014 review mappings";
            }
            else if (_mappingProvider.IsReadOnly)
            {
                stripeColor = (Brush)FindResource("BrushStatusSuccess");
                statusText  = "Loaded from shared file";
            }
            else if (System.IO.File.Exists(_mappingProvider.LocalFilePath))
            {
                stripeColor = (Brush)FindResource("BrushStatusSuccess");
                statusText  = "Using local mappings";
            }
            else
            {
                stripeColor = (Brush)FindResource("BrushSectionHeader");
                statusText  = "No mappings configured";
            }

            MappingStatusStripe.Background = stripeColor;
            MappingStatusText.Text         = statusText;
        }

        private ServerConfig? TryGetConfig()
        {
            try   { return _configProvider.GetServerConfig(); }
            catch { return null; }
        }

        // ── Shared helper ─────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task<string> ResolveApiKeyAsync()
        {
            var url      = UrlBox.Text.Trim();
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;
            var rawKey   = ApiBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is required.");

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// \u2014 a plain http:// connection is not secure.");

            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Enter both username and password.");

                SetStatus("Signing in\u2026", error: false);
                return await _tokenService.GetTokenAsync(url, username, password)
                                          .ConfigureAwait(true);
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            throw new InvalidOperationException(
                "Enter a username and password, or expand Advanced and paste an API key.");
        }

        // ── Save ──────────────────────────────────────────────────────────────

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!await ApplySettingsCore()) return;
            DialogResult = true;
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!await ApplySettingsCore()) return;
            SetStatus("\u2713  Settings applied.", error: false, success: true);
        }

        // ── Shared settings save + notify ─────────────────────────────────────

        /// <summary>
        /// Resolves credentials, persists server config, rebuilds the mapping provider,
        /// refreshes the status bar, and fires <see cref="MappingApplied"/>.
        /// Returns true on success, false if an error was shown to the user.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> ApplySettingsCore()
        {
            string apiKey;
            try
            {
                apiKey = await ResolveApiKeyAsync().ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(ex.Message, error: true);
                return false;
            }

            string? sharedPath = (SharedRadio.IsChecked == true)
                ? (string.IsNullOrWhiteSpace(SharedPathBox.Text) ? null : SharedPathBox.Text.Trim())
                : null;

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url               = UrlBox.Text.Trim(),
                    ApiKey            = apiKey,
                    MappingSourcePath = sharedPath,
                    BomKeyword        = string.IsNullOrWhiteSpace(BomKeywordBox.Text)
                                            ? "inventree"
                                            : BomKeywordBox.Text.Trim(),
                });

                _mappingProvider = new PropertyMappingProvider(sharedPath);
                RefreshMappingStatus();
                MappingApplied?.Invoke(this, _mappingProvider);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to save settings: {ex.Message}", error: true);
                return false;
            }
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
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);
                    var response = await client.GetAsync("api/part/?limit=1").ConfigureAwait(true);

                    if (response.IsSuccessStatusCode)
                        SetStatus("\u2713  Connection successful.", error: false, success: true);
                    else
                        SetStatus($"Server responded: {(int)response.StatusCode} {response.ReasonPhrase}",
                                  error: true);
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
            StatusText.Foreground =
                error   ? new SolidColorBrush(Color.FromRgb(180, 40, 0))
                : success ? new SolidColorBrush(Color.FromRgb(0, 130, 60))
                :           (Brush)FindResource("BrushSubtle");
        }
    }
}
