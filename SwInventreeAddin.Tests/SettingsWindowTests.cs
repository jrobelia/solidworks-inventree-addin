using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
            var mappingProvider = new ThrowingPropertyMappingProvider(_localMappingPath,
                new InvalidOperationException("Failed to load mapping file: C:\\temp\\missing.json"));

            var window = CreateWindow(mappingProvider: mappingProvider);

            Assert.That(window.MappingStatusMessage, Does.Contain("Failed to load mapping file"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenServiceThrowsConfigError_SetsServerSettingsStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(
                    "Failed to save server settings: stub config failure",
                    SettingsApplyErrorKind.Config),
            };

            var window = CreateWindow(applyService: applyService);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.False);
            Assert.That(window.StatusMessage, Does.Contain("Failed to save server settings"));
            Assert.That(window.StatusMessage, Does.Contain("stub config failure"));
        }

        [Test]
        public async Task ApplySettingsAsync_WhenMappingProviderThrowsOnRefresh_SetsMappingFileStatus()
        {
            var applyService = new StubSettingsApplyService();
            var throwingProvider = new ThrowingPropertyMappingProvider(_localMappingPath,
                new InvalidOperationException("Failed to load mapping file: C:\\temp\\bad.json"));

            var window = CreateWindow(
                applyService: applyService,
                mappingProvider: new StubPropertyMappingProvider(),
                mappingProviderFactory: _ => throwingProvider);

            bool result = await window.ApplySettingsAsync();

            Assert.That(result, Is.False);
            Assert.That(window.StatusMessage, Does.Contain("Failed to load mapping file"));
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
            Assert.That(window.StatusMessage, Does.Contain("Settings applied"));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private SettingsWindow CreateWindow(
            IPropertyMappingProvider? mappingProvider = null,
            ISettingsApplyService? applyService = null,
            Func<string?, IPropertyMappingProvider>? mappingProviderFactory = null)
        {
            mappingProvider        ??= new StubPropertyMappingProvider();
            applyService           ??= new StubSettingsApplyService();
            mappingProviderFactory ??= _ => mappingProvider;

            var configProvider = new StubConfigProvider("https://example.com", "stub-key");
            var versionInfo    = new StubVersionInfo();

            return new SettingsWindow(
                configProvider,
                mappingProvider,
                versionInfo,
                applyService,
                mappingProviderFactory);
        }

        private class ThrowingPropertyMappingProvider : IPropertyMappingProvider
        {
            private readonly string _localFilePath;
            private readonly Exception _exception;

            public ThrowingPropertyMappingProvider(string localFilePath, Exception exception)
            {
                _localFilePath = localFilePath;
                _exception     = exception;
            }

            public bool IsReadOnly => false;
            public string LocalFilePath => _localFilePath;

            public PropertyMappingConfig GetMapping() => throw _exception;
            public void SaveMapping(PropertyMappingConfig config) => throw _exception;
            public void CopyToLocal() => throw _exception;
        }
    }
}
