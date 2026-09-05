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
            var applyService    = new StubSettingsApplyService();
            var mappingProvider = new StubPropertyMappingProvider();
            var window          = CreateWindow(applyService: applyService, mappingProvider: mappingProvider);

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

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetText(Window window, string name)
        {
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, name);
            var textBlock = element as TextBlock;
            var textBox   = element as TextBox;
            Assert.That(textBlock ?? (object?)textBox, Is.Not.Null, $"Could not find TextBlock or TextBox named '{name}'.");
            return textBlock?.Text ?? textBox?.Text ?? string.Empty;
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
            mappingProvider        ??= new StubPropertyMappingProvider();
            applyService           ??= new StubSettingsApplyService();
            mappingProviderFactory ??= new StubMappingProviderFactory
            {
                Factory = _ => mappingProvider,
            };
            configProvider         ??= new StubConfigProvider("https://example.com", "stub-key");

            var versionInfo    = new StubVersionInfo();

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
