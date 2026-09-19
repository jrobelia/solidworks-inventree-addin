using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    // Credential state, dirty gating, the probe-skip predicate (#249), and the
    // form-view flags — the rules SettingsWindow used to keep in code-behind,
    // exercised here with no window and no STA thread.
    [TestFixture]
    public class SettingsViewModelTests
    {
        private static SettingsViewModel CreateVm(
            IConfigProvider? configProvider = null,
            StubSettingsApplyService? applyService = null) =>
            new SettingsViewModel(
                configProvider ?? new StubConfigProvider("https://inventree.example.com", "saved-key"),
                applyService ?? new StubSettingsApplyService());

        private static void SetDraft(SettingsViewModel vm, string field, string value)
        {
            switch (field)
            {
                case nameof(SettingsViewModel.Url): vm.Url = value; break;
                case nameof(SettingsViewModel.Username): vm.Username = value; break;
                case nameof(SettingsViewModel.Password): vm.Password = value; break;
                case nameof(SettingsViewModel.ApiKeyDraft): vm.ApiKeyDraft = value; break;
                case nameof(SettingsViewModel.SharedMappingPath): vm.SharedMappingPath = value; break;
                case nameof(SettingsViewModel.BomKeyword): vm.BomKeyword = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        // ── Dirty gating ──────────────────────────────────────────────────
        // Apply/Save enable only on a persistable change; blank or half-typed
        // credential fields never count, and the Cancel button relabels Close.

        [TestCase(nameof(SettingsViewModel.Url), "https://other.example.com", true)]
        [TestCase(nameof(SettingsViewModel.Url), "https://inventree.example.com", false)]
        [TestCase(nameof(SettingsViewModel.Username), "engineer", false)]
        [TestCase(nameof(SettingsViewModel.Password), "s3cret", false)]
        [TestCase(nameof(SettingsViewModel.ApiKeyDraft), "inv-new", true)]
        [TestCase(nameof(SettingsViewModel.ApiKeyDraft), "   ", false)]
        [TestCase(nameof(SettingsViewModel.SharedMappingPath), @"\share\map.json", true)]
        [TestCase(nameof(SettingsViewModel.BomKeyword), "custom-bom", true)]
        [TestCase(nameof(SettingsViewModel.BomKeyword), "inventree", false)]
        public void IsDirty_WhenFieldEdited_ReflectsPersistableChange(
            string field, string value, bool expected)
        {
            var vm = CreateVm();

            SetDraft(vm, field, value);

            Assert.Multiple(() =>
            {
                Assert.That(vm.IsDirty, Is.EqualTo(expected));
                Assert.That(vm.CancelLabel, Is.EqualTo(expected ? "Cancel" : "Close"));
            });
        }

        [Test]
        public void IsDirty_WhenNothingSavedAndNoEdits_IsFalse()
        {
            var vm = CreateVm(StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(vm.IsDirty, Is.False);
                Assert.That(vm.CancelLabel, Is.EqualTo("Close"));
            });
        }

        [Test]
        public void IsDirty_WhenCompletePairTyped_IsTrue()
        {
            var vm = CreateVm();

            vm.Username = "engineer";
            vm.Password = "s3cret";

            Assert.That(vm.IsDirty, Is.True);
        }

        [Test]
        public void IsDirty_WhenUseLocalMappingToggles_IsTrue()
        {
            var vm = CreateVm();

            vm.UseLocalMapping = false;

            Assert.That(vm.IsDirty, Is.True);
        }

        // ── Credential precedence into the apply input ────────────────────
        // Typed key draft > complete username/password pair > saved key;
        // half-typed pairs are ignored so a stray field cannot wipe a saved
        // credential.

        [TestCase("saved-key", "inv-typed", "engineer", "s3cret", "inv-typed", "", "")]
        [TestCase("saved-key", "", "engineer", "s3cret", "", "engineer", "s3cret")]
        [TestCase("saved-key", "", "", "", "saved-key", "", "")]
        [TestCase("saved-key", "", "engineer", "", "saved-key", "", "")]
        [TestCase("", "", "engineer", "s3cret", "", "engineer", "s3cret")]
        [TestCase("", "", "", "", "", "", "")]
        public void BuildApplyInput_ResolvesCredentialPrecedence(
            string savedKey, string keyDraft, string username, string password,
            string expectedKey, string expectedUser, string expectedPassword)
        {
            var vm = CreateVm(new StubConfigProvider("https://inventree.example.com", savedKey));

            vm.ApiKeyDraft = keyDraft;
            vm.Username = username;
            vm.Password = password;

            var input = vm.BuildApplyInput();

            Assert.Multiple(() =>
            {
                Assert.That(input.RawApiKey, Is.EqualTo(expectedKey));
                Assert.That(input.Username, Is.EqualTo(expectedUser));
                Assert.That(input.Password, Is.EqualTo(expectedPassword));
            });
        }

        // ── Probe-skip predicate (#249) ───────────────────────────────────
        // ProbeConnection feeds ISettingsApplyService.ApplyAsync's skip: only a
        // connection-relevant change (typed URL, key draft, complete pair, or
        // saved-key presence) pays probe latency; a Property Mapping path or
        // BOM Keyword edit saves without probing.

        [TestCase(nameof(SettingsViewModel.Url), "https://other.example.com", true)]
        [TestCase(nameof(SettingsViewModel.ApiKeyDraft), "inv-new", true)]
        [TestCase(nameof(SettingsViewModel.SharedMappingPath), @"\share\map.json", false)]
        [TestCase(nameof(SettingsViewModel.BomKeyword), "custom-bom", false)]
        public void BuildApplyInput_ProbeConnection_ReflectsConnectionFieldChanges(
            string field, string value, bool expected)
        {
            var vm = CreateVm();

            SetDraft(vm, field, value);

            Assert.That(vm.BuildApplyInput().ProbeConnection, Is.EqualTo(expected));
        }

        [Test]
        public void BuildApplyInput_WhenCompletePairTyped_SetsProbeConnection()
        {
            var vm = CreateVm();

            vm.Username = "engineer";
            vm.Password = "s3cret";

            Assert.That(vm.BuildApplyInput().ProbeConnection, Is.True);
        }

        [Test]
        public void BuildApplyInput_WhenHalfTypedPair_LeavesProbeConnectionFalse()
        {
            var vm = CreateVm();

            vm.Username = "engineer";

            Assert.That(vm.BuildApplyInput().ProbeConnection, Is.False);
        }

        [Test]
        public void BuildApplyInput_WhenOnlyMappingFieldsChanged_SkipsProbe()
        {
            var vm = CreateVm();

            vm.UseLocalMapping = false;
            vm.SharedMappingPath = @"\share\map.json";
            vm.BomKeyword = "custom-bom";

            var input = vm.BuildApplyInput();

            Assert.Multiple(() =>
            {
                Assert.That(input.ProbeConnection, Is.False);
                Assert.That(input.SharedMappingPath, Is.EqualTo(@"\share\map.json"));
                Assert.That(input.BomKeyword, Is.EqualTo("custom-bom"));
                Assert.That(vm.IsDirty, Is.True,
                    "a mapping-only edit is still a persistable change");
            });
        }

        [Test]
        public void BuildApplyInput_WhenLocalMapping_PersistsNullSharedPath()
        {
            var vm = CreateVm();

            vm.SharedMappingPath = @"\share\map.json";
            // UseLocalMapping stays true — the path box content is ignored.

            Assert.That(vm.BuildApplyInput().SharedMappingPath, Is.Null);
        }

        [Test]
        public void BuildApplyInput_WhenSharedMappingBlank_PersistsNullSharedPath()
        {
            var vm = CreateVm();

            vm.UseLocalMapping = false;
            vm.SharedMappingPath = "   ";

            Assert.That(vm.BuildApplyInput().SharedMappingPath, Is.Null);
        }

        [Test]
        public void BuildApplyInput_CarriesTheSavedWaitForServerAssignedIpnFlag()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            provider.Config!.WaitForServerAssignedIpn = false;
            var vm = CreateVm(provider);

            Assert.That(vm.BuildApplyInput().WaitForServerAssignedIpn, Is.False);
        }

        [Test]
        public void BuildTestInput_UsesTheEffectiveUrlWithTheSameCredential()
        {
            var vm = CreateVm();

            vm.Url = string.Empty;   // a cleared draft falls back to the saved URL

            var input = vm.BuildTestInput();

            Assert.Multiple(() =>
            {
                Assert.That(input.Url, Is.EqualTo("https://inventree.example.com"));
                Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
            });
        }

        // ── Effective URL + Test enablement ───────────────────────────────
        // The URL field hides in the configured state, so the trimmed draft
        // falls back to the saved URL.

        [TestCase("https://saved.example.com", "", "https://saved.example.com", true)]
        [TestCase("https://saved.example.com", "  ", "https://saved.example.com", true)]
        [TestCase("https://saved.example.com", "https://typed.example.com", "https://typed.example.com", true)]
        [TestCase("", "", "", false)]
        [TestCase("", "https://typed.example.com", "https://typed.example.com", true)]
        public void EffectiveUrl_FallsBackToTheSavedUrl(
            string savedUrl, string draftUrl, string expectedUrl, bool expectedEnabled)
        {
            var vm = CreateVm(new StubConfigProvider(savedUrl, "saved-key"));

            vm.Url = draftUrl;

            Assert.Multiple(() =>
            {
                Assert.That(vm.EffectiveUrl, Is.EqualTo(expectedUrl));
                Assert.That(vm.TestConnectionEnabled, Is.EqualTo(expectedEnabled));
            });
        }

        // ── Form view-state ───────────────────────────────────────────────
        // Forced open while the saved config is incomplete; once complete the
        // card toolbar toggles reveal one slice at a time.

        [Test]
        public void FormFlags_WhenNothingSaved_ForceTheFormOpen()
        {
            var vm = CreateVm(StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.True);
                Assert.That(vm.CredentialSliceOpen, Is.True);
                Assert.That(vm.CredentialFormOpen, Is.True);
            });
        }

        [Test]
        public void FormFlags_WhenServerOnlySaved_ForceTheFormOpen()
        {
            var vm = CreateVm(new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.True);
                Assert.That(vm.CredentialSliceOpen, Is.True);
            });
        }

        [Test]
        public void FormFlags_WhenConfigComplete_StartCollapsed()
        {
            var vm = CreateVm();

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.False);
                Assert.That(vm.CredentialSliceOpen, Is.False);
                Assert.That(vm.CredentialFormOpen, Is.False);
            });
        }

        [Test]
        public void ToggleUrlSlice_WhenConfigComplete_OpensOnlyTheUrlSlice()
        {
            var vm = CreateVm();

            vm.ToggleUrlSlice();

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.True);
                Assert.That(vm.CredentialSliceOpen, Is.False);
            });

            vm.ToggleUrlSlice();

            Assert.That(vm.CredentialFormOpen, Is.False);
        }

        [Test]
        public void ToggleCredentialSlice_WhenConfigComplete_OpensOnlyTheCredentialSlice()
        {
            var vm = CreateVm();

            vm.ToggleCredentialSlice();

            Assert.Multiple(() =>
            {
                Assert.That(vm.CredentialSliceOpen, Is.True);
                Assert.That(vm.UrlSliceOpen, Is.False);
            });
        }

        [Test]
        public void Toggles_AreMutuallyExclusive()
        {
            var vm = CreateVm();

            vm.ToggleCredentialSlice();
            vm.ToggleUrlSlice();

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.True);
                Assert.That(vm.CredentialSliceOpen, Is.False);
            });
        }

        [Test]
        public void Toggles_WhenConfigIncomplete_CannotCollapseTheForcedForm()
        {
            var vm = CreateVm(new StubConfigProvider("https://inventree.example.com", string.Empty));

            vm.ToggleUrlSlice();
            vm.ToggleCredentialSlice();

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.True);
                Assert.That(vm.CredentialSliceOpen, Is.True);
            });
        }

        // ── Post-persistence transitions ──────────────────────────────────

        [Test]
        public void MarkPersisted_RebaselinesAroundTheSavedConfig()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var vm = CreateVm(provider);
            vm.Url = "https://other.example.com";
            vm.ApiKeyDraft = "inv-new";
            vm.Password = "s3cret";
            vm.ToggleUrlSlice();

            // Mirror the persist ApplyAsync just performed.
            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://other.example.com",
                ApiKey = "inv-new",
                BomKeyword = "inventree",
            });
            vm.MarkPersisted();

            Assert.Multiple(() =>
            {
                Assert.That(vm.SavedConfig!.Url, Is.EqualTo("https://other.example.com"));
                Assert.That(vm.HasSavedApiKey, Is.True);
                Assert.That(vm.ApiKeyDraft, Is.Empty,
                    "a saved key is never re-shown as a draft");
                Assert.That(vm.Password, Is.Empty, "the password never lingers");
                Assert.That(vm.IsDirty, Is.False);
                Assert.That(vm.CancelLabel, Is.EqualTo("Close"));
                Assert.That(vm.CredentialFormOpen, Is.False,
                    "both slices collapse onto the card");
            });
        }

        [Test]
        public void ReloadPersistedConfig_ReReadsSavedConfigButKeepsDraftsAndBaseline()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var vm = CreateVm(provider);
            vm.BomKeyword = "custom-bom";

            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://inventree.example.com",
                ApiKey = "saved-key",
                BomKeyword = "custom-bom",
            });
            vm.ReloadPersistedConfig();

            Assert.Multiple(() =>
            {
                Assert.That(vm.SavedConfig!.BomKeyword, Is.EqualTo("custom-bom"));
                Assert.That(vm.BomKeyword, Is.EqualTo("custom-bom"), "the draft is untouched");
                Assert.That(vm.IsDirty, Is.True,
                    "the baseline is untouched — the dialog stays dirty until MarkPersisted");
            });
        }

        [Test]
        public void OnCredentialRemoved_ClearsTheCredentialDraftsAndRebaselines()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var vm = CreateVm(provider);
            vm.Username = "engineer";
            vm.Password = "s3cret";
            vm.ApiKeyDraft = "inv-new";
            vm.ToggleCredentialSlice();

            // Mirror RemoveApiKeyAsync: the saved key is cleared, the URL survives.
            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://inventree.example.com",
                ApiKey = string.Empty,
                BomKeyword = "inventree",
            });
            vm.OnCredentialRemoved();

            Assert.Multiple(() =>
            {
                Assert.That(vm.HasSavedApiKey, Is.False);
                Assert.That(vm.ApiKeyDraft, Is.Empty);
                Assert.That(vm.Username, Is.Empty);
                Assert.That(vm.Password, Is.Empty);
                Assert.That(vm.IsDirty, Is.False);
                Assert.That(vm.UrlSliceOpen, Is.True,
                    "the incomplete saved state forces the form open");
                Assert.That(vm.CredentialSliceOpen, Is.True);
            });
        }

        [Test]
        public void ClearSecrets_ClearsOnlyThePasswordDraft()
        {
            var vm = CreateVm();

            vm.Username = "engineer";
            vm.Password = "s3cret";
            vm.ClearSecrets();

            Assert.Multiple(() =>
            {
                Assert.That(vm.Password, Is.Empty);
                Assert.That(vm.Username, Is.EqualTo("engineer"));
                Assert.That(vm.IsDirty, Is.False,
                    "a half-typed pair is not a persistable change");
            });
        }

        // ── Apply outcome text (#249 adds the no-verdict line) ────────────

        [TestCase(ConnectionProbeStatus.Connected, true,
            "Saved — connection successful.", StatusSeverity.Success)]
        [TestCase(ConnectionProbeStatus.CredentialRejected, true,
            "Saved — authentication required (detail)", StatusSeverity.Warning)]
        [TestCase(ConnectionProbeStatus.NotConfigured, true,
            "Saved — server connection cleared.", StatusSeverity.Success)]
        [TestCase(ConnectionProbeStatus.NotProbed, true,
            "Saved.", StatusSeverity.Success)]
        [TestCase(ConnectionProbeStatus.Unreachable, true,
            "Saved — but the connection failed (detail)", StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.ServerError, true,
            "Saved — but the connection failed (detail)", StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.Connected, false,
            "Saved — connection successful; the Property Mapping file could not be loaded.",
            StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.CredentialRejected, false,
            "Saved — authentication required (detail); the Property Mapping file could not be loaded.",
            StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.NotConfigured, false,
            "Saved — server connection cleared; the Property Mapping file could not be loaded.",
            StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.NotProbed, false,
            "Saved; the Property Mapping file could not be loaded.", StatusSeverity.Error)]
        [TestCase(ConnectionProbeStatus.Unreachable, false,
            "Saved — connection failed (detail); the Property Mapping file could not be loaded.",
            StatusSeverity.Error)]
        public void FormatApplyOutcome_MapsTheVerdictToFooterText(
            ConnectionProbeStatus status, bool mappingOk,
            string expectedText, StatusSeverity expectedSeverity)
        {
            var outcome = new ConnectionProbeResult(status, "detail");

            var (text, severity) = SettingsViewModel.FormatApplyOutcome(outcome, mappingOk);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo(expectedText));
                Assert.That(severity, Is.EqualTo(expectedSeverity));
            });
        }

        // ── Construction tolerance + notifications ────────────────────────

        [Test]
        public void Constructor_WhenProviderThrows_ReadsAsNothingSaved()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key")
            {
                ThrowOnGet = new InvalidOperationException("read failed"),
            };

            var vm = CreateVm(provider);

            Assert.Multiple(() =>
            {
                Assert.That(vm.SavedConfig, Is.Null);
                Assert.That(vm.HasSavedApiKey, Is.False);
                Assert.That(vm.UrlSliceOpen, Is.True,
                    "an unreadable config is incomplete — the form opens");
                Assert.That(vm.IsDirty, Is.False);
            });
        }

        [Test]
        public void DraftSet_FiresPropertyChangedForTheDraftAndDerivedOutputs()
        {
            var vm = CreateVm();
            var fired = new List<string?>();
            vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

            vm.Url = "https://other.example.com";

            Assert.Multiple(() =>
            {
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.Url)));
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.IsDirty)));
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.CancelLabel)));
            });
        }

        [Test]
        public void DraftSet_WhenValueUnchanged_FiresNothing()
        {
            var vm = CreateVm();
            var fired = new List<string?>();
            vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

            vm.Url = "https://inventree.example.com";   // same as the seeded draft

            Assert.That(fired, Is.Empty);
        }

        [Test]
        public void LifecycleTransition_MarshalsThroughTheCapturedSyncContext()
        {
            var stubContext = new StubSynchronizationContext();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(stubContext);
            try
            {
                var vm = CreateVm();

                // A caller on another thread marshals through Send.
                Task.Run(() => vm.MarkPersisted()).Wait();

                Assert.That(stubContext.SendCount, Is.GreaterThan(0));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Test]
        public void LifecycleTransition_OnTheCapturedThread_RunsInline()
        {
            // Same-thread callers must not Send: inside a WPF Dispatcher.Invoke
            // the current context is a fresh DispatcherSynchronizationContext
            // wrapper that never reference-equals the captured one — a Send
            // there would marshal into a context whose pump is not running.
            var stubContext = new StubSynchronizationContext();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(stubContext);
            try
            {
                var vm = CreateVm();

                vm.MarkPersisted();

                Assert.That(stubContext.SendCount, Is.EqualTo(0));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        // ── Apply seam: the skip flag crosses the stub contract ───────────
        // StubSettingsApplyService mirrors the real service — it still
        // persists through its provider and returns the no-verdict result.

        [Test]
        public async Task ApplySeam_WhenOnlyMappingChanged_PersistsAndReturnsNotProbed()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var vm = CreateVm(provider);
            var applyService = new StubSettingsApplyService(provider);

            vm.BomKeyword = "custom-bom";
            var input = vm.BuildApplyInput();

            var result = await applyService.ApplyAsync(input, new HttpClient());

            Assert.Multiple(() =>
            {
                Assert.That(input.ProbeConnection, Is.False);
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotProbed));
                Assert.That(result.Succeeded, Is.False);
                Assert.That(provider.LastSavedConfig!.BomKeyword, Is.EqualTo("custom-bom"),
                    "the save still persists through the stub's provider");
            });
        }

        // ── Status card projection (ADR-0023) ───────────────────────────
        // The card is a pure projection: the saved-config axis crossed with
        // the session's probe axis. The window renders these outputs
        // mechanically — IsSaved → card visibility, IsComplete → toolbar
        // visibility, Indicator → dot brush — and never re-derives them.

        [TestCase(null, null, false, false,
            ServerConnectionIndicator.NotTested, "Not tested",
            "not saved", "none saved", "—")]
        // A cleared URL keeps the saved key (#253): the record exists but the
        // card still hides — the projection reports the unsaved state.
        [TestCase("", "saved-key", false, false,
            ServerConnectionIndicator.NotTested, "Not tested",
            "not saved", "API key saved", "—")]
        [TestCase("https://inventree.example.com", "", true, false,
            ServerConnectionIndicator.AuthenticationRequired, "Authentication required",
            "https://inventree.example.com", "none saved", "—")]
        public void StatusCard_ProjectsTheSavedConfigAxis(
            string? url, string? apiKey,
            bool expectedSaved, bool expectedComplete,
            ServerConnectionIndicator expectedIndicator, string expectedTitle,
            string expectedServer, string expectedCredential, string expectedConnection)
        {
            var provider = url == null
                ? StubConfigProvider.WithNoSavedConfig()
                : new StubConfigProvider(url, apiKey!);
            var vm = CreateVm(provider);

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.IsSaved, Is.EqualTo(expectedSaved));
                Assert.That(vm.StatusCard.IsComplete, Is.EqualTo(expectedComplete));
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(expectedIndicator));
                Assert.That(vm.StatusCard.Title, Is.EqualTo(expectedTitle));
                Assert.That(vm.StatusCard.ServerLine, Is.EqualTo(expectedServer));
                Assert.That(vm.StatusCard.CredentialLine, Is.EqualTo(expectedCredential));
                Assert.That(vm.StatusCard.ConnectionLine, Is.EqualTo(expectedConnection));
            });
        }

        [TestCase(ConnectionProbeStatus.Connected, "detail",
            "Connected", ServerConnectionIndicator.Connected, "last test succeeded")]
        [TestCase(ConnectionProbeStatus.CredentialRejected, "The server rejected the API key (401 Unauthorized).",
            "Authentication required", ServerConnectionIndicator.AuthenticationRequired,
            "The server rejected the API key (401 Unauthorized).")]
        [TestCase(ConnectionProbeStatus.Unreachable, "Could not reach the InvenTree server.",
            "Connection failed", ServerConnectionIndicator.Failed,
            "Could not reach the InvenTree server.")]
        [TestCase(ConnectionProbeStatus.ServerError, "Server responded: 500",
            "Connection failed", ServerConnectionIndicator.Failed, "Server responded: 500")]
        public async Task StatusCard_ProjectsTheSettledProbeVerdict(
            ConnectionProbeStatus status, string message,
            string expectedTitle, ServerConnectionIndicator expectedIndicator,
            string expectedConnection)
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var vm = CreateVm(applyService: applyService);

            Assert.That(vm.StatusCard.Title, Is.EqualTo("Testing connection…"),
                "the card reports the in-flight probe");

            pending.SetResult(new ConnectionProbeResult(status, message));
            await vm.OpenProbeTask!;

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.Title, Is.EqualTo(expectedTitle));
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(expectedIndicator));
                Assert.That(vm.StatusCard.ConnectionLine, Is.EqualTo(expectedConnection));
                Assert.That(vm.StatusCard.IsSaved, Is.True);
                Assert.That(vm.StatusCard.IsComplete, Is.True);
            });
        }

        [Test]
        public async Task StatusCard_WhenProbeReportsNotConfigured_HidesTheCard()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var vm = CreateVm(applyService: applyService);

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.NotConfigured, "Server connection cleared."));
            await vm.OpenProbeTask!;

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.IsSaved, Is.False);
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Not tested"));
            });
        }

        // ── Probe on open: lifecycle in the VM ──────────────────────────
        // A complete saved config starts the open probe inside the
        // constructor; OpenProbeTask is the deterministic settle point.

        [Test]
        public void Constructor_WhenConfigComplete_StartsOpenProbeWithSavedValues()
        {
            var applyService = new StubSettingsApplyService();
            var vm = CreateVm(applyService: applyService);

            Assert.Multiple(() =>
            {
                Assert.That(vm.OpenProbeTask, Is.Not.Null);
                Assert.That(applyService.TestCallCount, Is.EqualTo(1));
                Assert.That(applyService.LastTestInput!.Url,
                            Is.EqualTo("https://inventree.example.com"));
                Assert.That(applyService.LastTestInput.RawApiKey, Is.EqualTo("saved-key"));
                Assert.That(applyService.LastTestInput.Username, Is.Empty);
                Assert.That(applyService.LastTestInput.Password, Is.Empty);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Constructor_WhenConfigIncomplete_DoesNotProbe(bool nothingSaved)
        {
            var provider = nothingSaved
                ? StubConfigProvider.WithNoSavedConfig()
                : new StubConfigProvider("https://inventree.example.com", string.Empty);
            var applyService = new StubSettingsApplyService();

            var vm = CreateVm(provider, applyService);

            Assert.Multiple(() =>
            {
                Assert.That(vm.OpenProbeTask, Is.Null);
                Assert.That(applyService.TestCallCount, Is.EqualTo(0));
            });
        }

        [Test]
        public async Task OpenProbe_WhenServiceThrows_SettlesCardToFailedWithoutFaulting()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnTestConnection = new InvalidOperationException("saved URL unparsable"),
            };
            var vm = CreateVm(applyService: applyService);

            await vm.OpenProbeTask!;   // never faults — the settle point must always complete

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(ServerConnectionIndicator.Failed));
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Connection failed"));
                Assert.That(vm.StatusCard.ConnectionLine, Does.Contain("unparsable"));
            });
        }

        [Test]
        public async Task OpenProbe_WhenCancelled_SettlesSilentlyLeavingTheTestingCard()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnTestConnection = new OperationCanceledException(),
            };
            var vm = CreateVm(applyService: applyService);

            await vm.OpenProbeTask!;

            Assert.That(vm.StatusCard.Title, Is.EqualTo("Testing connection…"));
        }

        // ── Supersede + discard-late-verdict ────────────────────────────
        // Any newer user-initiated probe cancels the open probe's token; a
        // verdict arriving after cancellation is discarded, never applied.

        [Test]
        public async Task OpenProbe_WhenSupersededByUserProbe_DiscardsItsVerdict()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService
            {
                PendingTestResult = pending,
                // Delivers a normal verdict past the cancelled token so the
                // discard guard — not the cancellation race — is exercised.
                IgnoreCallerCancellation = true,
            };
            var vm = CreateVm(applyService: applyService);

            vm.BeginUserProbe();

            Assert.That(applyService.LastTestToken.IsCancellationRequested, Is.True,
                "a newer user-initiated probe supersedes the open probe");

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.Connected, "Connection successful."));
            await vm.OpenProbeTask!;

            Assert.That(vm.StatusCard.Title, Is.EqualTo("Testing connection…"),
                "the discarded verdict leaves the in-flight user probe owning the card");
        }

        [Test]
        public async Task OpenProbe_WhenVerdictArrivesAfterCancel_IsDiscarded()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService
            {
                PendingTestResult = pending,
                IgnoreCallerCancellation = true,
            };
            var vm = CreateVm(applyService: applyService);
            Assert.That(vm.StatusCard.Title, Is.EqualTo("Testing connection…"));

            // The Closed-path cancel: idempotent, and a late verdict is still discarded.
            vm.CancelOpenProbe();
            vm.CancelOpenProbe();

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.Connected, "Connection successful."));
            await vm.OpenProbeTask!;

            Assert.That(vm.StatusCard.Title, Is.EqualTo("Testing connection…"),
                "a verdict landing after cancellation is discarded, never applied to the card");
        }

        // ── User probe axis ─────────────────────────────────────────────

        [Test]
        public void EndUserProbe_RecordsTheVerdictOnTheCard()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var vm = CreateVm(applyService: new StubSettingsApplyService { PendingTestResult = pending });

            vm.BeginUserProbe();
            vm.EndUserProbe(new ConnectionProbeResult(
                ConnectionProbeStatus.Unreachable, "Could not reach the InvenTree server."));

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Connection failed"));
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(ServerConnectionIndicator.Failed));
                Assert.That(vm.StatusCard.ConnectionLine,
                            Is.EqualTo("Could not reach the InvenTree server."));
            });

            pending.SetCanceled();
        }

        [Test]
        public void EndUserProbe_WhenNull_ClearsInFlightWithoutRecordingAVerdict()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var vm = CreateVm(applyService: new StubSettingsApplyService { PendingTestResult = pending });

            vm.BeginUserProbe();
            vm.EndUserProbe(null);   // the exception path — nothing to record

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Not tested"));
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(ServerConnectionIndicator.NotTested));
                Assert.That(vm.StatusCard.ConnectionLine, Is.EqualTo("not tested yet"));
            });

            pending.SetCanceled();
        }

        [Test]
        public async Task ClearProbeVerdict_DropsTheSessionVerdictFromTheCard()
        {
            var vm = CreateVm();   // the synchronous stub settles the open probe to Connected
            await vm.OpenProbeTask!;
            Assert.That(vm.StatusCard.Title, Is.EqualTo("Connected"));

            vm.ClearProbeVerdict();

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Not tested"));
                Assert.That(vm.StatusCard.Indicator, Is.EqualTo(ServerConnectionIndicator.NotTested));
                Assert.That(vm.StatusCard.ConnectionLine, Is.EqualTo("not tested yet"));
            });
        }

        // ── Form commands ───────────────────────────────────────────────

        [Test]
        public void ToggleUrlSlice_ReturnsWhetherTheSliceIsNowOpen()
        {
            var vm = CreateVm();

            Assert.That(vm.ToggleUrlSlice(), Is.True);
            Assert.That(vm.ToggleUrlSlice(), Is.False);
        }

        [Test]
        public void ToggleCredentialSlice_ReturnsWhetherTheSliceIsNowOpen()
        {
            var vm = CreateVm();

            Assert.That(vm.ToggleCredentialSlice(), Is.True);
            Assert.That(vm.ToggleCredentialSlice(), Is.False);
        }

        [Test]
        public void CollapseCredentialForm_ClosesBothSlices()
        {
            var vm = CreateVm();
            vm.ToggleCredentialSlice();

            vm.CollapseCredentialForm();

            Assert.Multiple(() =>
            {
                Assert.That(vm.UrlSliceOpen, Is.False);
                Assert.That(vm.CredentialSliceOpen, Is.False);
                Assert.That(vm.CredentialFormOpen, Is.False);
            });
        }

        [Test]
        public void CollapseCredentialForm_WhenConfigIncomplete_CannotCloseTheForcedForm()
        {
            var vm = CreateVm(new StubConfigProvider("https://inventree.example.com", string.Empty));

            vm.CollapseCredentialForm();

            Assert.That(vm.CredentialFormOpen, Is.True,
                "the forced-open rule outranks the collapse — nothing to summarise yet");
        }

        // ── Coherent notifications + saved-config re-read ───────────────

        [Test]
        public void StateChange_RaisesStatusCardAndAllFormOutputsTogether()
        {
            var vm = CreateVm();
            var fired = new List<string?>();
            vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

            vm.ToggleUrlSlice();

            Assert.Multiple(() =>
            {
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.StatusCard)));
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.UrlSliceOpen)));
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.CredentialSliceOpen)));
                Assert.That(fired, Does.Contain(nameof(SettingsViewModel.CredentialFormOpen)));
            });
        }

        [Test]
        public void MarkPersisted_RecomputesStatusCardFromTheNewSavedConfig()
        {
            var provider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var vm = CreateVm(provider);

            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://other.example.com",
                ApiKey = string.Empty,
            });
            vm.MarkPersisted();

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.ServerLine, Is.EqualTo("https://other.example.com"));
                Assert.That(vm.StatusCard.Title, Is.EqualTo("Authentication required"),
                    "a missing saved key always wins over the session verdict");
                Assert.That(vm.StatusCard.IsComplete, Is.False);
            });
        }

        [Test]
        public void ReloadPersistedConfig_RecomputesStatusCardAndForcedOpenOutputs()
        {
            var provider = StubConfigProvider.WithNoSavedConfig();
            var vm = CreateVm(provider);

            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://inventree.example.com",
                ApiKey = "saved-key",
            });
            vm.ReloadPersistedConfig();

            Assert.Multiple(() =>
            {
                Assert.That(vm.StatusCard.IsSaved, Is.True);
                Assert.That(vm.StatusCard.IsComplete, Is.True);
                Assert.That(vm.CredentialFormOpen, Is.False,
                    "a complete saved config lifts the forced-open rule");
            });
        }
    }
}
