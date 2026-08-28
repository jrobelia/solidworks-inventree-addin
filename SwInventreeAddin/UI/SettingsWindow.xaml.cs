using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwInventreeAddin;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Settings dialog — server credentials (upper section) + property mapping (lower section).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider                            _configProvider;
        private readonly ISettingsApplyService                      _settingsApplyService;
        private readonly IVersionInfo                               _versionInfo;
        private readonly System.Func<string?, IPropertyMappingProvider> _mappingProviderFactory;
        private IPropertyMappingProvider                            _mappingProvider;

        /// <summary>
        /// The most recent main status text set by <see cref="SetStatus"/>.
        /// Exposed for unit-test verification; do not bind against it.
        /// </summary>
        public string? StatusMessage { get; private set; }

        /// <summary>
        /// The most recent mapping status text set by <see cref="RefreshMappingStatus"/>.
        /// Exposed for unit-test verification; do not bind against it.
        /// </summary>
        public string? MappingStatusMessage { get; private set; }

        private (string Url, string ApiKey, string Username, string Password,
                 string SharedPath, string BomKeyword, bool UseLocalMapping) _savedSnapshot;
        private bool _savedWaitForAutoPartNumber = true;

        /// <summary>
        /// Raised after Apply successfully saves settings, so the caller can update
        /// the live mapping provider without waiting for the dialog to close.
        /// </summary>
        public event EventHandler<IPropertyMappingProvider>? MappingApplied;

        public SettingsWindow(IConfigProvider configProvider,
                              IPropertyMappingProvider mappingProvider,
                              IVersionInfo versionInfo)
            : this(configProvider, mappingProvider, versionInfo,
                   new SettingsApplyService(configProvider,
                                            new InventreeTokenService(new HttpClient())),
                   sourcePath => new PropertyMappingProvider(sourcePath)) { }

        internal SettingsWindow(IConfigProvider configProvider,
                                IPropertyMappingProvider mappingProvider,
                                IVersionInfo versionInfo,
                                ISettingsApplyService settingsApplyService,
                                System.Func<string?, IPropertyMappingProvider> mappingProviderFactory)
        {
            _configProvider         = configProvider;
            _mappingProvider        = mappingProvider;
            _versionInfo            = versionInfo;
            _settingsApplyService   = settingsApplyService;
            _mappingProviderFactory = mappingProviderFactory;
            DataContext             = _versionInfo;

            InitializeComponent();

            UrlBox.TextChanged          += (_, __) => RefreshButtonStates();
            UsernameBox.TextChanged     += (_, __) => RefreshButtonStates();
            PasswordBox.PasswordChanged += (_, __) => RefreshButtonStates();
            ApiBox.TextChanged          += (_, __) => RefreshButtonStates();
            SharedPathBox.TextChanged   += (_, __) => RefreshButtonStates();
            BomKeywordBox.TextChanged   += (_, __) => RefreshButtonStates();
            LocalRadio.Checked          += (_, __) => RefreshButtonStates();
            SharedRadio.Checked         += (_, __) => RefreshButtonStates();

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());

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
                    _savedWaitForAutoPartNumber = config.WaitForAutoPartNumber;
                }
            }
            catch { /* corrupt settings — user can re-enter */ }

            // Show local path (read-only, copyable)
            LocalPathBox.Text = _mappingProvider.LocalFilePath;

            // Set Edit Mappings button state and mapping status bar
            RefreshMappingStatus();

            _savedSnapshot = CaptureSnapshot();
            RefreshButtonStates();
        }

        // ── Dirty-state tracking ───────────────────────────────────────────────

        private (string, string, string, string, string, string, bool) CaptureSnapshot() =>
            (UrlBox.Text.Trim(), ApiBox.Text.Trim(), UsernameBox.Text.Trim(), PasswordBox.Password,
             SharedPathBox.Text.Trim(), BomKeywordBox.Text.Trim(), LocalRadio.IsChecked == true);

        private void RefreshButtonStates()
        {
            bool isDirty          = CaptureSnapshot() != _savedSnapshot;
            ApplyButton.IsEnabled = isDirty;
            SaveButton.IsEnabled  = isDirty;
            CancelButtonText.Text = isDirty ? "Cancel" : "Close";
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
            var editor = new PropertyMappingEditorWindow(_mappingProvider, this);
            editor.ShowDialog();
            RefreshMappingStatus();
        }

        // ── Mapping status bar ────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the mapping status bar. Catches mapping provider errors,
        /// shows a red error stripe, and returns <c>false</c>.
        /// </summary>
        private bool RefreshMappingStatus()
        {
            try
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
                MappingStatusMessage           = statusText;
                return true;
            }
            catch (Exception ex)
            {
                EditMappingsButton.IsEnabled   = false;
                MappingStatusStripe.Background = (Brush)FindResource("BrushStatusError");
                MappingStatusText.Text         = ex.Message;
                MappingStatusMessage           = ex.Message;
                return false;
            }
        }

        private ServerConfig? TryGetConfig()
        {
            try   { return _configProvider.GetServerConfig(); }
            catch { return null; }
        }

        // ── Shared helper ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="SettingsApplyInput"/> from the current UI fields.
        /// </summary>
        private SettingsApplyInput BuildInput()
        {
            string? sharedPath = (SharedRadio.IsChecked == true)
                ? (string.IsNullOrWhiteSpace(SharedPathBox.Text) ? null : SharedPathBox.Text.Trim())
                : null;

            return new SettingsApplyInput
            {
                Url                   = UrlBox.Text.Trim(),
                Username              = UsernameBox.Text.Trim(),
                Password              = PasswordBox.Password,
                RawApiKey             = ApiBox.Text.Trim(),
                SharedMappingPath     = sharedPath,
                BomKeyword            = BomKeywordBox.Text,
                WaitForAutoPartNumber = _savedWaitForAutoPartNumber,
            };
        }

        // ── Save ──────────────────────────────────────────────────────────────

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!await ApplySettingsAsync()) return;
            DialogResult = true;
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!await ApplySettingsAsync()) return;
            SetStatus("\u2713  Settings applied.", error: false, success: true);
        }

        // ── Shared settings save + notify ─────────────────────────────────────

        /// <summary>
        /// Resolves credentials, persists server config, rebuilds the mapping provider,
        /// refreshes the status bar, and fires <see cref="MappingApplied"/>.
        /// Returns <c>true</c> on success, <c>false</c> if an error was shown to the user.
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ApplySettingsAsync()
        {
            try
            {
                await _settingsApplyService.ApplyAsync(BuildInput()).ConfigureAwait(true);
            }
            catch (SettingsApplyException ex)
            {
                SetStatus(ex.Message, error: true);
                return false;
            }

            var input     = BuildInput();
            string? sharedPath = input.SharedMappingPath;

            try
            {
                _mappingProvider = _mappingProviderFactory(sharedPath);
                if (!RefreshMappingStatus())
                {
                    SetStatus(MappingStatusMessage ?? "Failed to load mapping file.", error: true);
                    return false;
                }

                MappingApplied?.Invoke(this, _mappingProvider);
                _savedSnapshot = CaptureSnapshot();
                RefreshButtonStates();
                SetStatus("\u2713  Settings applied.", error: false, success: true);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load mapping file: {ex.Message}", error: true);
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
                apiKey = await _settingsApplyService.ResolveApiKeyAsync(BuildInput())
                                                   .ConfigureAwait(true);
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
            StatusMessage = text;
            StatusText.Text = text;
            StatusText.Foreground =
                error   ? new SolidColorBrush(Color.FromRgb(180, 40, 0))
                : success ? new SolidColorBrush(Color.FromRgb(0, 130, 60))
                :           (Brush)FindResource("BrushSubtle");
        }
    }
}
