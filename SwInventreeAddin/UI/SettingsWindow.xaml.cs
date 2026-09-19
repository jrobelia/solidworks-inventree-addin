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
        private readonly SettingsViewModel _vm;
        private readonly ISettingsApplyService _settingsApplyService;
        private readonly IVersionInfo _versionInfo;
        private readonly IMappingProviderFactory _mappingProviderFactory;
        private IPropertyMappingProvider _mappingProvider;

        private MappingChangedSubscription? _mappingChangedSubscription;
        private string? _mappingStatusDetail;

        /// <summary>
        /// The probe fired on open when a full config is saved — owned by the
        /// VM; forwarded here so tests keep their deterministic settle point.
        /// </summary>
        internal Task? OpenProbeTask => _vm.OpenProbeTask;

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
            _vm = new SettingsViewModel(configProvider, settingsApplyService);
            _mappingProvider = mappingProvider;
            _versionInfo = versionInfo;
            _settingsApplyService = settingsApplyService;
            _mappingProviderFactory = mappingProviderFactory;
            DataContext = _versionInfo;

            InitializeComponent();

            // VM state changes re-render the card and the form: every change
            // raises StatusCard and the three form outputs together, so one
            // pass is always coherent.
            _vm.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.StatusCard):
                    case nameof(SettingsViewModel.UrlSliceOpen):
                    case nameof(SettingsViewModel.CredentialSliceOpen):
                    case nameof(SettingsViewModel.CredentialFormOpen):
                        RefreshStatusCard();
                        RenderCredentialForm();
                        break;
                }
            };

            // Field events push drafts into the VM; every VM read below pulls
            // the derived outputs back out for enablement, labels, and visibility.
            UrlBox.TextChanged += (_, __) => { _vm.Url = UrlBox.Text; RefreshButtonStates(); };
            UsernameBox.TextChanged += (_, __) => { _vm.Username = UsernameBox.Text; RefreshButtonStates(); };
            PasswordBox.PasswordChanged += (_, __) => { _vm.Password = PasswordBox.Password; RefreshButtonStates(); };
            ApiKeyBox.PasswordChanged += (_, __) => OnApiKeyEdited();
            SharedPathBox.TextChanged += (_, __) => { _vm.SharedMappingPath = SharedPathBox.Text; RefreshButtonStates(); };
            BomKeywordBox.TextChanged += (_, __) => { _vm.BomKeyword = BomKeywordBox.Text; RefreshButtonStates(); };
            LocalRadio.Checked += (_, __) => { _vm.UseLocalMapping = true; RefreshButtonStates(); };
            SharedRadio.Checked += (_, __) => { _vm.UseLocalMapping = false; RefreshButtonStates(); };

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());

            // Seed the fields from the VM's drafts — the pushes are no-ops.
            UrlBox.Text = _vm.Url;
            SharedPathBox.Text = _vm.SharedMappingPath;
            BomKeywordBox.Text = _vm.BomKeyword;

            RefreshStatusCard();
            RenderCredentialForm();
            UpdateApiKeyPlaceholders();

            // Show local path (read-only, copyable)
            LocalPathBox.Text = _mappingProvider.LocalFilePath;

            // Set Edit Mappings button state and mapping status bar
            RefreshMappingStatus();
            AttachMappingChanged();
            Closed += (_, __) =>
            {
                // The VM cancels and disposes the open probe; a late verdict
                // landing after this is still discarded.
                _vm.CancelOpenProbe();
                DetachMappingChanged();
            };

            RefreshButtonStates();
        }

        // ── Dirty-state tracking ───────────────────────────────────────────────
        // The VM owns the snapshot/diff rules; the window only reads the outputs.

        private void RefreshButtonStates()
        {
            ApplyButton.IsEnabled = _vm.IsDirty;
            SaveButton.IsEnabled = _vm.IsDirty;
            CancelButtonText.Text = _vm.CancelLabel;
            TestConnectionButton.IsEnabled = _vm.TestConnectionEnabled;
        }

        // ── Connection status card ─────────────────────────────────────────────

        // The card holds the persistent state: what is saved crossed with the
        // session's probe axis — the VM computes that projection; this only
        // renders it. The connection section's status bar reports what the
        // last action did — they are deliberately separate (prototype 1b).
        private void RefreshStatusCard()
        {
            var status = _vm.StatusCard;

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

        // ── Single credential form ───────────────────────────────────────────

        // When the saved config is incomplete the form is always open — there is
        // nothing to summarise yet. Once complete, the card toolbar reveals just
        // the slice being changed. The VM owns the flags; this only renders.
        private void RenderCredentialForm()
        {
            CredentialFormScroll.Visibility =
                _vm.CredentialFormOpen ? Visibility.Visible : Visibility.Collapsed;
            UrlFieldPanel.Visibility =
                _vm.UrlSliceOpen ? Visibility.Visible : Visibility.Collapsed;
            CredentialFieldsPanel.Visibility =
                _vm.CredentialSliceOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        // Shared tail for paths that persist or remove the saved config: the
        // key draft is dropped (saved keys are never re-shown) and the field
        // chrome re-renders — card and form re-render on the VM's own
        // notifications, and the lifecycle call that rebaselines happens at
        // the call site.
        private void CollapseFormAndRefresh()
        {
            ApiKeyBox.Clear();
            UpdateApiKeyPlaceholders();
            RefreshButtonStates();
        }

        private void ChangeServer_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ToggleUrlSlice()) UrlBox.Focus();
        }

        private void ChangeCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ToggleCredentialSlice()) ApiKeyBox.Focus();
        }

        // ── API key field: write-once draft + placeholders ───────────────────

        private void OnApiKeyEdited()
        {
            _vm.ApiKeyDraft = ApiKeyBox.Password;
            UpdateApiKeyPlaceholders();
            RefreshButtonStates();
        }

        // Dots stand in for a saved key — never the key itself. With no saved key
        // the field prompts for a paste. Typing hides either overlay.
        private void UpdateApiKeyPlaceholders()
        {
            bool empty = ApiKeyBox.Password.Length == 0;
            ApiKeyDotsPlaceholder.Visibility =
                empty && _vm.HasSavedApiKey
                    ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyPromptPlaceholder.Visibility =
                empty && !_vm.HasSavedApiKey
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
                this.Dispatcher.Invoke(() => ConnectionStatusBar.SetStatus(ex.Message, StatusSeverity.Error));
                return;
            }

            this.Dispatcher.Invoke(() =>
            {
                _vm.OnCredentialRemoved();
                _vm.ClearProbeVerdict();
                UsernameBox.Clear();
                PasswordBox.Clear();
                CollapseFormAndRefresh();
                ConnectionStatusBar.SetStatus("Credential removed. Server address kept.", StatusSeverity.Success);
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

                var config = _vm.SavedConfig;
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

                _mappingStatusDetail = result.FullStatusMessage;
                MappingStatusBar.SetStatus(_mappingStatusDetail, stripeSeverity);
                return result.Health != MappingHealth.Invalid;
            }
            catch (InvalidOperationException ex)
            {
                return ShowInvalidMappingStatus(ex.Message);
            }
            catch (Exception ex)
            {
                return ShowMappingLoadFailure(ex);
            }
        }

        private bool ShowMappingLoadFailure(Exception ex) =>
            ShowInvalidMappingStatus($"Failed to load the Property Mapping file: {ex.Message}");

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
            _mappingStatusDetail = result.FullStatusMessage;
            MappingStatusBar.SetStatus(_mappingStatusDetail, StatusSeverity.Error);
            return false;
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
            var input = _vm.BuildApplyInput();

            // #249: a save that changed nothing connection-relevant skips the
            // probe. The open probe — if still in flight — keeps its claim on
            // the card: it is not cancelled, the in-flight axis is not raised,
            // and the NotProbed result never becomes the card's verdict.
            bool probing = input.ProbeConnection;
            if (probing)
            {
                // The apply's own probe verdict supersedes the open probe's.
                _vm.BeginUserProbe();
                // The save happens inside ApplyAsync — the interim status bar must
                // not claim a save that a pre-persistence failure would disprove.
                ActionStatusBar.SetStatus("Saving settings and testing connection\u2026", StatusSeverity.None);
            }
            else
            {
                ActionStatusBar.SetStatus("Saving settings\u2026", StatusSeverity.None);
            }
            // A new save supersedes any earlier connection-scoped outcome —
            // leaving it would let the section bar contradict the fresh verdict.
            ConnectionStatusBar.SetStatus(string.Empty, StatusSeverity.None);

            ConnectionProbeResult probe;
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
                if (probing) _vm.EndUserProbe(null);
                this.Dispatcher.Invoke(() =>
                {
                    ActionStatusBar.SetStatus(ex.Message, StatusSeverity.Error);
                });
                return false;
            }

            if (probing) _vm.EndUserProbe(probe);

            // The password never lingers — whether it was sent for token
            // resolution or shadowed by a winning key draft.
            this.Dispatcher.Invoke(() =>
            {
                PasswordBox.Clear();
                _vm.ClearSecrets();
            });

            bool mappingOk;
            try
            {
                _mappingProvider = _mappingProviderFactory.Create(input.SharedMappingPath);

                mappingOk = this.Dispatcher.Invoke(() =>
                {
                    // The save persisted — re-read the saved config so the
                    // radios and the card reflect what is now on disk.
                    _vm.ReloadPersistedConfig();
                    return RefreshMappingStatus();
                });

                this.Dispatcher.Invoke(() =>
                {
                    DetachMappingChanged();
                    AttachMappingChanged();
                });
            }
            catch (Exception ex)
            {
                // Mapping detail stays in the Property Mapping section's own
                // status bar — ShowInvalidMappingStatus renders it there.
                this.Dispatcher.Invoke(() => ShowMappingLoadFailure(ex));
                mappingOk = false;
            }

            if (!mappingOk)
            {
                // The save persisted; the mapping bar carries the detail. The
                // footer aggregates both facts — the probe verdict and the
                // mapping failure — so the line is truthful on its own.
                var failure = SettingsViewModel.FormatApplyOutcome(probe, mappingOk: false);
                this.Dispatcher.Invoke(() =>
                {
                    // The provider was swapped even though the file is invalid —
                    // the add-in and Task Pane must track the saved source path;
                    // the Invalid result keeps Part Sync gated off on its own.
                    MappingApplied?.Invoke(this, _mappingProvider);
                    _vm.ReloadPersistedConfig();
                    ActionStatusBar.SetStatus(failure.Text, failure.Severity);
                });
                return false;
            }

            try
            {
                var outcome = SettingsViewModel.FormatApplyOutcome(probe, mappingOk: true);
                this.Dispatcher.Invoke(() =>
                {
                    MappingApplied?.Invoke(this, _mappingProvider);

                    // Persist happened: re-read so the card, credential state, and
                    // placeholders reflect what is now on disk. A typed key draft is
                    // cleared — saved keys are never re-shown.
                    _vm.MarkPersisted();
                    CollapseFormAndRefresh();
                    ActionStatusBar.SetStatus(outcome.Text, outcome.Severity);
                });
                return true;
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() => ActionStatusBar.SetStatus($"Failed to apply settings: {ex.Message}", StatusSeverity.Error));
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
        // on the section status bar while the card takes the probe outcome.
        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var input = _vm.BuildTestInput();

            // A user-initiated probe supersedes the open probe — the card
            // should settle on the freshest verdict.
            _vm.BeginUserProbe();
            ConnectionStatusBar.SetStatus("Testing connection\u2026", StatusSeverity.None);

            try
            {
                ConnectionProbeResult result;
                using (var client = new HttpClient())
                {
                    result = await _settingsApplyService.TestConnectionAsync(
                            input, client, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                _vm.EndUserProbe(result);

                this.Dispatcher.Invoke(() =>
                {
                    ConnectionStatusBar.SetStatus(
                        result.Succeeded ? result.Message : $"Connection failed. {result.Message}",
                        result.Succeeded ? StatusSeverity.Success : StatusSeverity.Error);

                    // The password never lingers — whether it was sent for
                    // token resolution or shadowed by a winning key draft.
                    PasswordBox.Clear();
                    _vm.ClearSecrets();
                });
            }
            catch (InvalidOperationException ex)
            {
                _vm.EndUserProbe(null);
                this.Dispatcher.Invoke(() =>
                {
                    ConnectionStatusBar.SetStatus(ex.Message, StatusSeverity.Error);
                });
            }
            catch (Exception ex)
            {
                _vm.EndUserProbe(null);
                this.Dispatcher.Invoke(() =>
                {
                    ConnectionStatusBar.SetStatus($"Connection failed: {ex.Message}", StatusSeverity.Error);
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

    }
}
