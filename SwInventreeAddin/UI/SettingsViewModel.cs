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
    /// Credential state, dirty gating, the status-card projection, the
    /// open-probe lifecycle, and form-view-state for the Settings window — the
    /// rules that used to live in its code-behind. Pure C# with no WPF types,
    /// so tests construct it without STA. The window pushes field edits into
    /// the draft properties and reads the derived outputs for enablement,
    /// labels, and visibility; credential precedence and the probe-skip
    /// decision (#249) resolve inside <see cref="BuildApplyInput"/>.
    /// </summary>
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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
        private readonly bool _savedWaitForServerAssignedIpn = true;

        private string _url = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _sharedMappingPath = string.Empty;
        private string _bomKeyword = string.Empty;
        private bool _useLocalMapping = true;

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

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Reads the saved <see cref="ServerConfig"/> once — a throwing provider
        /// reads as "nothing saved" — seeds every draft, builds the credential
        /// state, and baselines the dirty snapshot. A complete saved config
        /// also starts the open probe: no caller call, so
        /// <see cref="OpenProbeTask"/> is non-null from then on.
        /// </summary>
        public SettingsViewModel(IConfigProvider configProvider,
                                 ISettingsApplyService settingsApplyService)
        {
            _configProvider = configProvider;
            _settingsApplyService = settingsApplyService;
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
                _useLocalMapping = string.IsNullOrEmpty(_savedConfig.MappingSourcePath);
            }

            _savedSnapshot = CaptureSnapshot();

            StartOpenProbe();
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

        /// <summary>True → the local Property Mapping file is used and the shared path persists as null.</summary>
        public bool UseLocalMapping
        {
            get => _useLocalMapping;
            set { if (Set(ref _useLocalMapping, value)) OnDraftsChanged(); }
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
            string? sharedPath = _useLocalMapping
                ? null
                : (string.IsNullOrWhiteSpace(_sharedMappingPath) ? null : _sharedMappingPath.Trim());

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
            _editingUrl = false;
            _showCredentialForm = false;
            _savedSnapshot = CaptureSnapshot();
            RaiseAllChanged();
        });

        /// <summary>
        /// For the apply-succeeded-but-mapping-failed path: re-reads the saved
        /// config only — drafts and the dirty baseline stay, so the dialog keeps
        /// its pending changes.
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

        /// <summary>Clears the password draft — the Test path calls this so the password never lingers.</summary>
        public void ClearSecrets() => RunOnUiThread(() => Password = string.Empty);

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
                useLocalMapping: _useLocalMapping,
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
            Raise(nameof(StatusCard));
            Raise(nameof(UrlSliceOpen));
            Raise(nameof(CredentialSliceOpen));
            Raise(nameof(CredentialFormOpen));
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
