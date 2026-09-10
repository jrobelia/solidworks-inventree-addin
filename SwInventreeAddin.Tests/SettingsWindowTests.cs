using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

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
        public async Task ApplySettingsAsync_WhenServiceThrowsConfigError_DoesNotWriteToConnectionStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: stub config failure"),
            };

            var window = CreateWindow(applyService: applyService);

            await window.ApplySettingsAsync();

            Assert.That(GetText(window, "ConnectionStatusText"), Is.Empty);
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
        public async Task ApplySettingsAsync_WhenSuccessful_SetsAppliedStatusAndFiresMappingApplied()
        {
            var applyService = new StubSettingsApplyService();
            var mappingProvider = new StubPropertyMappingProvider();
            var window = CreateWindow(applyService: applyService, mappingProvider: mappingProvider);

            IPropertyMappingProvider? firedProvider = null;
            window.MappingApplied += (s, e) => firedProvider = e;

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.True);
            Assert.That(firedProvider, Is.SameAs(mappingProvider));
            Assert.That(GetText(window, "ActionStatusText"), Does.Contain("Settings applied"));
        }

        [Test]
        public void ConnectionStatusText_IsReadOnlySelectableTextBox()
        {
            var window = CreateWindow();
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, "ConnectionStatusText");

            Assert.That(element, Is.InstanceOf<TextBox>());
            var textBox = (TextBox)element!;
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.Focusable, Is.True);
            Assert.That(textBox.IsTabStop, Is.False);
        }

        [Test]
        public void ActionStatusText_IsReadOnlySelectableTextBox()
        {
            var window = CreateWindow();
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, "ActionStatusText");

            Assert.That(element, Is.InstanceOf<TextBox>());
            var textBox = (TextBox)element!;
            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.Focusable, Is.True);
            Assert.That(textBox.IsTabStop, Is.False);
        }

        [Test]
        public void ConnectionStatusText_LongError_ToolTipContainsFullMessage()
        {
            var longMessage = "Connection failed: " + new string('x', 500);

            var window = CreateWindow();
            window.SetConnectionStatus(longMessage, StatusSeverity.Error);

            var textBox = (TextBox)System.Windows.LogicalTreeHelper.FindLogicalNode(window, "ConnectionStatusText")!;
            Assert.That(textBox.ToolTip, Is.InstanceOf<string>());
            Assert.That((string)textBox.ToolTip, Does.Contain(longMessage));
            Assert.That(textBox.ToolTip, Is.EqualTo(textBox.Text));
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

            var textBox = (TextBox)System.Windows.LogicalTreeHelper.FindLogicalNode(window, "ActionStatusText")!;
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
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText");

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
            var textBox = (TextBox)System.Windows.LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText")!;

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
            var textBox = (TextBox)System.Windows.LogicalTreeHelper.FindLogicalNode(window, "MappingStatusText")!;

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

        // ── Connection status card ────────────────────────────────────────────

        [Test]
        public void Constructor_WithSavedServerConfig_ShowsApiKeySavedOnStatusCard()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetText(window, "ConnectionCardText"),
                        Is.EqualTo("Server connection configured \u2014 API key saved"));
        }

        [Test]
        public void Constructor_WithSavedServerConfig_ShowsServerUrlOnStatusCard()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetText(window, "ConnectionCardUrl"), Is.EqualTo("https://inventree.example.com"));
        }

        [Test]
        public void Constructor_WithNoSavedServerConfig_ShowsNoServerSettingsSaved()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetText(window, "ConnectionCardText"), Is.EqualTo("No server settings saved"));
        }

        [Test]
        public void Constructor_WithNoSavedServerConfig_HidesServerUrlOnStatusCard()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());
            var url = (TextBlock)LogicalTreeHelper.FindLogicalNode(window, "ConnectionCardUrl")!;

            Assert.That(url.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void ConnectionCardText_DoesNotRevealSavedApiKey()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "super-secret-key"));

            Assert.That(GetText(window, "ConnectionCardText"), Does.Not.Contain("super-secret-key"));
        }

        // ── Edit connection disclosure ────────────────────────────────────────

        [Test]
        public void Constructor_ByDefault_CollapsesCredentialForm()
        {
            var window = CreateWindow();

            Assert.That(GetCredentialForm(window).Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_ByDefault_ShowsEditConnectionLabel()
        {
            var window = CreateWindow();

            Assert.That(GetText(window, "EditConnectionButtonText"), Is.EqualTo("Edit connection"));
        }

        [Test]
        public void EditConnection_WhenClicked_ExpandsCredentialForm()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");

            Assert.That(GetCredentialForm(window).Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void EditConnection_WhenClicked_ShowsCollapseLabel()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");

            Assert.That(GetText(window, "EditConnectionButtonText"), Is.EqualTo("Hide connection"));
        }

        [Test]
        public void EditConnection_WhenClickedTwice_CollapsesCredentialFormAgain()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");
            Click(window, "EditConnectionButton");

            Assert.That(GetCredentialForm(window).Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void EditConnection_WhenClickedTwice_RestoresEditConnectionLabel()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");
            Click(window, "EditConnectionButton");

            Assert.That(GetText(window, "EditConnectionButtonText"), Is.EqualTo("Edit connection"));
        }

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

        // The Test Connection button and its status bar stay usable while the
        // credential form is collapsed, so they live outside the ScrollViewer (#211).
        [Test]
        public void TestConnectionButton_WhenCredentialFormCollapsed_StaysVisible()
        {
            var window = CreateWindow();

            Assert.That(GetButton(window, "TestConnectionButton").Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void TestConnectionButton_IsOutsideCollapsibleCredentialForm()
        {
            var window = CreateWindow();

            Assert.That(IsInsideCredentialForm(window, "TestConnectionButton"), Is.False);
        }

        [Test]
        public void ConnectionStatusBar_IsOutsideCollapsibleCredentialForm()
        {
            var window = CreateWindow();

            Assert.That(IsInsideCredentialForm(window, "ConnectionStatusBar"), Is.False);
        }

        [Test]
        public void ConnectionStatusBar_WhenCredentialFormCollapsed_StaysVisible()
        {
            var window = CreateWindow();
            var statusBar = (Border)LogicalTreeHelper.FindLogicalNode(window, "ConnectionStatusBar")!;

            Assert.That(statusBar.Visibility, Is.EqualTo(Visibility.Visible));
        }

        // ── Credential mode switcher (#212) ───────────────────────────────────

        [Test]
        public void ModeButtons_WhenCredentialFormExpanded_AreInsideTheForm()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");

            Assert.Multiple(() =>
            {
                Assert.That(IsInsideCredentialForm(window, "AccountModeButton"), Is.True,
                            "AccountModeButton should live inside the credential form.");
                Assert.That(IsInsideCredentialForm(window, "ApiKeyModeButton"), Is.True,
                            "ApiKeyModeButton should live inside the credential form.");
            });
        }

        [Test]
        public void ModeButtons_WhenCredentialFormExpanded_AreBothVisible()
        {
            var window = CreateWindow();

            Click(window, "EditConnectionButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetButton(window, "AccountModeButton").Visibility, Is.EqualTo(Visibility.Visible),
                            "AccountModeButton visibility");
                Assert.That(GetButton(window, "ApiKeyModeButton").Visibility, Is.EqualTo(Visibility.Visible),
                            "ApiKeyModeButton visibility");
            });
        }

        [Test]
        public void Constructor_WithNoSavedApiKey_ShowsAccountForm()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Assert.That(GetPanel(window, "AccountFormPanel").Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Constructor_WithNoSavedApiKey_HidesApiKeyForm()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Assert.That(GetPanel(window, "ApiKeyFormPanel").Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void ApiKeyMode_WhenClicked_ShowsApiKeyFormAndHidesAccountForm()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Click(window, "ApiKeyModeButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetPanel(window, "ApiKeyFormPanel").Visibility, Is.EqualTo(Visibility.Visible),
                            "ApiKeyFormPanel visibility");
                Assert.That(GetPanel(window, "AccountFormPanel").Visibility, Is.EqualTo(Visibility.Collapsed),
                            "AccountFormPanel visibility");
            });
        }

        [Test]
        public void ApiKeyMode_WhenClicked_MarksApiKeyButtonAsActive()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Click(window, "ApiKeyModeButton");

            Assert.That(GetButton(window, "ApiKeyModeButton").Style,
                        Is.SameAs(window.TryFindResource("PrimaryButtonStyle")));
        }

        [Test]
        public void ApiKeyMode_WhenClicked_MarksAccountButtonAsInactive()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Click(window, "ApiKeyModeButton");

            Assert.That(GetButton(window, "AccountModeButton").Style,
                        Is.SameAs(window.TryFindResource("SecondaryButtonStyle")));
        }

        [Test]
        public void AccountMode_WhenClickedAfterApiKeyMode_ShowsAccountFormAgain()
        {
            var window = CreateWindow(configProvider: new StubConfigProvider("https://example.com", string.Empty));

            Click(window, "ApiKeyModeButton");
            Click(window, "AccountModeButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetPanel(window, "AccountFormPanel").Visibility, Is.EqualTo(Visibility.Visible),
                            "AccountFormPanel visibility");
                Assert.That(GetPanel(window, "ApiKeyFormPanel").Visibility, Is.EqualTo(Visibility.Collapsed),
                            "ApiKeyFormPanel visibility");
            });
        }

        [Test]
        public void AccountMode_WhenClicked_MarksAccountButtonAsActive()
        {
            var window = CreateWindow();

            Click(window, "AccountModeButton");

            Assert.That(GetButton(window, "AccountModeButton").Style,
                        Is.SameAs(window.TryFindResource("PrimaryButtonStyle")));
        }

        // ── Masked API key (#212) ─────────────────────────────────────────────

        [Test]
        public void Constructor_WithSavedApiKey_PreselectsApiKeyMode()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetPanel(window, "ApiKeyFormPanel").Visibility, Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Constructor_WithSavedApiKey_PopulatesMaskedApiKeyField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetPasswordBox(window, "ApiKeyMaskedBox").Password, Is.EqualTo("saved-key"));
        }

        [Test]
        public void Constructor_WithSavedApiKey_KeepsApiKeyMasked()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetPasswordBox(window, "ApiKeyMaskedBox").Visibility, Is.EqualTo(Visibility.Visible),
                            "ApiKeyMaskedBox visibility");
                Assert.That(GetTextBox(window, "ApiBox")!.Visibility, Is.EqualTo(Visibility.Collapsed),
                            "ApiBox visibility");
            });
        }

        [Test]
        public void ShowApiKey_WhenClicked_RevealsApiKeyInPlainField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Click(window, "ShowApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetTextBox(window, "ApiBox")!.Visibility, Is.EqualTo(Visibility.Visible),
                            "ApiBox visibility");
                Assert.That(GetPasswordBox(window, "ApiKeyMaskedBox").Visibility, Is.EqualTo(Visibility.Collapsed),
                            "ApiKeyMaskedBox visibility");
            });
        }

        [Test]
        public void ShowApiKey_WhenClicked_ShowsTheSavedKeyInThePlainField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Click(window, "ShowApiKeyButton");

            Assert.That(GetTextBox(window, "ApiBox")!.Text, Is.EqualTo("saved-key"));
        }

        [Test]
        public void ShowApiKey_WhenClicked_ShowsHideLabel()
        {
            var window = CreateWindow();

            Click(window, "ShowApiKeyButton");

            Assert.That(GetText(window, "ShowApiKeyButtonText"), Is.EqualTo("Hide"));
        }

        [Test]
        public void ShowApiKey_WhenClickedTwice_MasksTheApiKeyAgain()
        {
            var window = CreateWindow();

            Click(window, "ShowApiKeyButton");
            Click(window, "ShowApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetPasswordBox(window, "ApiKeyMaskedBox").Visibility, Is.EqualTo(Visibility.Visible),
                            "ApiKeyMaskedBox visibility");
                Assert.That(GetTextBox(window, "ApiBox")!.Visibility, Is.EqualTo(Visibility.Collapsed),
                            "ApiBox visibility");
            });
        }

        [Test]
        public void ShowApiKey_WhenClickedTwice_RestoresShowLabel()
        {
            var window = CreateWindow();

            Click(window, "ShowApiKeyButton");
            Click(window, "ShowApiKeyButton");

            Assert.That(GetText(window, "ShowApiKeyButtonText"), Is.EqualTo("Show"));
        }

        [Test]
        public void ShowApiKey_AfterEditingMaskedField_RevealsTheEditedKey()
        {
            var window = CreateWindow();
            GetPasswordBox(window, "ApiKeyMaskedBox").Password = "typed-key";

            Click(window, "ShowApiKeyButton");

            Assert.That(GetTextBox(window, "ApiBox")!.Text, Is.EqualTo("typed-key"));
        }

        [Test]
        public async Task ApplySettingsAsync_InApiKeyMode_SendsTheMaskedKeyToTheService()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            await window.ApplySettingsAsync();

            Assert.That(applyService.LastInput!.RawApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public async Task ApplySettingsAsync_InAccountMode_SendsThePasswordToTheService()
        {
            var applyService = new StubSettingsApplyService();
            var window = CreateWindow(
                applyService: applyService,
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            await window.ApplySettingsAsync();

            Assert.That(applyService.LastInput!.Password, Is.EqualTo("s3cret"));
        }

        [Test]
        public async Task ApplySettingsAsync_InAccountMode_ClearsThePasswordBox()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            GetTextBox(window, "UsernameBox")!.Text = "engineer";
            GetPasswordBox(window, "PasswordBox").Password = "s3cret";

            await window.ApplySettingsAsync();

            Assert.That(GetPasswordBox(window, "PasswordBox").Password, Is.Empty);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ScrollViewer GetCredentialForm(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "CredentialFormScroll") as ScrollViewer;
            Assert.That(element, Is.Not.Null, "Could not find ScrollViewer named 'CredentialFormScroll'.");
            return element!;
        }

        private static bool IsInsideCredentialForm(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as DependencyObject;
            Assert.That(element, Is.Not.Null, $"Could not find element named '{name}'.");

            var form = GetCredentialForm(window);
            for (var parent = LogicalTreeHelper.GetParent(element!); parent != null;
                 parent = LogicalTreeHelper.GetParent(parent))
            {
                if (ReferenceEquals(parent, form)) return true;
            }

            return false;
        }

        private static void Click(Window window, string name) =>
            GetButton(window, name).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        private static string GetText(Window window, string name)
        {
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, name);
            var textBlock = element as TextBlock;
            var textBox = element as TextBox;
            Assert.That(textBlock ?? (object?)textBox, Is.Not.Null, $"Could not find TextBlock or TextBox named '{name}'.");
            return textBlock?.Text ?? textBox?.Text ?? string.Empty;
        }

        private static Panel GetPanel(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as Panel;
            Assert.That(element, Is.Not.Null, $"Could not find Panel named '{name}'.");
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
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, name);
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
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, name);
            return element as TextBox;
        }

        private static Brush GetStripeBrush(Window window)
        {
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, "MappingStatusStripe");
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
