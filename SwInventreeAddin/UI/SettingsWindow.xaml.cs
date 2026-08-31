using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwInventreeAddin;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Settings dialog — server credentials (upper section) + property mapping (lower section).
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider            _configProvider;
        private readonly ISettingsApplyService      _settingsApplyService;
        private readonly IVersionInfo               _versionInfo;
        private readonly IMappingProviderFactory    _mappingProviderFactory;
        private          IPropertyMappingProvider   _mappingProvider;

        private (string Url, string ApiKey, string Username, string Password,
                 string SharedPath, string BomKeyword, bool UseLocalMapping) _savedSnapshot;
        private          bool                       _savedWaitForAutoPartNumber = true;
        private          MappingChangedSubscription? _mappingChangedSubscription;

        /// <summary>
        /// Raised after Apply successfully saves settings, so the caller can update
        /// the live mapping provider without waiting for the dialog to close.
        /// </summary>
        public event EventHandler<IPropertyMappingProvider>? MappingApplied;

        internal SettingsWindow(IConfigProvider configProvider,
                                IPropertyMappingProvider mappingProvider,
                                IVersionInfo versionInfo,
                                ISettingsApplyService settingsApplyService,
                                IMappingProviderFactory mappingProviderFactory)
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
            AttachMappingChanged();
            Closed += (_, __) => DetachMappingChanged();

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

        // Re-renders the mapping status bar from IPropertyMappingProvider.GetMappingResult()
        // whenever the provider or its underlying file changes, so the status, colour, and
        // Edit Mappings button always reflect the current mapping health.
        private bool RefreshMappingStatus()
        {
            try
            {
                var result = _mappingProvider.GetMappingResult();

                EditMappingsButton.IsEnabled = result.CanEdit && !_mappingProvider.IsReadOnly;

                var config = TryGetConfig();
                bool hasSharedPath = config != null && !string.IsNullOrEmpty(config.MappingSourcePath);
                SharedRadio.IsChecked = hasSharedPath;
                LocalRadio.IsChecked  = !hasSharedPath;

                Brush? stripeColor = result.Health switch
                {
                    MappingHealth.Healthy      => (Brush?)FindResource("BrushStatusSuccess"),
                    MappingHealth.NeedsUpgrade => (Brush?)FindResource("BrushStatusWarning"),
                    MappingHealth.NewerSchema  => (Brush?)FindResource("BrushStatusWarning"),
                    _                          => (Brush?)FindResource("BrushStatusError"),
                };

                MappingStatusStripe.Background = stripeColor;
                MappingStatusText.Text         = result.MessageOrDefault;
                MappingStatusText.ToolTip      = result.MessageOrDefault;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                EditMappingsButton.IsEnabled   = false;
                MappingStatusStripe.Background = (Brush)FindResource("BrushStatusError");
                MappingStatusText.Text         = ex.Message;
                MappingStatusText.ToolTip      = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                EditMappingsButton.IsEnabled   = false;
                MappingStatusStripe.Background = (Brush)FindResource("BrushStatusError");
                MappingStatusText.Text         = $"Failed to load mapping file: {ex.Message}";
                MappingStatusText.ToolTip      = MappingStatusText.Text;
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
            this.Dispatcher.Invoke(() => SetStatus("\u2713  Settings applied.", error: false, success: true));
        }

        // ── Shared settings save + notify ─────────────────────────────────────

        /// <summary>
        /// Resolves credentials, persists server config, rebuilds the mapping provider,
        /// refreshes the status bar, and fires <see cref="MappingApplied"/>.
        /// Returns <c>true</c> on success, <c>false</c> if an error was shown to the user.
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ApplySettingsAsync()
        {
            var input = BuildInput();

            try
            {
                await _settingsApplyService.ApplyAsync(input).ConfigureAwait(false);
            }
            catch (SettingsApplyException ex)
            {
                this.Dispatcher.Invoke(() => SetStatus(ex.Message, error: true));
                return false;
            }

            try
            {
                var previousProvider = _mappingProvider;
                _mappingProvider = _mappingProviderFactory.Create(input.SharedMappingPath);

                bool mappingOk   = this.Dispatcher.Invoke(() => RefreshMappingStatus());

                this.Dispatcher.Invoke(() =>
                {
                    DetachMappingChanged();
                    AttachMappingChanged();
                });

                if (!mappingOk)
                {
                    this.Dispatcher.Invoke(() => SetStatus(MappingStatusText.Text, error: true));
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => SetStatus($"Failed to load mapping file: {ex.Message}", error: true));
                return false;
            }

            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    MappingApplied?.Invoke(this, _mappingProvider);
                    _savedSnapshot = CaptureSnapshot();
                    RefreshButtonStates();
                    SetStatus("\u2713  Settings applied.", error: false, success: true);
                });
                return true;
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => SetStatus($"Failed to apply settings: {ex.Message}", error: true));
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
            try
            {
                using (var client = new HttpClient())
                {
                    await _settingsApplyService.TestConnectionAsync(BuildInput(), client)
                                               .ConfigureAwait(false);
                }

                this.Dispatcher.Invoke(() =>
                    SetStatus("\u2713  Connection successful.", error: false, success: true));
            }
            catch (InvalidOperationException ex)
            {
                this.Dispatcher.Invoke(() => SetStatus(ex.Message, error: true));
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() =>
                    SetStatus($"Connection failed: {ex.Message}", error: true));
            }
        }

        // ── Mapping change notifications ──────────────────────────────────────

        private void AttachMappingChanged() =>
            MappingChangedSubscription.SubscribeTo(ref _mappingChangedSubscription, _mappingProvider, OnMappingChanged);

        private void DetachMappingChanged() =>
            MappingChangedSubscription.UnsubscribeFrom(ref _mappingChangedSubscription);

        private void OnMappingChanged()
        {
            if (!CheckAccess())
            {
                this.Dispatcher.Invoke(() => RefreshMappingStatus());
                return;
            }

            RefreshMappingStatus();
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
