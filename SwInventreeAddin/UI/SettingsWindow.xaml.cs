using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SwInventreeAddin;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Settings dialog — server credentials (upper section) + property mapping
    /// (lower section). The credential section follows prototype 1b: a fixed
    /// three-line status card whenever anything is saved, and a single plain
    /// form (URL + username/password or API key) with no mode switcher.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider _configProvider;
        private readonly ISettingsApplyService _settingsApplyService;
        private readonly IVersionInfo _versionInfo;
        private readonly IMappingProviderFactory _mappingProviderFactory;
        private IPropertyMappingProvider _mappingProvider;

        private SettingsSnapshot _savedSnapshot;
        private readonly bool _savedWaitForServerAssignedIpn = true;
        private CredentialEditorState _credentialState;
        private ServerConfig? _savedConfig;
        private ConnectionProbeResult? _lastProbe;
        private bool _probeInFlight;

        // Configured state only: which slice of the form a toolbar click revealed.
        // Both are forced open whenever the config is incomplete.
        private bool _editingUrl;
        private bool _showCredentialForm;

        private MappingChangedSubscription? _mappingChangedSubscription;
        private string? _mappingStatusDetail;

        // Lifecycle for the probe fired on open: Closed cancels it, and so does
        // any newer user-initiated probe whose verdict supersedes it.
        private readonly CancellationTokenSource _openProbeCts = new CancellationTokenSource();

        /// <summary>
        /// The probe fired on open when a full config is saved — null when no
        /// probe started. Never faults; completes once the verdict is applied
        /// or discarded. Tests await it for a deterministic settle point.
        /// </summary>
        internal Task? OpenProbeTask { get; private set; }

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
            _configProvider = configProvider;
            _mappingProvider = mappingProvider;
            _versionInfo = versionInfo;
            _settingsApplyService = settingsApplyService;
            _mappingProviderFactory = mappingProviderFactory;
            DataContext = _versionInfo;

            InitializeComponent();

            UrlBox.TextChanged += (_, __) => RefreshButtonStates();
            UsernameBox.TextChanged += (_, __) => RefreshButtonStates();
            PasswordBox.PasswordChanged += (_, __) => RefreshButtonStates();
            ApiKeyBox.PasswordChanged += (_, __) => OnApiKeyEdited();
            SharedPathBox.TextChanged += (_, __) => RefreshButtonStates();
            BomKeywordBox.TextChanged += (_, __) => RefreshButtonStates();
            LocalRadio.Checked += (_, __) => RefreshButtonStates();
            SharedRadio.Checked += (_, __) => RefreshButtonStates();

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());

            _savedConfig = TryGetConfig();
            _credentialState = CredentialEditorState.FromSavedConfig(_savedConfig);

            if (_savedConfig != null)
            {
                UrlBox.Text = _savedConfig.Url ?? string.Empty;

                if (!string.IsNullOrEmpty(_savedConfig.MappingSourcePath))
                    SharedPathBox.Text = _savedConfig.MappingSourcePath;

                BomKeywordBox.Text = _savedConfig.BomKeyword ?? "inventree";
                _savedWaitForServerAssignedIpn = _savedConfig.WaitForServerAssignedIpn;
            }

            ReloadConfigAndRefreshCard();
            RenderCredentialForm();
            UpdateApiKeyPlaceholders();

            // Show local path (read-only, copyable)
            LocalPathBox.Text = _mappingProvider.LocalFilePath;

            // Set Edit Mappings button state and mapping status bar
            RefreshMappingStatus();
            AttachMappingChanged();
            Closed += (_, __) =>
            {
                // IsCancellationRequested stays readable after Dispose, so a
                // late probe continuation still discards cleanly.
                _openProbeCts.Cancel();
                _openProbeCts.Dispose();
                DetachMappingChanged();
            };

            _savedSnapshot = CaptureSnapshot();
            RefreshButtonStates();

            StartOpenProbe();
        }

        // ── Dirty-state tracking ───────────────────────────────────────────────

        private SettingsSnapshot CaptureSnapshot() =>
            new SettingsSnapshot(
                url: UrlBox.Text.Trim(),
                apiKeyDraft: ApiKeyBox.Password.Trim(),
                hasSavedApiKey: _credentialState.HasSavedApiKey,
                username: UsernameBox.Text.Trim(),
                password: PasswordBox.Password,
                sharedPath: SharedPathBox.Text.Trim(),
                bomKeyword: BomKeywordBox.Text.Trim(),
                useLocalMapping: LocalRadio.IsChecked == true,
                waitForServerAssignedIpn: _savedWaitForServerAssignedIpn);

        // The URL under test: the trimmed draft, else the saved URL. The URL
        // field is hidden in the configured state, so both the Test button's
        // enable check and the probe input fall back to what is on disk.
        private string EffectiveUrl
        {
            get
            {
                string typed = UrlBox.Text.Trim();
                return typed.Length > 0 ? typed : (_savedConfig?.Url ?? string.Empty);
            }
        }

        private void RefreshButtonStates()
        {
            bool isDirty = CaptureSnapshot().HasPersistableChangeFrom(_savedSnapshot);
            ApplyButton.IsEnabled = isDirty;
            SaveButton.IsEnabled = isDirty;
            CancelButtonText.Text = isDirty ? "Cancel" : "Close";
            TestConnectionButton.IsEnabled = !string.IsNullOrWhiteSpace(EffectiveUrl);
        }

        // ── Connection status card ─────────────────────────────────────────────

        // The card holds the persistent state: what is saved plus the session's
        // probe axis. The footer status bar reports what the last action did —
        // they are deliberately separate (prototype 1b).
        private void ReloadConfigAndRefreshCard()
        {
            _savedConfig = TryGetConfig();
            var status = ServerConnectionStatus.From(_savedConfig, _lastProbe, _probeInFlight);

            ConnectionCard.Visibility = status.IsSaved ? Visibility.Visible : Visibility.Collapsed;
            ConnectionCardTitle.Text = status.Title;
            ConnectionCardServer.Text = status.ServerLine;
            ConnectionCardCredential.Text = status.CredentialLine;
            ConnectionCardConnection.Text = status.ConnectionLine;
            ConnectionCardToolbar.Visibility =
                status.IsComplete ? Visibility.Visible : Visibility.Collapsed;

            SetStatusDot(status.Indicator);
        }

        // Colour never carries meaning alone — Title always names the state.
        private void SetStatusDot(ServerConnectionIndicator indicator)
        {
            switch (indicator)
            {
                case ServerConnectionIndicator.NotTested:
                    ConnectionStatusDot.Fill = Brushes.Transparent;
                    ConnectionStatusDot.Stroke = (Brush)FindResource("BrushStatusNotTested");
                    break;
                case ServerConnectionIndicator.Testing:
                    ConnectionStatusDot.Fill = (Brush)FindResource("BrushAccentBlue");
                    ConnectionStatusDot.Stroke = Brushes.Transparent;
                    break;
                case ServerConnectionIndicator.Connected:
                    ConnectionStatusDot.Fill = (Brush)FindResource("BrushStatusSuccess");
                    ConnectionStatusDot.Stroke = Brushes.Transparent;
                    break;
                case ServerConnectionIndicator.Failed:
                    ConnectionStatusDot.Fill = (Brush)FindResource("BrushStatusError");
                    ConnectionStatusDot.Stroke = Brushes.Transparent;
                    break;
                default: // AuthenticationRequired
                    ConnectionStatusDot.Fill = (Brush)FindResource("BrushStatusWarning");
                    ConnectionStatusDot.Stroke = Brushes.Transparent;
                    break;
            }
        }

        // ── Probe on open (#234) ─────────────────────────────────────────────
        // A complete saved config gets a live probe on every open — the card
        // reports a real verdict, never a stale saved claim. The probe runs
        // against the saved values, not the form fields, and its lifecycle is
        // the window's: Close and any newer probe cancel it, and a late
        // verdict is discarded.

        private void StartOpenProbe()
        {
            if (!ServerConnectionStatus.From(_savedConfig).IsComplete)
                return;

            var input = new SettingsApplyInput
            {
                Url = _savedConfig!.Url,
                RawApiKey = _savedConfig.ApiKey,
            };

            _probeInFlight = true;
            ReloadConfigAndRefreshCard();
            OpenProbeTask = RunOpenProbeAsync(input);
        }

        private async Task RunOpenProbeAsync(SettingsApplyInput input)
        {
            ConnectionProbeResult? result;
            try
            {
                using (var client = new HttpClient())
                {
                    result = await _settingsApplyService
                        .TestConnectionAsync(input, client, _openProbeCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Closed or superseded by a newer probe — discard silently.
                return;
            }
            catch (Exception ex)
            {
                // The probe could not run to a verdict (e.g. an unparsable saved
                // URL) — surface it like any other failure instead of leaving
                // the card on Testing forever.
                result = new ConnectionProbeResult(ConnectionProbeStatus.Unreachable, ex.Message);
            }

            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    // Closed or superseded between verdict and application —
                    // the newer probe owns the card now.
                    if (_openProbeCts.IsCancellationRequested)
                        return;

                    _lastProbe = result;
                    _probeInFlight = false;
                    ReloadConfigAndRefreshCard();
                });
            }
            catch (Exception)
            {
                // The window's dispatcher is gone — nothing left to report to.
            }
        }

        // ── Single credential form ───────────────────────────────────────────

        // When the saved config is incomplete the form is always open — there is
        // nothing to summarise yet. Once complete, the card toolbar reveals just
        // the slice being changed.
        private void RenderCredentialForm()
        {
            var status = ServerConnectionStatus.From(_savedConfig, _lastProbe, _probeInFlight);
            bool forced = !status.IsComplete;

            bool showUrl = forced || _editingUrl;
            bool showCredentials = forced || _showCredentialForm;

            CredentialFormScroll.Visibility =
                (showUrl || showCredentials) ? Visibility.Visible : Visibility.Collapsed;
            UrlFieldPanel.Visibility = showUrl ? Visibility.Visible : Visibility.Collapsed;
            CredentialFieldsPanel.Visibility =
                showCredentials ? Visibility.Visible : Visibility.Collapsed;
        }

        // Shared tail for paths that persist or remove the saved config: the
        // key draft is dropped (saved keys are never re-shown), the form
        // collapses back to the card, and everything re-reads from disk state.
        private void CollapseFormAndRefresh()
        {
            _editingUrl = false;
            _showCredentialForm = false;
            ApiKeyBox.Clear();
            ReloadConfigAndRefreshCard();
            RenderCredentialForm();
            UpdateApiKeyPlaceholders();
            _savedSnapshot = CaptureSnapshot();
            RefreshButtonStates();
        }

        private void ChangeServer_Click(object sender, RoutedEventArgs e)
        {
            _editingUrl = !_editingUrl;
            _showCredentialForm = false;
            RenderCredentialForm();
            if (_editingUrl) UrlBox.Focus();
        }

        private void ChangeCredential_Click(object sender, RoutedEventArgs e)
        {
            _showCredentialForm = !_showCredentialForm;
            _editingUrl = false;
            RenderCredentialForm();
            if (_showCredentialForm) ApiKeyBox.Focus();
        }

        // ── API key field: write-once draft + placeholders ───────────────────

        private void OnApiKeyEdited()
        {
            _credentialState.ApiKey = ApiKeyBox.Password;
            UpdateApiKeyPlaceholders();
            RefreshButtonStates();
        }

        // Dots stand in for a saved key — never the key itself. With no saved key
        // the field prompts for a paste. Typing hides either overlay.
        private void UpdateApiKeyPlaceholders()
        {
            bool empty = ApiKeyBox.Password.Length == 0;
            ApiKeyDotsPlaceholder.Visibility =
                empty && _credentialState.HasSavedApiKey
                    ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyPromptPlaceholder.Visibility =
                empty && !_credentialState.HasSavedApiKey
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Remove API key ───────────────────────────────────────────────────

        // Removing the API key goes through the apply service so every settings
        // mutation surfaces as a SettingsApplyException with a consistent message
        // prefix. Only the credential is cleared — the saved URL, Property Mapping
        // path, and BOM keyword survive, and the card lands on
        // Authentication required with the form open.
        private async void RemoveApiKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _settingsApplyService.RemoveApiKeyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => SetActionStatus(ex.Message, StatusSeverity.Error));
                return;
            }

            this.Dispatcher.Invoke(() =>
            {
                _credentialState.Clear();
                _lastProbe = null;
                UsernameBox.Clear();
                PasswordBox.Clear();
                CollapseFormAndRefresh();
                SetActionStatus("Credential removed. Server address kept.", StatusSeverity.Success);
            });
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
                Title = "Select shared mapping file",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
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
                LocalRadio.IsChecked = !hasSharedPath;

                var stripeSeverity = result.Health switch
                {
                    MappingHealth.Healthy => StatusSeverity.Success,
                    MappingHealth.NeedsUpgrade => StatusSeverity.Warning,
                    MappingHealth.NewerSchema => StatusSeverity.Warning,
                    _ => StatusSeverity.Error,
                };

                MappingStatusStripe.Background = StatusSeverityToBrush(this, stripeSeverity);
                _mappingStatusDetail = result.FullStatusMessage;
                MappingStatusText.Text = _mappingStatusDetail;
                MappingStatusText.ToolTip = _mappingStatusDetail;
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

            EditMappingsButton.IsEnabled = false;
            MappingStatusStripe.Background = StatusSeverityToBrush(this, StatusSeverity.Error);
            _mappingStatusDetail = result.FullStatusMessage;
            MappingStatusText.Text = _mappingStatusDetail;
            MappingStatusText.ToolTip = _mappingStatusDetail;
            return false;
        }

        private ServerConfig? TryGetConfig()
        {
            try { return _configProvider.GetServerConfig(); }
            catch { return null; }
        }

        // ── Shared helper ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds a <see cref="SettingsApplyInput"/> from the current UI fields.
        /// Credential precedence lives in <see cref="CredentialEditorState.ApplyCredentialTo"/> —
        /// typed key draft wins, then a complete username/password pair, then the saved key.
        /// </summary>
        private SettingsApplyInput BuildInput()
        {
            string? sharedPath = (SharedRadio.IsChecked == true)
                ? (string.IsNullOrWhiteSpace(SharedPathBox.Text) ? null : SharedPathBox.Text.Trim())
                : null;

            var input = new SettingsApplyInput
            {
                Url = UrlBox.Text.Trim(),
                SharedMappingPath = sharedPath,
                BomKeyword = BomKeywordBox.Text,
                WaitForServerAssignedIpn = _savedWaitForServerAssignedIpn,
            };

            _credentialState.ApplyCredentialTo(input, UsernameBox.Text, PasswordBox.Password);
            return input;
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
            await ApplySettingsAsync();
        }

        // ── Shared settings save + notify ─────────────────────────────────────

        /// <summary>
        /// Resolves credentials, persists server config, rebuilds the mapping provider,
        /// refreshes the status card, and fires <see cref="MappingApplied"/>.
        /// Returns <c>true</c> once the settings are persisted — a failed connection
        /// probe is reported as the outcome, not an apply failure; <c>false</c> only
        /// when an error was shown to the user.
        /// </summary>
        public async Task<bool> ApplySettingsAsync()
        {
            // The apply's own probe verdict supersedes the open probe's.
            _openProbeCts.Cancel();

            var input = BuildInput();

            ConnectionProbeResult probe;
            _probeInFlight = true;
            ReloadConfigAndRefreshCard();
            // The save happens inside ApplyAsync — the interim footer must not
            // claim a save that a pre-persistence failure would disprove.
            SetActionStatus("Saving settings and testing connection\u2026", StatusSeverity.None);
            try
            {
                using (var client = new HttpClient())
                {
                    probe = await _settingsApplyService.ApplyAsync(input, client)
                                                       .ConfigureAwait(false);
                }
            }
            catch (SettingsApplyException ex)
            {
                _probeInFlight = false;
                this.Dispatcher.Invoke(() =>
                {
                    ReloadConfigAndRefreshCard();
                    SetActionStatus(ex.Message, StatusSeverity.Error);
                });
                return false;
            }

            _probeInFlight = false;
            _lastProbe = probe;

            // The password never lingers — whether it was sent for token
            // resolution or shadowed by a winning key draft.
            this.Dispatcher.Invoke(() => PasswordBox.Clear());

            try
            {
                _mappingProvider = _mappingProviderFactory.Create(input.SharedMappingPath);

                bool mappingOk = this.Dispatcher.Invoke(() => RefreshMappingStatus());

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

                    // Persist happened: re-read so the card, credential state, and
                    // placeholders reflect what is now on disk. A typed key draft is
                    // cleared — saved keys are never re-shown.
                    _credentialState = CredentialEditorState.FromSavedConfig(TryGetConfig());
                    CollapseFormAndRefresh();
                    SetActionStatus(
                        probe.Succeeded
                            ? "Saved \u2014 connection successful."
                            : $"Saved \u2014 but the connection failed ({probe.Message})",
                        probe.Succeeded ? StatusSeverity.Success : StatusSeverity.Error);
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

        // ── Test connection ───────────────────────────────────────────────────

        // Test never saves — it probes with the effective credential and reports
        // on the footer status bar while the card takes the probe outcome.
        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            // A user-initiated probe supersedes the open probe — the card
            // should settle on the freshest verdict.
            _openProbeCts.Cancel();

            var input = BuildInput();
            input.Url = EffectiveUrl;

            _probeInFlight = true;
            ReloadConfigAndRefreshCard();
            SetActionStatus("Testing connection\u2026", StatusSeverity.None);

            try
            {
                ConnectionProbeResult result;
                using (var client = new HttpClient())
                {
                    result = await _settingsApplyService.TestConnectionAsync(
                            input, client, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                _probeInFlight = false;
                _lastProbe = result;

                this.Dispatcher.Invoke(() =>
                {
                    ReloadConfigAndRefreshCard();
                    RenderCredentialForm();
                    SetActionStatus(
                        result.Succeeded ? result.Message : $"Connection failed. {result.Message}",
                        result.Succeeded ? StatusSeverity.Success : StatusSeverity.Error);

                    // The password never lingers — whether it was sent for
                    // token resolution or shadowed by a winning key draft.
                    PasswordBox.Clear();
                });
            }
            catch (InvalidOperationException ex)
            {
                _probeInFlight = false;
                this.Dispatcher.Invoke(() =>
                {
                    ReloadConfigAndRefreshCard();
                    SetActionStatus(ex.Message, StatusSeverity.Error);
                });
            }
            catch (Exception ex)
            {
                _probeInFlight = false;
                this.Dispatcher.Invoke(() =>
                {
                    ReloadConfigAndRefreshCard();
                    SetActionStatus($"Connection failed: {ex.Message}", StatusSeverity.Error);
                });
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

        // One footer status bar reports what the last action did (ADR-0018); the
        // status card above holds the persistent server state.
        internal void SetActionStatus(string text, StatusSeverity severity) =>
            SetStatusBar(ActionStatusText, ActionStatusStripe, text, severity);

        private static Brush StatusSeverityToBrush(FrameworkElement element, StatusSeverity severity) =>
            (Brush)element.FindResource(severity switch
            {
                StatusSeverity.Success => "BrushStatusSuccess",
                StatusSeverity.Warning => "BrushStatusWarning",
                StatusSeverity.Error => "BrushStatusError",
                _ => "BrushStatusNone",
            });

        private void SetStatusBar(System.Windows.Controls.TextBox textBox, System.Windows.Controls.Border stripe,
                                  string text, StatusSeverity severity)
        {
            textBox.Text = text;
            textBox.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            stripe.Background = StatusSeverityToBrush(this, severity);
        }
    }
}
