using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class SettingsWindowTests
    {
        private string _localMappingPath = null!;

        [SetUp]
        public void SetUp()
        {
            _localMappingPath = Path.Combine(Path.GetTempPath(),
                $"settings_window_mapping_{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_localMappingPath))
                File.Delete(_localMappingPath);
        }

        [Test]
        public void Constructor_WhenMappingProviderThrowsOnGetMappingResult_SetsRedMappingStatusAndDoesNotCrash()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                ThrowOnGet = new InvalidOperationException("Failed to load mapping file: C:\\temp\\missing.json"),
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("The Property Mapping file is invalid."));
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusError")));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenServiceThrowsConfigError_SetsActionStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: stub config failure"),
            };

            var window = CreateWindow(applyService: applyService);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.False);
            Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Failed to save server settings"));
            Assert.That(GetText(window, "ActionStatusText"), Does.Contain("stub config failure"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenServiceThrowsConfigError_LeavesTheStatusCardUnchanged()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: stub config failure"),
            };

            var window = CreateWindow(applyService: applyService);

            await window.ApplySettingsAsync();

            Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenMappingProviderThrowsOnRefresh_SetsActionStatus()
        {
            var applyService = new StubSettingsApplyService();
            var throwingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                ThrowOnGet = new InvalidOperationException("Failed to load mapping file: C:\\temp\\bad.json"),
            };

            var window = CreateWindow(
                applyService: applyService,
                mappingProvider: new StubPropertyMappingProvider(),
                mappingProviderFactory: new StubMappingProviderFactory { Factory = _ => throwingProvider });

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.False);
            Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Failed to load mapping file"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenSuccessful_SetsSavedStatusAndFiresMappingApplied()
        {
            var applyService = new StubSettingsApplyService();
            var mappingProvider = new StubPropertyMappingProvider();
            var window = CreateWindow(applyService: applyService, mappingProvider: mappingProvider);

            IPropertyMappingProvider? firedProvider = null;
            window.MappingApplied += (s, e) => firedProvider = e;

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.True);
            Assert.That(firedProvider, Is.SameAs(mappingProvider));
            Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Saved"));
        }

        [Test]
        public void ActionStatusText_IsReadOnlySelectableTextBox()
        {
            var window = CreateWindow();
            var element = LogicalTreeHelper.FindLogicalNode(window, "ActionStatusText");

            Assert.That(element, Is.InstanceOf<TextBox>());
            var textBox = (TextBox)element!;
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.Focusable, Is.True);
            Assert.That(textBox.IsTabStop, Is.False);
        }

        [Test]
        public async Task ActionStatusText_LongError_ToolTipContainsFullMessage()
        {
            var longMessage = "Failed to save server settings: " + new string('x', 500);
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(longMessage),
            };

            var window = CreateWindow(applyService: applyService);

            await window.ApplySettingsAsync();

            var textBox = (TextBox)LogicalTreeHelper.FindLogicalNode(window, "ActionStatusText")!;
            Assert.That(textBox.ToolTip, Is.InstanceOf<string>());
            Assert.That((string)textBox.ToolTip, Does.Contain(longMessage));
        }

        // ── Mapping health status bar ─────────────────────────────────────────

        [Test]
        public void Constructor_HealthyMapping_ShowsGreenStatusWithUpToDateMessage()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("up to date").IgnoreCase);
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusSuccess")));
        }

        [Test]
        public void Constructor_NeedsUpgradeMapping_ShowsAmberStatusWithOutOfDateMessage()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = "2" }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("Property Mapping Schema is out of date"));
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusWarning")));
        }

        [Test]
        public void Constructor_NewerSchemaMapping_ShowsAmberStatusWithUpgradeAddInPrompt()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = "4" }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            var statusText = GetText(window, "MappingStatusText");
            Assert.That(statusText, Does.Contain("newer").IgnoreCase);
            Assert.That(statusText, Does.Contain("add-in").IgnoreCase);
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusWarning")));
        }

        [Test]
        public void Constructor_InvalidMapping_ShowsRedStatusWithDefaultMessage()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Health = MappingHealth.Invalid,
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("The Property Mapping file is invalid."));
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusError")));
        }

        [Test]
        public void MappingStatusText_IsReadOnlySelectableTextBox()
        {
            var window = CreateWindow();
            var element = LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText");

            Assert.That(element, Is.InstanceOf<TextBox>());
            var textBox = (TextBox)element!;
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.Focusable, Is.True);
            Assert.That(textBox.IsTabStop, Is.False);
        }

        [Test]
        public void MappingStatusText_LongMessage_ToolTipContainsFullMessage()
        {
            var longMessage = "Invalid mapping file: " + new string('x', 500);
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Health = MappingHealth.Invalid,
                Message = longMessage
            };

            var window = CreateWindow(mappingProvider: mappingProvider);
            var textBox = (TextBox)LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText")!;

            Assert.That(textBox.ToolTip, Is.InstanceOf<string>());
            Assert.That((string)textBox.ToolTip, Does.Contain(longMessage));
            Assert.That(textBox.ToolTip, Is.EqualTo(textBox.Text));
        }

        [Test]
        public void MappingStatusText_NeedsUpgrade_MatchesToolTipAndShowsFullMessage()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = "2" }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);
            var textBox = (TextBox)LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText")!;

            Assert.That(textBox.Text, Does.Contain("The Property Mapping Schema is out of date."));
            Assert.That(textBox.Text, Does.Contain("Edit the Property Mapping and save to enable Part Sync."));
            Assert.That(textBox.ToolTip, Is.EqualTo(textBox.Text));
        }

        [Test]
        public void OnMappingChanged_RefreshesMappingStatus()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion }
            };
            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("up to date").IgnoreCase);

            mappingProvider.Health = MappingHealth.Invalid;
            mappingProvider.Message = "Invalid after change";
            mappingProvider.RaiseMappingChanged();

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("The Property Mapping file is invalid."));
            Assert.That(GetStripeBrush(window), Is.SameAs(GetBrush(window, "BrushStatusError")));
        }

        [Test]
        public async Task ApplySettingsAsync_WithNewProvider_RefreshesOnNewProviderMappingChanged()
        {
            var originalProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion }
            };
            var newProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Health = MappingHealth.Invalid,
                Message = "New provider invalid"
            };

            var factory = new StubMappingProviderFactory { Factory = _ => newProvider };
            var window = CreateWindow(mappingProvider: originalProvider, mappingProviderFactory: factory);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.True);

            newProvider.RaiseMappingChanged();

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("The Property Mapping file is invalid."));
        }

        // ── Edit Mappings button ───────────────────────────────────────────────

        [Test]
        public void Constructor_SharedHealthyMapping_ShowsEditSharedMappingsButtonAndEnabled()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                SourceFilePath = "C:\\shared.json",
                SourceFileExists = true,
                Config = new PropertyMappingConfig { SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            var button = GetButton(window, "EditMappingsButton");
            Assert.That(button.IsEnabled, Is.True);
            Assert.That(GetText(window, "EditMappingsButtonText"), Is.EqualTo("Edit Shared Mappings"));
        }

        [Test]
        public void Constructor_LocalHealthyMapping_ShowsEditLocalMappingsButtonAndEnabled()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Config = new PropertyMappingConfig { SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion }
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            var button = GetButton(window, "EditMappingsButton");
            Assert.That(button.IsEnabled, Is.True);
            Assert.That(GetText(window, "EditMappingsButtonText"), Is.EqualTo("Edit Local Mappings"));
        }

        [Test]
        public void Constructor_InvalidMapping_DisablesEditMappingsButton()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Health = MappingHealth.Invalid,
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            var button = GetButton(window, "EditMappingsButton");
            Assert.That(button.IsEnabled, Is.False);
        }

        // ── Status card per saved state (#233) ─────────────────────────────
        // The card appears once anything is saved; the layout never changes
        // between states — the title, dot, and line values do.

        [Test]
        public void Constructor_WithNoSavedConfig_HidesStatusCard()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetElement(window, "ConnectionCard").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_WithNoSavedConfig_ShowsTheFormDirectly()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
            });
        }

        [Test]
        public void Constructor_WithServerOnlySaved_ShowsAuthenticationRequiredCard()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "ConnectionCard").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Authentication required"));
                Assert.That(GetText(window, "ConnectionCardServer"),
                            Is.EqualTo("https://inventree.example.com"));
                Assert.That(GetText(window, "ConnectionCardCredential"),
                            Is.EqualTo("none saved"));
            });
        }

        [Test]
        public void Constructor_WithServerOnlySaved_UsesAmberDot()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.That(GetDot(window).Fill, Is.SameAs(GetBrush(window, "BrushStatusWarning")));
        }

        [Test]
        public void Constructor_WithServerOnlySaved_HidesCardToolbar()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.That(GetElement(window, "ConnectionCardToolbar").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_WithServerOnlySaved_ShowsTheFormDirectly()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
            });
        }

        [Test]
        public void Constructor_WithFullConfig_ShowsTestingCardWhileProbeInFlight()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Testing connection…"));
                Assert.That(GetText(window, "ConnectionCardServer"),
                            Is.EqualTo("https://inventree.example.com"));
                Assert.That(GetText(window, "ConnectionCardCredential"),
                            Is.EqualTo("API key saved"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Is.EqualTo("testing…"));
            });

            pending.SetCanceled();
        }

        [Test]
        public void Constructor_WithFullConfig_UsesBlueDotWhileProbing()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetDot(window).Fill, Is.SameAs(GetBrush(window, "BrushAccentBlue")));

            pending.SetCanceled();
        }

        [Test]
        public void Constructor_WithFullConfig_ShowsCardToolbar()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "ConnectionCardToolbar").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetButton(window, "ChangeServerButton"), Is.Not.Null);
                Assert.That(GetButton(window, "ChangeCredentialButton"), Is.Not.Null);
                Assert.That(GetButton(window, "RemoveApiKeyButton"), Is.Not.Null);
            });
        }

        [Test]
        public void Constructor_WithFullConfig_HidesTheForm()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetCredentialForm(window).Visibility,
                            Is.EqualTo(Visibility.Collapsed));
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void StatusCard_NeverShowsTheSavedApiKey()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "super-secret-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardServer"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardCredential"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetPasswordBox(window, "ApiKeyBox").Password, Is.Empty,
                            "the saved key is never re-shown in the field");
            });
        }

        // The mode switcher and the masked/revealed field pair are gone — one
        // form, one masked key field, no Show/Hide control.
        [Test]
        public void Constructor_NoModeSwitcherOrRevealControlsRemain()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "AccountModeButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiKeyModeButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ShowApiKeyButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiBox"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiKeyMaskedBox"), Is.Null);
            });
        }

        // ── Probe on open (#234) ───────────────────────────────────────
        // A complete saved config gets a live probe on every open: the card
        // shows Testing while it runs, then the verdict — never a stale saved
        // claim. Fresh and auth-required states start nothing; Close and a
        // new Apply both cancel the probe and discard any late result.

        [Test]
        public void Constructor_WithSavedKey_StartsOpenProbeWithSavedValues()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(window.OpenProbeTask, Is.Not.Null);
                Assert.That(applyService.TestCallCount, Is.EqualTo(1));
                Assert.That(applyService.LastTestInput!.Url,
                            Is.EqualTo("https://inventree.example.com"));
                Assert.That(applyService.LastTestInput.RawApiKey, Is.EqualTo("saved-key"));
                Assert.That(applyService.LastTestInput.Username, Is.Empty);
                Assert.That(applyService.LastTestInput.Password, Is.Empty);
            });

            pending.SetCanceled();
            WaitForProbe(window);
        }

        [Test]
        public void Constructor_WithNoSavedConfig_DoesNotProbe()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(window.OpenProbeTask, Is.Null);
                Assert.That(applyService.TestCallCount, Is.EqualTo(0));
            });
        }

        [Test]
        public void Constructor_WithServerOnlySaved_DoesNotProbe()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(window.OpenProbeTask, Is.Null);
                Assert.That(applyService.TestCallCount, Is.EqualTo(0));
            });
        }

        [Test]
        public void OpenProbe_WhenConnected_SettlesCardToConnected()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.Connected, "Connection successful."));
            WaitForProbe(window);

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Is.EqualTo("last test succeeded"));
                Assert.That(GetDot(window).Fill,
                            Is.SameAs(GetBrush(window, "BrushStatusSuccess")));
                Assert.That(GetText(window, "ActionStatusText"), Is.Empty,
                            "the open probe writes to the card only, never the status bar");
            });
        }

        [Test]
        public void OpenProbe_WhenUnreachable_SettlesCardToFailedWithDetail()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.Unreachable,
                "Could not reach the InvenTree server. Check the URL and network connection."));
            WaitForProbe(window);

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Connection failed"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Contain("Could not reach"));
                Assert.That(GetDot(window).Fill,
                            Is.SameAs(GetBrush(window, "BrushStatusError")));
            });
        }

        [Test]
        public void OpenProbe_WhenCredentialRejected_SettlesCardToAuthenticationRequired()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            pending.SetResult(new ConnectionProbeResult(
                ConnectionProbeStatus.CredentialRejected,
                "The server rejected the API key (401 Unauthorized)."));
            WaitForProbe(window);

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Authentication required"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Contain("rejected the API key"));
            });
        }

        [Test, Timeout(15000)]
        public void OpenProbe_WhenWindowClosed_DiscardsTheLateResult()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();
            SolidWorksWindowHandle.Set(form.Handle);

            try
            {
                var pending = new TaskCompletionSource<ConnectionProbeResult>();
                var applyService = new StubSettingsApplyService { PendingTestResult = pending };
                var window = CreateWindow(
                    applyService: applyService,
                    configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

                window.Show();
                Assert.That(
                    HiddenTestWindow.IsOnScreen(
                        HiddenTestWindow.GetRect(new WindowInteropHelper(window).Handle)),
                    Is.False, "Test dialog must stay off every monitor");
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Testing connection…"));

                window.Close();
                WaitForProbe(window);

                pending.SetResult(new ConnectionProbeResult(
                    ConnectionProbeStatus.Connected, "Connection successful."));

                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Testing connection…"),
                            "a verdict landing after Close is discarded");
            }
            finally
            {
                SolidWorksWindowHandle.Set(IntPtr.Zero);
            }
        }

        [Test, Timeout(15000)]
        public async Task Apply_WhenOpenProbeInFlight_CancelsItAndAppliesOwnVerdict()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider) { PendingTestResult = pending };
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            bool result = await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(window.OpenProbeTask, Is.Not.Null);
            });
            WaitForProbe(window);

            Assert.Multiple(() =>
            {
                Assert.That(applyService.LastTestToken.IsCancellationRequested, Is.True,
                            "starting Apply cancels the in-flight open probe");
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"),
                            "the apply's own verdict supersedes the open probe");
            });
        }

        // ── Dots placeholder (#233) ────────────────────────────────────────
        // Dots stand in for a saved key — shown only when a key is actually
        // saved, never for a server-only config, and never as editable text.

        [Test]
        public void Constructor_WithSavedKey_ShowsDotsOnApiKeyField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Constructor_WithServerOnlySaved_ShowsNoDotsOnApiKeyField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_WithNoSavedConfig_ShowsNoDotsOnApiKeyField()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void ApiKeyBox_WhenTyped_HidesDotsPlaceholder()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        // ── Card toolbar actions (#233) ────────────────────────────────────

        [Test]
        public void ChangeCredential_WhenClicked_RevealsOnlyTheCredentialFields()
        {
            var window = CreateWindow();

            Click(window, "ChangeCredentialButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void ChangeCredential_WhenClickedTwice_HidesTheFormAgain()
        {
            var window = CreateWindow();

            Click(window, "ChangeCredentialButton");
            Click(window, "ChangeCredentialButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void ChangeServer_WhenClicked_RevealsOnlyTheUrlField()
        {
            var window = CreateWindow();

            Click(window, "ChangeServerButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void ChangeServer_AfterChangeCredential_SwitchesPanels()
        {
            var window = CreateWindow();

            Click(window, "ChangeCredentialButton");
            Click(window, "ChangeServerButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        // ── Dirty gating (#233) ────────────────────────────────────────────
        // Apply/Save enable only when a persistable change exists. Blank or
        // half-typed credential fields never count.

        [Test]
        public void Constructor_FreshState_ApplyAndSaveStartDisabled()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False);
                Assert.That(GetButton(window, "SaveButton").IsEnabled, Is.False);
            });
        }

        [Test]
        public void Constructor_ConfiguredState_ApplyAndSaveStartDisabled()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False);
                Assert.That(GetButton(window, "SaveButton").IsEnabled, Is.False);
            });
        }

        [Test]
        public void UrlField_WhenChanged_EnablesApplyAndSave()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";

            Assert.Multiple(() =>
            {
                Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.True);
                Assert.That(GetButton(window, "SaveButton").IsEnabled, Is.True);
            });
        }

        [Test]
        public void UsernameAlone_DoesNotEnableApply()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetTextBox(window, "UsernameBox")!.Text = "engineer";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False);
        }

        [Test]
        public void PasswordAlone_DoesNotEnableApply()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False);
        }

        [Test]
        public void UsernameAndPassword_WhenComplete_EnableApply()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.True);
        }

        [Test]
        public void ApiKeyDraft_WhenTyped_EnablesApply()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.True);
        }

        [Test]
        public void ApiKeyDraft_WhenWhitespaceOnly_DoesNotEnableApply()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetPasswordBox(window, "ApiKeyBox").Password = "   ";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False);
        }

        [Test]
        public void BomKeyword_WhenChanged_EnablesApply()
        {
            var window = CreateWindow();

            GetTextBox(window, "BomKeywordBox")!.Text = "custom-bom";

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.True);
        }

        [Test]
        public void SharedRadio_WhenChecked_EnablesApply()
        {
            var window = CreateWindow();

            GetRadioButton(window, "SharedRadio").IsChecked = true;

            Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.True);
        }

        // ── Credential precedence through the apply seam (#233) ────────────
        // One form, key-wins: the typed key draft beats a complete pair, a
        // complete pair beats the saved key, and an untouched credential axis
        // keeps the saved key so a URL-only edit still probes with it.

        [Test]
        public async Task Apply_WhenBothCredentialPathsFilled_SendsTheApiKeyDraft()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-typed";

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applyService.LastInput!.RawApiKey, Is.EqualTo("inv-typed"));
                Assert.That(applyService.LastInput.Username, Is.Empty);
                Assert.That(applyService.LastInput.Password, Is.Empty);
            });
        }

        [Test]
        public async Task Apply_WithCompletePair_SendsUsernameAndPassword()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applyService.LastInput!.Username, Is.EqualTo("engineer"));
                Assert.That(applyService.LastInput.Password, Is.EqualTo("s3cret"));
                Assert.That(applyService.LastInput.RawApiKey, Is.Empty);
            });
        }

        [Test]
        public async Task Apply_WithCompletePair_OverridesSavedKey()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applyService.LastInput!.Username, Is.EqualTo("engineer"));
                Assert.That(applyService.LastInput.Password, Is.EqualTo("s3cret"));
                Assert.That(applyService.LastInput.RawApiKey, Is.Empty);
            });
        }

        [Test]
        public async Task Apply_WithUrlOnlyChange_KeepsTheSavedKey()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            GetTextBox(window, "UrlBox")!.Text = "https://other.example.com";

            await window.ApplySettingsAsync();

            Assert.That(applyService.LastInput!.RawApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public async Task Apply_WithHalfTypedPair_KeepsTheSavedKey()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            GetTextBox(window, "UsernameBox")!.Text = "engineer";

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applyService.LastInput!.RawApiKey, Is.EqualTo("saved-key"));
                Assert.That(applyService.LastInput.Username, Is.Empty);
            });
        }

        // ── Apply reports the probe outcome on card + status bar (#232/#233)
        // The service persists first, then probes: a failed probe is a reported
        // outcome, not an apply failure, so Apply still proceeds.

        [Test]
        public async Task ApplySettingsAsync_PassesAnHttpClientToTheApplyService()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(applyService: applyService);

            await window.ApplySettingsAsync();

            Assert.That(applyService.LastApplyClient, Is.Not.Null);
        }

        [Test]
        public async Task Apply_WhenProbeConnected_CardShowsConnected()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Is.EqualTo("last test succeeded"));
                Assert.That(GetDot(window).Fill,
                            Is.SameAs(GetBrush(window, "BrushStatusSuccess")));
            });
        }

        [Test]
        public async Task Apply_WhenProbeUnreachable_CardShowsFailedWithDetail()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider)
            {
                ResultToReturnOnApply = new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable, "Could not reach the InvenTree server."),
            };
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Connection failed"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Contain("Could not reach"));
                Assert.That(GetDot(window).Fill,
                            Is.SameAs(GetBrush(window, "BrushStatusError")));
            });
        }

        [Test]
        public async Task Apply_WhenCredentialRejected_CardShowsAuthenticationRequired()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider)
            {
                ResultToReturnOnApply = new ConnectionProbeResult(
                    ConnectionProbeStatus.CredentialRejected,
                    "The server rejected the API key (401 Unauthorized)."),
            };
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Authentication required"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Contain("rejected the API key"));
            });
        }

        [Test]
        public async Task Apply_WhenProbeFails_ReturnsTrueAndReportsOutcome()
        {
            var applyService = new StubSettingsApplyService
            {
                ResultToReturnOnApply = new ConnectionProbeResult(
                    ConnectionProbeStatus.CredentialRejected,
                    "The server rejected the API key (401 Unauthorized)."),
            };
            var window = CreateWindow(applyService: applyService);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.True);
            Assert.That(GetText(window, "ActionStatusText"),
                        Does.Contain("Saved").And.Contain("connection failed"));
        }

        [Test]
        public async Task Apply_WhenProbeFails_StillFiresMappingApplied()
        {
            var applyService = new StubSettingsApplyService
            {
                ResultToReturnOnApply = new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable,
                    "Could not reach the InvenTree server."),
            };
            var mappingProvider = new StubPropertyMappingProvider();
            var window = CreateWindow(applyService: applyService, mappingProvider: mappingProvider);

            IPropertyMappingProvider? firedProvider = null;
            window.MappingApplied += (s, e) => firedProvider = e;

            await window.ApplySettingsAsync();

            Assert.That(firedProvider, Is.SameAs(mappingProvider));
        }

        [Test]
        public async Task Apply_WhenProbeSucceeds_ReportsSavedAndConnected()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(applyService: applyService);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.True);
            Assert.That(GetText(window, "ActionStatusText"),
                        Does.Contain("Saved").And.Contain("connection successful"));
        }

        [Test]
        public async Task Apply_FromFreshState_RevealsTheStatusCard()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            await window.ApplySettingsAsync();

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "ConnectionCard").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed),
                            "a complete config collapses the form back to the card");
                Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                            Is.EqualTo(Visibility.Visible),
                            "the freshly saved key shows as dots");
            });
        }

        [Test]
        public async Task Apply_WhenPasswordSent_ClearsPasswordBox()
        {
            var window = CreateWindow(
                configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            await window.ApplySettingsAsync();

            Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty);
        }

        // ── Test connection (#233) ──────────────────────────────────────────
        // Test lives in the footer, never saves, and reports on the status bar
        // while the card takes the probe result.

        [Test]
        public void TestConnectionButton_IsOutsideCollapsibleCredentialForm()
        {
            var window = CreateWindow();

            Assert.That(IsInsideCredentialForm(window, "TestConnectionButton"), Is.False);
        }

        [Test]
        public void TestConnectionButton_WithNoUrlAnywhere_IsDisabled()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetButton(window, "TestConnectionButton").IsEnabled, Is.False);
        }

        [Test]
        public void TestConnectionButton_WhenUrlTyped_IsEnabled()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";

            Assert.That(GetButton(window, "TestConnectionButton").IsEnabled, Is.True);
        }

        [Test]
        public void TestConnectionButton_WhenUrlFieldClearedButUrlSaved_StaysEnabled()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            GetTextBox(window, "UrlBox")!.Text = string.Empty;

            Assert.That(GetButton(window, "TestConnectionButton").IsEnabled, Is.True);
        }

        [Test]
        public void Test_WhenProbeInFlight_ReportsTestingInActionStatus()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            Click(window, "TestConnectionButton");

            Assert.That(GetText(window, "ActionStatusText"),
                        Is.EqualTo("Testing connection…"));

            pending.SetCanceled();
        }

        [Test]
        public void Test_WhenKeyDraftWins_StillClearsTypedPassword()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";  // key wins — password never sent

            Click(window, "TestConnectionButton");

            Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty);
        }

        [Test]
        public async Task Apply_WhenKeyDraftWins_StillClearsTypedPassword()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";  // key wins — password never sent

            await window.ApplySettingsAsync();

            Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty);
        }

        [Test]
        public void Test_WhenProbeSucceeds_ReportsSuccessAndUpdatesCard()
        {
            var window = CreateWindow();

            Click(window, "TestConnectionButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ActionStatusText"),
                            Does.Contain("Connection successful"));
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
            });
        }

        [Test]
        public void Test_WhenProbeFails_ReportsOutcomeAndUpdatesCard()
        {
            var applyService = new StubSettingsApplyService
            {
                ResultToReturnOnTestConnection = new ConnectionProbeResult(
                    ConnectionProbeStatus.Unreachable,
                    "Could not reach the InvenTree server."),
            };
            var window = CreateWindow(applyService: applyService);

            Click(window, "TestConnectionButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Could not reach"));
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Connection failed"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Contain("Could not reach"));
            });
        }

        [Test]
        public void Test_WhenPasswordSent_ClearsPasswordBox()
        {
            var window = CreateWindow(
                configProvider: StubConfigProvider.WithNoSavedConfig());
            GetTextBox(window, "UrlBox")!.Text = "https://inventree.example.com";
            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            Click(window, "TestConnectionButton");

            Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty);
        }

        // Clicking Save runs synchronously to the early return: the stub apply
        // service throws before yielding, so DialogResult is never set and the
        // (unshown) dialog stays open.
        [Test]
        public void Save_Click_WhenApplyFails_StaysOpenAndShowsErrorStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: Server responded: 500"),
            };
            var window = CreateWindow(applyService: applyService);

            Click(window, "SaveButton");

            Assert.That(window.DialogResult, Is.Null);
            Assert.That(GetText(window, "ActionStatusText"),
                        Does.Contain("Failed to save server settings"));
        }

        [Test, Timeout(10000)]
        public void Save_Click_WhenApplySucceeds_ClosesDialogWithTrueResult()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();
            SolidWorksWindowHandle.Set(form.Handle);

            try
            {
                var window = CreateWindow();
                Exception? assertFailure = null;

                window.ContentRendered += (s, e) =>
                {
                    var timer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(300),
                    };

                    timer.Tick += (s2, e2) =>
                    {
                        timer.Stop();

                        try
                        {
                            var helper = new WindowInteropHelper(window);
                            var dialogRect = HiddenTestWindow.GetRect(helper.Handle);

                            Assert.That(
                                HiddenTestWindow.IsOnScreen(dialogRect),
                                Is.False, "Test dialog must stay off every monitor");
                        }
                        catch (Exception ex)
                        {
                            assertFailure = ex;
                        }

                        Click(window, "SaveButton");
                    };

                    timer.Start();
                };

                var result = window.ShowDialog();

                if (assertFailure != null)
                    Assert.Fail($"Off-screen assertion failed: {assertFailure.Message}");

                Assert.That(result, Is.EqualTo(true));
                Assert.That(window.DialogResult, Is.EqualTo(true));
                Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Saved"));
            }
            finally
            {
                SolidWorksWindowHandle.Set(IntPtr.Zero);
            }
        }

        // ── Remove API key (#232/#233) ─────────────────────────────────────
        // Remove clears only the credential: the server URL, Property Mapping
        // path, and BOM keyword survive, and the card lands on the
        // authentication-required state with the form open.

        [Test]
        public void RemoveApiKeyButton_IsInsideCardToolbar()
        {
            var window = CreateWindow();

            Assert.That(IsInside(window, "RemoveApiKeyButton", "ConnectionCardToolbar"), Is.True);
        }

        [Test]
        public void RemoveApiKey_WhenClicked_CallsRemoveApiKeyAsync()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.That(applyService.RemoveCallCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveApiKey_WhenClicked_LandsOnAuthenticationRequiredWithServerKept()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Authentication required"), "card title");
                Assert.That(GetText(window, "ConnectionCardServer"),
                            Is.EqualTo("https://inventree.example.com"), "server kept");
                Assert.That(GetText(window, "ConnectionCardCredential"),
                            Is.EqualTo("none saved"), "credential line");
                Assert.That(GetElement(window, "ConnectionCardToolbar").Visibility,
                            Is.EqualTo(Visibility.Collapsed), "toolbar gone in auth state");
            });
        }

        [Test]
        public void RemoveApiKey_WhenClicked_ShowsTheFormDirectly()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
            });
        }

        [Test]
        public void RemoveApiKey_WhenClicked_ResetsCredentialFieldsButKeepsServerUrl()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetTextBox(window, "UrlBox")!.Text,
                            Is.EqualTo("https://inventree.example.com"), "UrlBox");
                Assert.That(GetTextBox(window, "UsernameBox")!.Text, Is.Empty, "UsernameBox");
                Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty, "PasswordBox");
                Assert.That(GetPasswordBox(window, "ApiKeyBox").Password, Is.Empty, "ApiKeyBox");
                Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                            Is.EqualTo(Visibility.Collapsed), "no dots without a saved key");
            });
        }

        [Test]
        public void RemoveApiKey_WhenClicked_ReportsCredentialRemovedInActionStatus()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.That(GetText(window, "ActionStatusText"),
                        Does.Contain("Credential removed"));
        }

        [Test]
        public void RemoveApiKey_WhenClicked_LeavesTheDialogClean()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            Click(window, "RemoveApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetButton(window, "ApplyButton").IsEnabled, Is.False, "ApplyButton.IsEnabled");
                Assert.That(GetText(window, "CancelButtonText"), Is.EqualTo("Close"), "CancelButtonText");
            });
        }

        [Test]
        public void RemoveApiKey_WhenServiceThrows_ShowsErrorInActionStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnRemove = new SettingsApplyException(
                    "Failed to remove the API key: stub delete failure"),
            };
            var window = CreateWindow(applyService: applyService);

            Click(window, "RemoveApiKeyButton");

            Assert.That(GetText(window, "ActionStatusText"),
                        Does.Contain("Failed to remove the API key"));
        }

        [Test]
        public void RemoveApiKey_WhenServiceThrows_LeavesConfiguredStatusCard()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnRemove = new SettingsApplyException(
                    "Failed to remove the API key: stub delete failure"),
            };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Click(window, "RemoveApiKeyButton");

            Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
        }

        // ── Credential form container ────────────────────────────────────────

        [Test]
        public void CredentialForm_IsScrollViewerWithBoundedMaxHeight()
        {
            var window = CreateWindow();

            Assert.That(GetCredentialForm(window).MaxHeight, Is.LessThan(double.PositiveInfinity));
        }

        [Test]
        public void CredentialForm_ContainsServerUrlField()
        {
            var window = CreateWindow();

            Assert.That(IsInsideCredentialForm(window, "UrlBox"), Is.True);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // The open probe applies its verdict via Dispatcher.Invoke from a pool
        // thread, so a plain await in the test deadlocks — NUnit does not pump
        // the dispatcher while it waits. Pump a DispatcherFrame until the task
        // completes instead.
        private static void WaitForProbe(SettingsWindow window)
        {
            var task = window.OpenProbeTask;
            Assert.That(task, Is.Not.Null, "no open probe was started");

            var frame = new DispatcherFrame();
            task!.ContinueWith(
                _ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        private static ScrollViewer GetCredentialForm(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "CredentialFormScroll") as ScrollViewer;
            Assert.That(element, Is.Not.Null, "Could not find ScrollViewer named 'CredentialFormScroll'.");
            return element!;
        }

        private static bool IsInsideCredentialForm(Window window, string name) =>
            IsInside(window, name, "CredentialFormScroll");

        private static bool IsInside(Window window, string elementName, string ancestorName)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, elementName) as DependencyObject;
            Assert.That(element, Is.Not.Null, $"Could not find element named '{elementName}'.");

            var ancestor = LogicalTreeHelper.FindLogicalNode(window, ancestorName) as DependencyObject;
            Assert.That(ancestor, Is.Not.Null, $"Could not find element named '{ancestorName}'.");

            for (var parent = LogicalTreeHelper.GetParent(element!); parent != null;
                 parent = LogicalTreeHelper.GetParent(parent))
            {
                if (ReferenceEquals(parent, ancestor)) return true;
            }

            return false;
        }

        private static void Click(Window window, string name) =>
            GetButton(window, name).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        private static string GetText(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name);
            var textBlock = element as TextBlock;
            var textBox = element as TextBox;
            Assert.That(textBlock ?? (object?)textBox, Is.Not.Null, $"Could not find TextBlock or TextBox named '{name}'.");
            return textBlock?.Text ?? textBox?.Text ?? string.Empty;
        }

        private static FrameworkElement GetElement(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as FrameworkElement;
            Assert.That(element, Is.Not.Null, $"Could not find element named '{name}'.");
            return element!;
        }

        private static Ellipse GetDot(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "ConnectionStatusDot") as Ellipse;
            Assert.That(element, Is.Not.Null, "Could not find Ellipse named 'ConnectionStatusDot'.");
            return element!;
        }

        private static RadioButton GetRadioButton(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as RadioButton;
            Assert.That(element, Is.Not.Null, $"Could not find RadioButton named '{name}'.");
            return element!;
        }

        private static PasswordBox GetPasswordBox(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as PasswordBox;
            Assert.That(element, Is.Not.Null, $"Could not find PasswordBox named '{name}'.");
            return element!;
        }

        private static Button GetButton(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name);
            var button = element as Button;
            Assert.That(button, Is.Not.Null, $"Could not find Button named '{name}'.");
            return button!;
        }

        private SettingsWindow CreateWindow(
            IPropertyMappingProvider? mappingProvider = null,
            ISettingsApplyService? applyService = null,
            IMappingProviderFactory? mappingProviderFactory = null,
            IConfigProvider? configProvider = null)
        {
            mappingProvider ??= new StubPropertyMappingProvider();
            applyService ??= new StubSettingsApplyService();
            mappingProviderFactory ??= new StubMappingProviderFactory
            {
                Factory = _ => mappingProvider,
            };
            configProvider ??= new StubConfigProvider("https://example.com", "stub-key");

            var versionInfo = new StubVersionInfo();

            return new SettingsWindow(
                configProvider,
                mappingProvider,
                versionInfo,
                applyService,
                mappingProviderFactory);
        }

        private static TextBox? GetTextBox(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name);
            return element as TextBox;
        }

        private static Brush GetStripeBrush(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "MappingStatusStripe");
            var border = (Border?)element;
            Assert.That(border, Is.Not.Null, "Could not find MappingStatusStripe.");
            return border!.Background!;
        }

        private static Brush GetBrush(Window window, string key)
        {
            var brush = window.TryFindResource(key) as Brush;
            Assert.That(brush, Is.Not.Null, $"Could not find resource '{key}'.");
            return brush!;
        }
    }
}
