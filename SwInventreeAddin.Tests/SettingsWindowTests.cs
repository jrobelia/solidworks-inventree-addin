using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        public void Constructor_WhenMappingProviderThrowsOnGetMapping_SetsRedMappingStatusAndDoesNotCrash()
        {
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                ThrowOnGet = new InvalidOperationException("Failed to load mapping file: C:\\temp\\missing.json"),
            };

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(GetText(window, "MappingStatusText"), Does.Contain("Failed to load mapping file"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenServiceThrowsConfigError_SetsServerSettingsStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: stub config failure"),
            };

            var window = CreateWindow(applyService: applyService);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.False);
            Assert.That(GetText(window, "StatusText"), Does.Contain("Failed to save server settings"));
            Assert.That(GetText(window, "StatusText"), Does.Contain("stub config failure"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenMappingProviderThrowsOnRefresh_SetsMappingFileStatus()
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
            Assert.That(GetText(window, "StatusText"), Does.Contain("Failed to load mapping file"));
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
            Assert.That(GetText(window, "StatusText"), Does.Contain("Settings applied"));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetText(Window window, string name)
        {
            var element = System.Windows.LogicalTreeHelper.FindLogicalNode(window, name);
            var textBlock = element as TextBlock;
            Assert.That(textBlock, Is.Not.Null, $"Could not find TextBlock named '{name}'.");
            return textBlock!.Text ?? string.Empty;
        }

        private SettingsWindow CreateWindow(
            IPropertyMappingProvider? mappingProvider = null,
            ISettingsApplyService? applyService = null,
            IMappingProviderFactory? mappingProviderFactory = null)
        {
            mappingProvider        ??= new StubPropertyMappingProvider();
            applyService           ??= new StubSettingsApplyService();
            mappingProviderFactory ??= new StubMappingProviderFactory
            {
                Factory = _ => mappingProvider,
            };

            var configProvider = new StubConfigProvider("https://example.com", "stub-key");
            var versionInfo    = new StubVersionInfo();

            return new SettingsWindow(
                configProvider,
                mappingProvider,
                versionInfo,
                applyService,
                mappingProviderFactory);
        }
    }
}
