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
        private          bool                       _savedWaitForServerAssignedIpn = true;
        private          MappingChangedSubscription? _mappingChangedSubscription;
        private          string?                    _mappingStatusDetail;

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
                    _savedWaitForServerAssignedIpn = config.WaitForServerAssignedIpn;
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
            // EditMappingsButton.IsEnabled and label are controlled by RefreshMappingStatus()
            // based on the resolved mapping file and its MappingHealth.
        }

        private void LocalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SharedPathBox == null) return;   // guard during InitializeComponent

            SharedPathBox.IsReadOnly = true;
            SharedPathBox.Background = (Brush)FindResource("BrushSectionHeader");
            // EditMappingsButton.IsEnabled and label are controlled by RefreshMappingStatus()
            // based on the resolved mapping file and its MappingHealth.
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

                EditMappingsButton.IsEnabled = result.CanEdit;
                SetEditMappingsButtonLabel(result);

                var config = TryGetConfig();
                bool hasSharedPath = config != null && !string.IsNullOrEmpty(config.MappingSourcePath);
                SharedRadio.IsChecked = hasSharedPath;
                LocalRadio.IsChecked  = !hasSharedPath;

                var stripeSeverity = result.Health switch
                {
                    MappingHealth.Healthy      => StatusSeverity.Success,
                    MappingHealth.NeedsUpgrade => StatusSeverity.Warning,
                    MappingHealth.NewerSchema  => StatusSeverity.Warning,
                    _                          => StatusSeverity.Error,
                };

                MappingStatusStripe.Background = StatusSeverityToBrush(this, stripeSeverity);
                _mappingStatusDetail           = result.FullStatusMessage;
                MappingStatusText.Text         = _mappingStatusDetail;
                MappingStatusText.ToolTip      = _mappingStatusDetail;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                return ShowInvalidMappingStatus(ex.Message);
            }
            catch (Exception ex)
            {
                return ShowInvalidMappingStatus($"Failed to load the Property Mapping file: {ex.Message}");
            }
        }

        private void SetEditMappingsButtonLabel(MappingResult result)
        {
            if (EditMappingsButtonText == null) return;   // guard during InitializeComponent

            EditMappingsButtonText.Text =
                result.Source == MappingSource.Local ? "Edit Local Mappings" : "Edit Shared Mappings";
        }

        private bool ShowInvalidMappingStatus(string detail)
        {
            var result = new MappingResult(MappingHealth.Invalid,
                                           PropertyMappingConfig.WithDefaults(),
                                           detail);

            EditMappingsButton.IsEnabled   = false;
            MappingStatusStripe.Background = StatusSeverityToBrush(this, StatusSeverity.Error);
            _mappingStatusDetail           = result.FullStatusMessage;
            MappingStatusText.Text         = _mappingStatusDetail;
            MappingStatusText.ToolTip      = _mappingStatusDetail;
            return false;
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
                SharedMappingPath        = sharedPath,
                BomKeyword               = BomKeywordBox.Text,
                WaitForServerAssignedIpn = _savedWaitForServerAssignedIpn,
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
            this.Dispatcher.Invoke(() => SetActionStatus("\u2713  Settings applied.", StatusSeverity.Success));
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
                this.Dispatcher.Invoke(() => SetActionStatus(ex.Message, StatusSeverity.Error));
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
                    this.Dispatcher.Invoke(() =>
                        SetActionStatus(_mappingStatusDetail ?? MappingStatusText.Text, StatusSeverity.Error));
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => SetActionStatus($"Failed to load the Property Mapping file: {ex.Message}", StatusSeverity.Error));
                return false;
            }

            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    MappingApplied?.Invoke(this, _mappingProvider);
                    _savedSnapshot = CaptureSnapshot();
                    RefreshButtonStates();
                    SetActionStatus("\u2713  Settings applied.", StatusSeverity.Success);
                });
                return true;
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => SetActionStatus($"Failed to apply settings: {ex.Message}", StatusSeverity.Error));
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
                    SetConnectionStatus("\u2713  Connection successful.", StatusSeverity.Success));
            }
            catch (InvalidOperationException ex)
            {
                this.Dispatcher.Invoke(() => SetConnectionStatus(ex.Message, StatusSeverity.Error));
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() =>
                    SetConnectionStatus($"Connection failed: {ex.Message}", StatusSeverity.Error));
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

        // ── Status bars ───────────────────────────────────────────────────────

        // Server-connection results live beside the Test Connection button; Apply/Save
        // results live in the status bar next to the action buttons (ADR-0018).
        internal void SetConnectionStatus(string text, StatusSeverity severity) =>
            SetStatusBar(ConnectionStatusText, ConnectionStatusStripe, text, severity);

        internal void SetActionStatus(string text, StatusSeverity severity) =>
            SetStatusBar(ActionStatusText, ActionStatusStripe, text, severity);

        private static Brush StatusSeverityToBrush(FrameworkElement element, StatusSeverity severity) =>
            (Brush)element.FindResource(severity switch
            {
                StatusSeverity.Success => "BrushStatusSuccess",
                StatusSeverity.Warning => "BrushStatusWarning",
                StatusSeverity.Error   => "BrushStatusError",
                _                      => "BrushStatusNone",
            });

        private void SetStatusBar(System.Windows.Controls.TextBox textBox, System.Windows.Controls.Border stripe,
                                  string text, StatusSeverity severity)
        {
            textBox.Text    = text;
            textBox.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            stripe.Background = StatusSeverityToBrush(this, severity);
        }
    }
}
