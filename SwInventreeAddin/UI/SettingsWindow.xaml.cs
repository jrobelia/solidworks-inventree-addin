using System;
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
    ///
    /// This code-behind is event wiring and rendering only: field events push
    /// drafts into the <see cref="SettingsViewModel"/>, every derived output
    /// flows back through PropertyChanged into a render method, and the
    /// button handlers forward to the VM's orchestration commands. What stays
    /// here is what is WPF-bound: the file browse dialog, the shared-path
    /// read-only styling, the Property Mappings editor launch, and the API-key
    /// placeholders.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _vm;
        private readonly IVersionInfo _versionInfo;

        /// <summary>
        /// The probe fired on open when a full config is saved — owned by the
        /// VM; forwarded here so tests keep their deterministic settle point.
        /// </summary>
        internal Task? OpenProbeTask => _vm.OpenProbeTask;

        /// <summary>The view model — the residual visual tests' settle point.</summary>
        internal SettingsViewModel ViewModel => _vm;

        /// <summary>
        /// Raised after Apply successfully saves settings, so the caller can update
        /// the live mapping provider without waiting for the dialog to close.
        /// </summary>
        public event EventHandler<IPropertyMappingProvider>? MappingApplied;

        /// <summary>
        /// Raised whenever the VM's connection-state read-model changes — the
        /// #241 reach path for a Task Pane consumer.
        /// </summary>
        public event EventHandler<ServerConnectionStatus>? ConnectionStateChanged;

        internal SettingsWindow(IConfigProvider configProvider,
                                IPropertyMappingProvider mappingProvider,
                                IVersionInfo versionInfo,
                                ISettingsApplyService settingsApplyService,
                                IMappingProviderFactory mappingProviderFactory)
        {
            _vm = new SettingsViewModel(configProvider, settingsApplyService,
                                        mappingProvider, mappingProviderFactory);
            _versionInfo = versionInfo;
            DataContext = _versionInfo;

            InitializeComponent();

            _vm.MappingApplied += (_, provider) => MappingApplied?.Invoke(this, provider);
            _vm.ConnectionStateChanged += (_, status) => ConnectionStateChanged?.Invoke(this, status);

            // Every VM output change re-renders: each derived property flows
            // through PropertyChanged into the matching render method.
            _vm.PropertyChanged += (_, e) => OnViewModelPropertyChanged(e.PropertyName);

            // Field events push drafts into the VM; the derived outputs come
            // back through PropertyChanged.
            UrlBox.TextChanged += (_, __) => _vm.Url = UrlBox.Text;
            UsernameBox.TextChanged += (_, __) => _vm.Username = UsernameBox.Text;
            PasswordBox.PasswordChanged += (_, __) => _vm.Password = PasswordBox.Password;
            ApiKeyBox.PasswordChanged += (_, __) => _vm.ApiKeyDraft = ApiKeyBox.Password;
            SharedPathBox.TextChanged += (_, __) => _vm.SharedMappingPath = SharedPathBox.Text;
            BomKeywordBox.TextChanged += (_, __) => _vm.BomKeyword = BomKeywordBox.Text;
            LocalRadio.Checked += (_, __) => _vm.UseLocalMapping = true;
            SharedRadio.Checked += (_, __) => _vm.UseLocalMapping = false;

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());

            // Seed the fields from the VM's drafts — the pushes are no-ops.
            UrlBox.Text = _vm.Url;
            SharedPathBox.Text = _vm.SharedMappingPath;
            BomKeywordBox.Text = _vm.BomKeyword;

            RefreshStatusCard();
            RenderCredentialForm();
            UpdateApiKeyPlaceholders();
            RenderMappingSection();
            RefreshButtonStates();

            Closed += (_, __) =>
            {
                // The VM cancels the open probe and detaches the
                // mapping-changed subscription — a late verdict landing after
                // this is still discarded.
                _vm.OnClosed();
            };
        }

        // ── Rendering ────────────────────────────────────────────────────────
        // Every derived output the VM raises lands here; each case is a
        // mechanical forward — no rules, no state.

        private void OnViewModelPropertyChanged(string? propertyName)
        {
            switch (propertyName)
            {
                case nameof(SettingsViewModel.StatusCard):
                case nameof(SettingsViewModel.UrlSliceOpen):
                case nameof(SettingsViewModel.CredentialSliceOpen):
                case nameof(SettingsViewModel.CredentialFormOpen):
                    RefreshStatusCard();
                    RenderCredentialForm();
                    break;

                case nameof(SettingsViewModel.ActionStatusText):
                case nameof(SettingsViewModel.ActionStatusSeverity):
                    ActionStatusBar.SetStatus(_vm.ActionStatusText, _vm.ActionStatusSeverity);
                    break;

                case nameof(SettingsViewModel.ConnectionStatusText):
                case nameof(SettingsViewModel.ConnectionStatusSeverity):
                    ConnectionStatusBar.SetStatus(_vm.ConnectionStatusText, _vm.ConnectionStatusSeverity);
                    break;

                case nameof(SettingsViewModel.MappingStatusText):
                case nameof(SettingsViewModel.MappingStatusSeverity):
                    MappingStatusBar.SetStatus(_vm.MappingStatusText, _vm.MappingStatusSeverity);
                    break;

                case nameof(SettingsViewModel.EditMappingsEnabled):
                    EditMappingsButton.IsEnabled = _vm.EditMappingsEnabled;
                    break;

                case nameof(SettingsViewModel.EditMappingsLabel):
                    EditMappingsButtonText.Text = _vm.EditMappingsLabel;
                    break;

                case nameof(SettingsViewModel.LocalMappingPath):
                    LocalPathBox.Text = _vm.LocalMappingPath;
                    break;

                case nameof(SettingsViewModel.UseLocalMapping):
                    LocalRadio.IsChecked = _vm.UseLocalMapping;
                    SharedRadio.IsChecked = !_vm.UseLocalMapping;
                    break;

                case nameof(SettingsViewModel.Username):
                    UsernameBox.Text = _vm.Username;
                    break;

                case nameof(SettingsViewModel.Password):
                    PasswordBox.Password = _vm.Password;
                    break;

                case nameof(SettingsViewModel.ApiKeyDraft):
                    ApiKeyBox.Password = _vm.ApiKeyDraft;
                    UpdateApiKeyPlaceholders();
                    break;

                case nameof(SettingsViewModel.HasSavedApiKey):
                    UpdateApiKeyPlaceholders();
                    break;

                case nameof(SettingsViewModel.IsDirty):
                case nameof(SettingsViewModel.CancelLabel):
                case nameof(SettingsViewModel.TestConnectionEnabled):
                    RefreshButtonStates();
                    break;
            }
        }

        private void RefreshButtonStates()
        {
            ApplyButton.IsEnabled = _vm.IsDirty;
            SaveButton.IsEnabled = _vm.IsDirty;
            CancelButtonText.Text = _vm.CancelLabel;
            TestConnectionButton.IsEnabled = _vm.TestConnectionEnabled;
        }

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

        // When the saved config is incomplete the form is always open — there
        // is nothing to summarise yet. Once complete, the card toolbar reveals
        // just the slice being changed. The VM owns the flags; this only renders.
        private void RenderCredentialForm()
        {
            CredentialFormScroll.Visibility =
                _vm.CredentialFormOpen ? Visibility.Visible : Visibility.Collapsed;
            UrlFieldPanel.Visibility =
                _vm.UrlSliceOpen ? Visibility.Visible : Visibility.Collapsed;
            CredentialFieldsPanel.Visibility =
                _vm.CredentialSliceOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderMappingSection()
        {
            LocalPathBox.Text = _vm.LocalMappingPath;
            LocalRadio.IsChecked = _vm.UseLocalMapping;
            SharedRadio.IsChecked = !_vm.UseLocalMapping;
            EditMappingsButton.IsEnabled = _vm.EditMappingsEnabled;
            EditMappingsButtonText.Text = _vm.EditMappingsLabel;
            MappingStatusBar.SetStatus(_vm.MappingStatusText, _vm.MappingStatusSeverity);
        }

        // ── Card toolbar ─────────────────────────────────────────────────────

        private void ChangeServer_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ToggleUrlSlice()) UrlBox.Focus();
        }

        private void ChangeCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ToggleCredentialSlice()) ApiKeyBox.Focus();
        }

        // ── API key field: write-once draft + placeholders ───────────────────

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

        // ── Radio button styling ─────────────────────────────────────────────
        // WPF-bound chrome: the read-only treatment on the shared-path box
        // follows whichever radio is checked. The radio state itself is a VM
        // draft; enablement and labels flow from the mapping projection.

        private void SharedRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SharedPathBox == null) return;   // guard during InitializeComponent

            SharedPathBox.IsReadOnly = false;
            SharedPathBox.Background = System.Windows.Media.Brushes.White;
        }

        private void LocalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (SharedPathBox == null) return;   // guard during InitializeComponent

            SharedPathBox.IsReadOnly = true;
            SharedPathBox.Background = (Brush)FindResource("BrushSectionHeader");
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
            var editor = new PropertyMappingEditorWindow(_vm.MappingProvider, this);
            editor.ShowDialog();
            _vm.RefreshMappingStatus();
        }

        // ── Orchestration forwards ────────────────────────────────────────────
        // async-void only on event handlers; the VM's commands never throw for
        // service, probe, or mapping failures — they surface through the
        // status pairs instead, so these forwards can never crash the host.

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            await _vm.ApplyAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!await _vm.ApplyAsync()) return;
            DialogResult = true;
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            await _vm.TestConnectionAsync();
        }

        private async void RemoveApiKey_Click(object sender, RoutedEventArgs e)
        {
            await _vm.RemoveApiKeyAsync();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
