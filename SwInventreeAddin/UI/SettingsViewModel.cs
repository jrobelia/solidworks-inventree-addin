using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// The Settings window's rules: credential state, dirty gating, the
    /// status-card projection, the open-probe lifecycle, form-view-state, the
    /// Apply/Test/Remove orchestration, and the Property Mapping section's
    /// status projection + provider lifecycle — everything that used to live
    /// in the code-behind. Pure C# with no WPF types, so tests construct it
    /// without STA. The window pushes field edits into the draft properties,
    /// reads the derived outputs for enablement, labels, and visibility, and
    /// its click handlers only forward to the orchestration commands;
    /// credential precedence and the probe-skip decision (#249) resolve inside
    /// <see cref="BuildApplyInput"/>.
    /// </summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raised after a persisted save, exactly once per <see cref="ApplyAsync"/>,
        /// with the rebuilt mapping provider — or the provider already in place
        /// when the rebuild threw. Even an invalid mapping result propagates:
        /// the add-in tracks the saved source path while
        /// <see cref="MappingHealth.Invalid"/> keeps Part Sync gated.
        /// </summary>
        public event EventHandler<IPropertyMappingProvider>? MappingApplied;

        /// <summary>
        /// Raised whenever the recomputed <see cref="ConnectionState"/> differs
        /// in any field — the #241 reach path for a Task Pane consumer. Neither
        /// #241 consumption option is committed; this only makes the state
        /// reachable.
        /// </summary>
        public event EventHandler<ServerConnectionStatus>? ConnectionStateChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly IConfigProvider _configProvider;
        private readonly ISettingsApplyService _settingsApplyService;
        private readonly IMappingProviderFactory _mappingProviderFactory;
        private IPropertyMappingProvider _mappingProvider;
        private MappingChangedSubscription? _mappingChangedSubscription;

        /// <summary>
        /// UI-thread synchronisation context captured at construction. Null when
        /// constructed on a thread-pool thread (unit tests) — in that case
        /// RunOnUiThread executes actions inline.
        /// </summary>
        private readonly SynchronizationContext? _uiContext;

        // Captured alongside _uiContext so "already on the UI thread" is a
        // thread check, not a context check — inside a Dispatcher.Invoke
        // callback SynchronizationContext.Current is a fresh
        // DispatcherSynchronizationContext wrapper that never reference-equals
        // the captured context, and Send would marshal into a context whose
        // pump is not running.
        private readonly int _uiThreadId;

        // ── State ─────────────────────────────────────────────────────────────

        private ServerConfig? _savedConfig;
        private CredentialEditorState _credentialState;
        private SettingsSnapshot _savedSnapshot;
        private readonly bool _savedWaitForServerAssignedIpn = ServerConfig.DefaultWaitForServerAssignedIpn;

        private string _url = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _sharedMappingPath = string.Empty;
        private string _bomKeyword = string.Empty;
        private bool _useSharedMapping = false;

        // Configured state only: which slice of the form a toolbar click
        // revealed. Both are forced open whenever the config is incomplete.
        private bool _editingUrl;
        private bool _showCredentialForm;

        // The session's connection axis (ADR-0023): the last probe verdict plus
        // the in-flight flag, and the lifecycle for the probe fired on open —
        // CancelOpenProbe and any newer user-initiated probe cancel it, and a
        // verdict landing after cancellation is discarded.
        private ConnectionProbeResult? _lastProbe;
        private bool _probeInFlight;
        private readonly CancellationTokenSource _openProbeCts = new CancellationTokenSource();

        /// <summary>
        /// The probe fired on open when a full config is saved — null when no
        /// probe started. Never faults; completes once the verdict is applied
        /// or discarded. Tests await it for a deterministic settle point.
        /// </summary>
        internal Task? OpenProbeTask { get; private set; }

        // The last <see cref="ConnectionState"/> value handed to
        // <see cref="ConnectionStateChanged"/> subscribers — the event fires
        // only when the recomputed projection differs in a field.
        private ServerConnectionStatus _emittedConnectionState = null!;

        // Status bars: one text + severity pair per bar — the dialog-level
        // Action bar (Apply/Save outcomes) and the connection-scoped bar
        // (Test connection, Remove API key).
        private string _actionStatusText = string.Empty;
        private StatusSeverity _actionStatusSeverity = StatusSeverity.None;
        private string _connectionStatusText = string.Empty;
        private StatusSeverity _connectionStatusSeverity = StatusSeverity.None;

        // Property Mapping section projection.
        private string _mappingStatusText = string.Empty;
        private StatusSeverity _mappingStatusSeverity = StatusSeverity.None;
        private bool _editMappingsEnabled;
        private string _editMappingsLabel = "Edit Mappings";

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Reads the saved <see cref="ServerConfig"/> once — a throwing provider
        /// reads as "nothing saved" — seeds every draft, builds the credential
        /// state, projects the mapping section, subscribes to mapping-changed,
        /// and baselines the dirty snapshot. A complete saved config also
        /// starts the open probe: no caller call, so <see cref="OpenProbeTask"/>
        /// is non-null from then on.
        /// </summary>
        public SettingsViewModel(IConfigProvider configProvider,
                                 ISettingsApplyService settingsApplyService,
                                 IPropertyMappingProvider mappingProvider,
                                 IMappingProviderFactory mappingProviderFactory)
        {
            _configProvider = configProvider;
            _settingsApplyService = settingsApplyService;
            _mappingProvider = mappingProvider;
            _mappingProviderFactory = mappingProviderFactory;
            _uiContext = SynchronizationContext.Current;
            _uiThreadId = Environment.CurrentManagedThreadId;

            _savedConfig = TryGetConfig();
            _credentialState = CredentialEditorState.FromSavedConfig(_savedConfig);

            if (_savedConfig != null)
            {
                _url = _savedConfig.Url ?? string.Empty;
                _sharedMappingPath = _savedConfig.MappingSourcePath ?? string.Empty;
                _bomKeyword = _savedConfig.BomKeyword ?? "inventree";
                _savedWaitForServerAssignedIpn = _savedConfig.WaitForServerAssignedIpn;
                _useSharedMapping = !string.IsNullOrEmpty(_savedConfig.MappingSourcePath);
            }

            _savedSnapshot = CaptureSnapshot();

            ProjectMappingStatus();
            MappingChangedSubscription.SubscribeTo(
                ref _mappingChangedSubscription, _mappingProvider, OnMappingChanged);

            StartOpenProbe();
            _emittedConnectionState = ConnectionState;
        }

        // ── Draft inputs ──────────────────────────────────────────────────────
        // The window pushes raw field text; each set re-evaluates gating and
        // fires PropertyChanged for the derived outputs.

        /// <summary>Server URL draft.</summary>
        public string Url
        {
            get => _url;
            set { if (Set(ref _url, value)) OnDraftsChanged(); }
        }

        /// <summary>Username draft for token-based sign-in.</summary>
        public string Username
        {
            get => _username;
            set { if (Set(ref _username, value)) OnDraftsChanged(); }
        }

        /// <summary>Password draft — never persisted; cleared after Apply and Test.</summary>
        public string Password
        {
            get => _password;
            set { if (Set(ref _password, value)) OnDraftsChanged(); }
        }

        /// <summary>
        /// Typed API-key draft only — never the saved key. Lives in the credential
        /// state so a post-persistence rebuild clears it in one step.
        /// </summary>
        public string ApiKeyDraft
        {
            get => _credentialState.ApiKey;
            set
            {
                string draft = value ?? string.Empty;
                if (_credentialState.ApiKey == draft) return;
                _credentialState.ApiKey = draft;
                Raise(nameof(ApiKeyDraft));
                OnDraftsChanged();
            }
        }

        /// <summary>Shared Property Mapping path draft.</summary>
        public string SharedMappingPath
        {
            get => _sharedMappingPath;
            set { if (Set(ref _sharedMappingPath, value)) OnDraftsChanged(); }
        }

        /// <summary>BOM Keyword draft.</summary>
        public string BomKeyword
        {
            get => _bomKeyword;
            set { if (Set(ref _bomKeyword, value)) OnDraftsChanged(); }
        }

        /// <summary>True → the shared Property Mapping path is used and persisted; false → the local file.</summary>
        public bool UseSharedMapping
        {
            get => _useSharedMapping;
            set { if (Set(ref _useSharedMapping, value)) OnDraftsChanged(); }
        }

        // ── Derived outputs ───────────────────────────────────────────────────

        /// <summary>True when a persistable change exists — Apply and Save both enable on it.</summary>
        public bool IsDirty => CaptureSnapshot().HasPersistableChangeFrom(_savedSnapshot);

        /// <summary>"Cancel" while dirty, "Close" once the dialog is clean.</summary>
        public string CancelLabel => IsDirty ? "Cancel" : "Close";

        /// <summary>
        /// The URL under test: the trimmed draft, else the saved URL. The URL
        /// field is hidden in the configured state, so both the Test button's
        /// enable check and the probe input fall back to what is on disk.
        /// </summary>
        public string EffectiveUrl
        {
            get
            {
                string typed = _url.Trim();
                return typed.Length > 0 ? typed : (_savedConfig?.Url ?? string.Empty);
            }
        }

        /// <summary>Test connection needs some URL to probe — draft or saved.</summary>
        public bool TestConnectionEnabled => !string.IsNullOrWhiteSpace(EffectiveUrl);

        /// <summary>
        /// A key is persisted. The window composes this with an empty
        /// <see cref="ApiKeyDraft"/> for dots-vs-prompt — the key itself is never readable.
        /// </summary>
        public bool HasSavedApiKey => _credentialState.HasSavedApiKey;

        /// <summary>
        /// The persisted config — feeds <see cref="StatusCard"/> and the
        /// window's mapping-radio refresh.
        /// </summary>
        public ServerConfig? SavedConfig => _savedConfig;

        // ── Status card ───────────────────────────────────────────────────────
        // The card holds the persistent state: what is saved crossed with the
        // session's probe axis (ADR-0023). The projection is computed here;
        // the window renders it mechanically — text, IsSaved/IsComplete →
        // Visibility, Indicator → dot brush — and never re-derives it.

        /// <summary>
        /// The whole status-card projection: title, the three lines, the dot
        /// state, and card/toolbar visibility. Never null; recomputed whenever
        /// the saved config or the probe axis moves.
        /// </summary>
        public ServerConnectionStatus StatusCard =>
            ServerConnectionStatus.From(_savedConfig, _lastProbe, _probeInFlight);

        /// <summary>
        /// The connection state as a public read-model — the same
        /// <see cref="ServerConnectionStatus"/> value the status-card
        /// projection derives from; <see cref="IsSaved"/>,
        /// <see cref="ServerConnectionStatus.IsComplete"/>, and
        /// <see cref="ServerConnectionStatus.Indicator"/> are the
        /// Task-Pane-relevant members. Changes are announced through
        /// <see cref="ConnectionStateChanged"/>.
        /// </summary>
        public ServerConnectionStatus ConnectionState => StatusCard;

        // ── Status bar pairs ─────────────────────────────────────────────
        // One text + severity pair per status bar. The window forwards each
        // pair into its StatusBarControl; the commands below are the only
        // writers, so a failure can never leave the bars stale.

        /// <summary>The dialog-level action bar (Apply/Save outcomes).</summary>
        public string ActionStatusText
        {
            get => _actionStatusText;
            private set => Set(ref _actionStatusText, value);
        }

        /// <summary>Severity for <see cref="ActionStatusText"/>.</summary>
        public StatusSeverity ActionStatusSeverity
        {
            get => _actionStatusSeverity;
            private set => Set(ref _actionStatusSeverity, value);
        }

        /// <summary>The connection-scoped bar (Test connection, Remove API key).</summary>
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            private set => Set(ref _connectionStatusText, value);
        }

        /// <summary>Severity for <see cref="ConnectionStatusText"/>.</summary>
        public StatusSeverity ConnectionStatusSeverity
        {
            get => _connectionStatusSeverity;
            private set => Set(ref _connectionStatusSeverity, value);
        }

        // ── Property Mapping section ─────────────────────────────────────
        // The mapping-status projection: the current provider's health
        // crossed with the persisted source path. Re-projected on open, on
        // mapping-changed, after a save rebuild, and on demand when the
        // window's editor launch closes.

        /// <summary>
        /// The mapping provider currently in place — rebuilt through
        /// <see cref="IMappingProviderFactory"/> on every save, so the
        /// window's editor launch edits the live one.
        /// </summary>
        public IPropertyMappingProvider MappingProvider => _mappingProvider;

        /// <summary>The Property Mapping section's status text.</summary>
        public string MappingStatusText
        {
            get => _mappingStatusText;
            private set => Set(ref _mappingStatusText, value);
        }

        /// <summary>Severity for <see cref="MappingStatusText"/>.</summary>
        public StatusSeverity MappingStatusSeverity
        {
            get => _mappingStatusSeverity;
            private set => Set(ref _mappingStatusSeverity, value);
        }

        /// <summary>Whether the Edit Mappings button may be used — the mapping must be editable.</summary>
        public bool EditMappingsEnabled
        {
            get => _editMappingsEnabled;
            private set => Set(ref _editMappingsEnabled, value);
        }

        /// <summary>"Edit Local Mappings" / "Edit Shared Mappings" per the resolved source.</summary>
        public string EditMappingsLabel
        {
            get => _editMappingsLabel;
            private set => Set(ref _editMappingsLabel, value);
        }

        /// <summary>The local mapping file path — re-notified on a provider swap.</summary>
        public string LocalMappingPath => _mappingProvider.LocalFilePath;

        // ── Form view-state ───────────────────────────────────────────────────
        // The form is forced open while the saved config is incomplete — there
        // is nothing to summarise yet. Once complete, the card toolbar reveals
        // just the slice being changed (mutual exclusion).

        private bool ForcedOpen => !StatusCard.IsComplete;

        /// <summary>The Server URL slice renders when forced open or being edited.</summary>
        public bool UrlSliceOpen => ForcedOpen || _editingUrl;

        /// <summary>The credential slice renders when forced open or being edited.</summary>
        public bool CredentialSliceOpen => ForcedOpen || _showCredentialForm;

        /// <summary>The credential form container renders when either slice does.</summary>
        public bool CredentialFormOpen => UrlSliceOpen || CredentialSliceOpen;

        // ── Form commands ─────────────────────────────────────────────────────

        /// <summary>Toggles the URL slice; the credential slice closes — the two never show together. Returns whether the slice is now open.</summary>
        internal bool ToggleUrlSlice()
        {
            _editingUrl = !_editingUrl;
            _showCredentialForm = false;
            RaiseCardAndFormChanged();
            return UrlSliceOpen;
        }

        /// <summary>Toggles the credential slice; the URL slice closes — the two never show together. Returns whether the slice is now open.</summary>
        internal bool ToggleCredentialSlice()
        {
            _showCredentialForm = !_showCredentialForm;
            _editingUrl = false;
            RaiseCardAndFormChanged();
            return CredentialSliceOpen;
        }

        /// <summary>
        /// Clears both edit flags — the post-apply/remove landing. The
        /// forced-open rule is unaffected: an incomplete saved config keeps
        /// the form open.
        /// </summary>
        internal void CollapseCredentialForm()
        {
            _editingUrl = false;
            _showCredentialForm = false;
            RaiseCardAndFormChanged();
        }

        // ── Apply seam ────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the apply input with credential precedence resolved inside
        /// (<see cref="CredentialEditorState"/>: key draft &gt; complete pair &gt;
        /// saved key; half-typed pairs ignored) and <see cref="SettingsApplyInput.ProbeConnection"/>
        /// set from the connection-fields-changed predicate (#249).
        /// </summary>
        public SettingsApplyInput BuildApplyInput()
        {
            string? sharedPath = _useSharedMapping
                ? (string.IsNullOrWhiteSpace(_sharedMappingPath) ? null : _sharedMappingPath.Trim())
                : null;

            var input = new SettingsApplyInput
            {
                Url = _url.Trim(),
                SharedMappingPath = sharedPath,
                BomKeyword = _bomKeyword,
                WaitForServerAssignedIpn = _savedWaitForServerAssignedIpn,
                ProbeConnection = ConnectionFieldsChanged(),
            };

            _credentialState.ApplyCredentialTo(input, _username, _password);
            return input;
        }

        /// <summary>The same input with <see cref="EffectiveUrl"/> — Test always probes; the flag is irrelevant there.</summary>
        public SettingsApplyInput BuildTestInput()
        {
            var input = BuildApplyInput();
            input.Url = EffectiveUrl;
            return input;
        }

        // A probe is only worth its latency when something the probe exercises
        // changed since the last save: the typed URL, a non-blank key draft, a
        // complete credential pair, or saved-key presence. A mapping-path,
        // BOM-keyword, or IPN-flag-only change is not connection-relevant.
        private bool ConnectionFieldsChanged()
        {
            var current = CaptureSnapshot();
            if (!string.Equals(current.Url, _savedSnapshot.Url, StringComparison.Ordinal))
                return true;
            if (current.HasSavedApiKey != _savedSnapshot.HasSavedApiKey)
                return true;
            if (!string.IsNullOrWhiteSpace(current.ApiKeyDraft))
                return true;
            return !string.IsNullOrWhiteSpace(current.Username)
                && !string.IsNullOrWhiteSpace(current.Password);
        }

        // ── Post-persistence transitions ──────────────────────────────────────
        // Each marshals through the captured UI context so a caller off the UI
        // thread cannot fire PropertyChanged on a pool thread; off the UI
        // thread entirely (unit tests) the work runs inline.

        /// <summary>
        /// After a successful <see cref="ISettingsApplyService.ApplyAsync"/>: re-reads
        /// the saved config, rebuilds the credential state so the saved key is never
        /// re-shown, drops the password draft, collapses both slices, and re-baselines
        /// the dirty snapshot.
        /// </summary>
        public void MarkPersisted() => RunOnUiThread(() =>
        {
            _savedConfig = TryGetConfig();
            _credentialState = CredentialEditorState.FromSavedConfig(_savedConfig);
            _password = string.Empty;
            CollapseCredentialForm();
            _savedSnapshot = CaptureSnapshot();
            RaiseAllChanged();
        });

        /// <summary>
        /// Re-reads the saved config without touching drafts or the dirty
        /// baseline — ApplyAsync calls it after the provider rebuild so the
        /// card reflects the just-persisted state before the health check.
        /// </summary>
        public void ReloadPersistedConfig() => RunOnUiThread(() =>
        {
            _savedConfig = TryGetConfig();
            Raise(nameof(SavedConfig));
            Raise(nameof(EffectiveUrl));
            Raise(nameof(TestConnectionEnabled));
            // The saved-config axis moved — the card and the forced-open rule
            // re-derive from it; the user's edit flags are untouched.
            RaiseCardAndFormChanged();
        });

        /// <summary>
        /// After <see cref="ISettingsApplyService.RemoveApiKeyAsync"/>: as
        /// <see cref="MarkPersisted"/> plus clearing the account drafts — the
        /// form lands open on the incomplete saved state.
        /// </summary>
        public void OnCredentialRemoved() => RunOnUiThread(() =>
        {
            _username = string.Empty;
            MarkPersisted();
        });

        // Both apply outcomes end identically: the rebuilt provider propagates
        // to the add-in — it is swapped even when the file is invalid, since
        // the add-in and Task Pane must track the saved source path and the
        // Invalid result keeps Part Sync gated off on its own — the persisted
        // state becomes the dirty baseline so the dialog reads clean and the
        // cancel button honestly says Close (#267), and the footer reports the
        // merged outcome.
        private void FinishPersistedApply((string Text, StatusSeverity Severity) outcome) =>
            RunOnUiThread(() =>
            {
                MappingApplied?.Invoke(this, _mappingProvider);
                MarkPersisted();
                SetActionStatus(outcome.Text, outcome.Severity);
            });

        /// <summary>Clears the password draft — the Test path calls this so the password never lingers.</summary>
        public void ClearSecrets() => RunOnUiThread(() => Password = string.Empty);

        // ── Orchestration commands ──────────────────────────────────────────
        // Apply/Test/Remove: the window's async-void click handlers only
        // forward to these. Service, probe, and mapping failures surface only
        // through the status pairs — the commands never throw for them, so a
        // forward can never crash the host.

        /// <summary>
        /// Resolves credentials, persists the server config, rebuilds the
        /// mapping provider through <see cref="IMappingProviderFactory"/>,
        /// re-subscribes to mapping-changed, and fires <see cref="MappingApplied"/>
        /// exactly once. Returns <c>true</c> once the settings are persisted —
        /// a failed connection probe is reported as the outcome, not an apply
        /// failure; <c>false</c> only when an error was reported to the user
        /// (a <see cref="SettingsApplyException"/>, or a mapping
        /// load/result failure — the save persisted but Save must not close).
        /// </summary>
        public async Task<bool> ApplyAsync()
        {
            var input = BuildApplyInput();

            // #249: a save that changed nothing connection-relevant skips the
            // probe. The open probe — if still in flight — keeps its claim on
            // the card: it is not cancelled, the in-flight axis is not raised,
            // and the NotProbed result never becomes the card's verdict.
            bool probing = input.ProbeConnection;
            if (probing)
            {
                // The apply's own probe verdict supersedes the open probe's.
                BeginUserProbe();
                // The save happens inside ApplyAsync — the interim status must
                // not claim a save that a pre-persistence failure would disprove.
                RunOnUiThread(() => SetActionStatus(
                    "Saving settings and testing connection…", StatusSeverity.None));
            }
            else
            {
                RunOnUiThread(() => SetActionStatus("Saving settings…", StatusSeverity.None));
            }
            // A new save supersedes any earlier connection-scoped outcome —
            // leaving it would let the section bar contradict the fresh verdict.
            RunOnUiThread(() => SetConnectionStatus(string.Empty, StatusSeverity.None));

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
                if (probing) EndUserProbe(null);
                RunOnUiThread(() => SetActionStatus(ex.Message, StatusSeverity.Error));
                return false;
            }

            if (probing) EndUserProbe(probe);

            // The password never lingers — whether it was sent for token
            // resolution or shadowed by a winning key draft.
            ClearSecrets();

            bool mappingOk;
            try
            {
                var provider = _mappingProviderFactory.Create(input.SharedMappingPath);
                bool healthOk = false;
                RunOnUiThread(() =>
                {
                    _mappingProvider = provider;
                    Raise(nameof(LocalMappingPath));

                    // The save persisted — re-read the saved config so the
                    // card reflects what is now on disk. The radios already
                    // hold the just-persisted drafts.
                    ReloadPersistedConfig();
                    healthOk = ProjectMappingStatus();

                    // The rebuilt provider owns the mapping-changed feed now.
                    MappingChangedSubscription.SubscribeTo(
                        ref _mappingChangedSubscription, _mappingProvider, OnMappingChanged);
                });
                mappingOk = healthOk;
            }
            catch (Exception ex)
            {
                // Mapping detail stays in the Property Mapping section's own
                // status bar; the provider in place keeps its subscription.
                RunOnUiThread(() => ShowInvalidMappingStatus(
                    $"Failed to load the Property Mapping file: {ex.Message}"));
                mappingOk = false;
            }

            if (!mappingOk)
            {
                // The save persisted; the mapping bar carries the detail. The
                // footer aggregates both facts — the probe verdict and the
                // mapping failure — so the line is truthful on its own.
                FinishPersistedApply(FormatApplyOutcome(probe, mappingOk: false));
                return false;
            }

            try
            {
                FinishPersistedApply(FormatApplyOutcome(probe, mappingOk: true));
                return true;
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetActionStatus(
                    $"Failed to apply settings: {ex.Message}", StatusSeverity.Error));
                return false;
            }
        }

        /// <summary>
        /// Probes the <see cref="EffectiveUrl"/> with the effective credential —
        /// writes nothing. The verdict lands on the connection axis and the
        /// connection-scoped status pair; the password draft is cleared either
        /// way. A user-initiated probe supersedes the open probe.
        /// </summary>
        public async Task TestConnectionAsync()
        {
            var input = BuildTestInput();

            // A user-initiated probe supersedes the open probe — the card
            // should settle on the freshest verdict.
            BeginUserProbe();
            RunOnUiThread(() => SetConnectionStatus("Testing connection…", StatusSeverity.None));

            try
            {
                ConnectionProbeResult result;
                using (var client = new HttpClient())
                {
                    result = await _settingsApplyService.TestConnectionAsync(
                            input, client, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                EndUserProbe(result);
                RunOnUiThread(() =>
                {
                    SetConnectionStatus(
                        result.Succeeded ? result.Message : $"Connection failed. {result.Message}",
                        result.Succeeded ? StatusSeverity.Success : StatusSeverity.Error);

                    // The password never lingers — whether it was sent for
                    // token resolution or shadowed by a winning key draft.
                    Password = string.Empty;
                });
            }
            catch (InvalidOperationException ex)
            {
                EndUserProbe(null);
                RunOnUiThread(() => SetConnectionStatus(ex.Message, StatusSeverity.Error));
            }
            catch (Exception ex)
            {
                EndUserProbe(null);
                RunOnUiThread(() => SetConnectionStatus(
                    $"Connection failed: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>
        /// Clears only the saved API key through the apply service so the
        /// mutation surfaces as a <see cref="SettingsApplyException"/> with a
        /// consistent prefix. The saved URL, Property Mapping path, and BOM
        /// keyword survive; the card lands on Authentication required with the
        /// form open.
        /// </summary>
        public async Task RemoveApiKeyAsync()
        {
            try
            {
                await _settingsApplyService.RemoveApiKeyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetConnectionStatus(ex.Message, StatusSeverity.Error));
                return;
            }

            RunOnUiThread(() =>
            {
                OnCredentialRemoved();
                ClearProbeVerdict();
                SetConnectionStatus(
                    "Credential removed. Server address kept.", StatusSeverity.Success);
            });
        }

        // ── Mapping section ───────────────────────────────────────────────

        /// <summary>
        /// Re-projects the mapping section on demand — the window calls this
        /// after its editor launch closes. The subscription
        /// (<see cref="OnMappingChanged"/>) routes here too, so an external
        /// save and a post-edit refresh take the same path.
        /// </summary>
        public void RefreshMappingStatus() => RunOnUiThread(() => ProjectMappingStatus());

        /// <summary>
        /// The window's Closed hook: cancels the open probe and detaches the
        /// mapping-changed subscription — nothing outlives the dialog.
        /// </summary>
        internal void OnClosed()
        {
            CancelOpenProbe();
            MappingChangedSubscription.UnsubscribeFrom(ref _mappingChangedSubscription);
        }

        private void OnMappingChanged() => RefreshMappingStatus();

        // Re-projects the mapping status pair and the Edit Mappings
        // enabled/label from the current provider. The radios are the user's
        // draft — seeded from the saved source on open and left alone here,
        // so a refresh never discards an unapplied choice (#266). Returns
        // whether the resolved mapping is usable — a false return feeds
        // ApplyAsync's merged footer outcome.
        private bool ProjectMappingStatus()
        {
            try
            {
                var result = _mappingProvider.GetMappingResult();

                EditMappingsEnabled = result.CanEdit;
                EditMappingsLabel =
                    result.Source == MappingSource.Local
                        ? "Edit Local Mappings"
                        : "Edit Shared Mappings";

                MappingStatusText = result.FullStatusMessage;
                MappingStatusSeverity = result.Health switch
                {
                    MappingHealth.Healthy => StatusSeverity.Success,
                    MappingHealth.NeedsUpgrade => StatusSeverity.Warning,
                    MappingHealth.NewerSchema => StatusSeverity.Warning,
                    _ => StatusSeverity.Error,
                };
                return result.Health != MappingHealth.Invalid;
            }
            catch (InvalidOperationException ex)
            {
                return ShowInvalidMappingStatus(ex.Message);
            }
            catch (Exception ex)
            {
                return ShowInvalidMappingStatus(
                    $"Failed to load the Property Mapping file: {ex.Message}");
            }
        }

        // An unreadable mapping is an Invalid result for display purposes:
        // edit stays disabled and the radios keep their current draft.
        private bool ShowInvalidMappingStatus(string detail)
        {
            var result = new MappingResult(MappingHealth.Invalid,
                                           PropertyMappingConfig.WithDefaults(),
                                           detail);
            EditMappingsEnabled = false;
            MappingStatusText = result.FullStatusMessage;
            MappingStatusSeverity = StatusSeverity.Error;
            return false;
        }

        private void SetActionStatus(string text, StatusSeverity severity)
        {
            ActionStatusText = text;
            ActionStatusSeverity = severity;
        }

        private void SetConnectionStatus(string text, StatusSeverity severity)
        {
            ConnectionStatusText = text;
            ConnectionStatusSeverity = severity;
        }

        // ── Probe lifecycle ───────────────────────────────────────────────────
        // The probe fired on open reports a live verdict instead of a stale
        // saved claim. The window's Test/Apply paths begin a user probe that
        // supersedes it; Closed cancels it; a verdict landing after
        // cancellation is discarded.

        /// <summary>
        /// A user-initiated probe (Test connection, a probing Apply) is
        /// starting: cancels the open probe — its verdict, if it ever lands,
        /// is discarded — and raises the in-flight axis so the card reads
        /// Testing. Called before the probe's await.
        /// </summary>
        internal void BeginUserProbe() => RunOnUiThread(() =>
        {
            if (!_openProbeCts.IsCancellationRequested)
                _openProbeCts.Cancel();
            _probeInFlight = true;
            RaiseCardAndFormChanged();
        });

        /// <summary>
        /// The user probe settled: clears the in-flight axis and records
        /// <paramref name="verdict"/> as the session's connection state. A null
        /// verdict records nothing — the exception exit paths call this so the
        /// card cannot stick on Testing.
        /// </summary>
        internal void EndUserProbe(ConnectionProbeResult? verdict) => RunOnUiThread(() =>
        {
            _probeInFlight = false;
            if (verdict != null)
                _lastProbe = verdict;
            RaiseCardAndFormChanged();
        });

        /// <summary>
        /// The <see cref="ISettingsApplyService.RemoveApiKeyAsync"/> landing:
        /// drops the session verdict so the card re-derives from the
        /// configuration axis alone.
        /// </summary>
        internal void ClearProbeVerdict() => RunOnUiThread(() =>
        {
            _lastProbe = null;
            RaiseCardAndFormChanged();
        });

        /// <summary>
        /// Idempotent cancel-on-close: cancels and disposes the open-probe
        /// CTS while <see cref="CancellationTokenSource.IsCancellationRequested"/>
        /// stays readable, so a late verdict is still discarded.
        /// </summary>
        internal void CancelOpenProbe()
        {
            if (_openProbeCts.IsCancellationRequested)
                return;
            _openProbeCts.Cancel();
            _openProbeCts.Dispose();
        }

        // ── Probe on open (#234) ──────────────────────────────────────────────
        // A complete saved config gets a live probe on every open. The probe
        // runs against the saved values, not the form fields; cancellation is
        // silent, any other failure surfaces as an Unreachable verdict rather
        // than leaving the card on Testing forever.

        private void StartOpenProbe()
        {
            if (!StatusCard.IsComplete)
                return;

            var input = new SettingsApplyInput
            {
                Url = _savedConfig!.Url,
                RawApiKey = _savedConfig.ApiKey,
            };

            _probeInFlight = true;
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
                // The probe could not run to a verdict (e.g. an unparsable
                // saved URL) — surface it like any other failure instead of
                // leaving the card on Testing forever.
                result = new ConnectionProbeResult(ConnectionProbeStatus.Unreachable, ex.Message);
            }

            try
            {
                RunOnUiThread(() =>
                {
                    // Closed or superseded between verdict and application —
                    // the newer probe owns the card now.
                    if (_openProbeCts.IsCancellationRequested)
                        return;

                    _lastProbe = result;
                    _probeInFlight = false;
                    RaiseCardAndFormChanged();
                });
            }
            catch (Exception)
            {
                // The window's dispatcher is gone — nothing left to report to.
            }
        }

        // ── Outcome text ──────────────────────────────────────────────────────

        /// <summary>
        /// The footer line for an apply: "Saved — &lt;clause&gt;." while the Property
        /// Mapping is healthy, "Saved — &lt;clause&gt;; the Property Mapping file could
        /// not be loaded." when it is not. A <see cref="ConnectionProbeStatus.NotProbed"/>
        /// outcome carries no verdict clause — the line is just "Saved." (#249).
        /// </summary>
        public static (string Text, StatusSeverity Severity) FormatApplyOutcome(
            ConnectionProbeResult outcome, bool mappingOk)
        {
            if (!mappingOk)
            {
                string clause = outcome.Status switch
                {
                    ConnectionProbeStatus.Connected => " — connection successful",
                    ConnectionProbeStatus.CredentialRejected => $" — authentication required ({outcome.Message})",
                    ConnectionProbeStatus.NotConfigured => " — server connection cleared",
                    ConnectionProbeStatus.NotProbed => string.Empty,
                    _ => $" — connection failed ({outcome.Message})",
                };
                return ($"Saved{clause}; the Property Mapping file could not be loaded.",
                        StatusSeverity.Error);
            }

            return outcome.Status switch
            {
                ConnectionProbeStatus.Connected =>
                    ("Saved — connection successful.", StatusSeverity.Success),
                ConnectionProbeStatus.CredentialRejected =>
                    ($"Saved — authentication required ({outcome.Message})", StatusSeverity.Warning),
                ConnectionProbeStatus.NotConfigured =>
                    ("Saved — server connection cleared.", StatusSeverity.Success),
                ConnectionProbeStatus.NotProbed =>
                    ("Saved.", StatusSeverity.Success),
                _ =>
                    ($"Saved — but the connection failed ({outcome.Message})", StatusSeverity.Error),
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ServerConfig? TryGetConfig()
        {
            try { return _configProvider.GetServerConfig(); }
            catch { return null; }
        }

        private SettingsSnapshot CaptureSnapshot() =>
            new SettingsSnapshot(
                url: _url.Trim(),
                apiKeyDraft: ApiKeyDraft.Trim(),
                hasSavedApiKey: _credentialState.HasSavedApiKey,
                username: _username.Trim(),
                password: _password,
                sharedPath: _sharedMappingPath.Trim(),
                bomKeyword: _bomKeyword.Trim(),
                useLocalMapping: !_useSharedMapping,
                waitForServerAssignedIpn: _savedWaitForServerAssignedIpn);

        // Every draft change can flip dirty gating, the Cancel/Close label, and
        // the Test button's effective-URL enablement.
        private void OnDraftsChanged()
        {
            Raise(nameof(IsDirty));
            Raise(nameof(CancelLabel));
            Raise(nameof(EffectiveUrl));
            Raise(nameof(TestConnectionEnabled));
        }

        // Every card-affecting change raises the card and all three form
        // outputs together so one re-render is always coherent — the
        // forced-open rule derives from StatusCard.IsComplete.
        private void RaiseCardAndFormChanged()
        {
            EmitConnectionStateIfChanged();
            Raise(nameof(StatusCard));
            Raise(nameof(ConnectionState));
            Raise(nameof(UrlSliceOpen));
            Raise(nameof(CredentialSliceOpen));
            Raise(nameof(CredentialFormOpen));
        }

        // The #241 reach path: fire only when the recomputed read-model
        // actually moved — a re-render that leaves every field equal (e.g. a
        // slice toggle) is not a state change.
        private void EmitConnectionStateIfChanged()
        {
            var current = ConnectionState;
            if (_emittedConnectionState.Equals(current))
                return;

            _emittedConnectionState = current;
            ConnectionStateChanged?.Invoke(this, current);
        }

        // Post-persistence transitions can touch any derived output at once —
        // raise the full set so subscribers see a coherent state.
        private void RaiseAllChanged()
        {
            Raise(nameof(SavedConfig));
            Raise(nameof(HasSavedApiKey));
            Raise(nameof(ApiKeyDraft));
            Raise(nameof(Username));
            Raise(nameof(Password));
            OnDraftsChanged();
            RaiseCardAndFormChanged();
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread. Same-thread callers
        /// execute inline; off-thread callers marshal through Send (synchronous)
        /// so they see property updates immediately. Falls back to inline
        /// execution when no context was captured (unit tests).
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (Environment.CurrentManagedThreadId == _uiThreadId || _uiContext == null)
                action();
            else
                _uiContext.Send(_ => action(), null);
        }
    }
}
