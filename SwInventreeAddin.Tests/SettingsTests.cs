using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    // ── Fixture 1: EncryptedConfigProvider ────────────────────────────────────
    // Uses a temp file so real APPDATA is never touched during tests.
    [TestFixture]
    public class EncryptedConfigProviderTests
    {
        private string _tempFilePath = null!;
        private EncryptedConfigProvider _provider = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"test_settings_{Guid.NewGuid():N}.dat");
            _provider = new EncryptedConfigProvider(_tempFilePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        [Test]
        public void GetServerConfig_WhenFileDoesNotExist_ReturnsNull()
        {
            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SaveThenGet_RoundTripsUrlAndApiKey()
        {
            var config = new ServerConfig { Url = "http://example.com", ApiKey = "my-api-key" };

            _provider.SaveServerConfig(config);
            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Url,    Is.EqualTo("http://example.com"));
            Assert.That(result.ApiKey,  Is.EqualTo("my-api-key"));
        }

        [Test]
        public void GetServerConfig_WhenFileIsCorrupt_ThrowsInvalidOperationException()
        {
            File.WriteAllBytes(_tempFilePath, new byte[] { 0xFF, 0xFE, 0x00, 0x01 });

            Assert.That(() => _provider.GetServerConfig(),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    // ── Fixture 2: TaskPaneControl settings surface ───────────────────────────
    // WinForms requires STA thread; NUnit honours the [Apartment] attribute.
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class TaskPaneSettingsTests
    {
        private StubInventreeClient _client = null!;
        private StubDocumentPropertyService _propertyService = null!;
        private TaskPaneControl _control = null!;

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            _control         = new TaskPaneControl(_client, _propertyService);
        }

        [TearDown]
        public void TearDown()
        {
            _control?.Dispose();
        }

        [Test]
        public void UpdateClient_WithNull_SetsStatusToNotConfiguredMessage()
        {
            _control.UpdateClient(null);

            Assert.That(_control.StatusLabel.Text, Does.Contain("No server configured"));
        }

        [Test]
        public void UpdateClient_WithNull_DisablesFetchButton()
        {
            _control.UpdateClient(null);

            Assert.That(_control.FetchButton.Enabled, Is.False);
        }

        [Test]
        public void UpdateClient_WithValidClient_EnablesFetchButton()
        {
            _control.UpdateClient(null);
            _control.UpdateClient(_client);

            Assert.That(_control.FetchButton.Enabled, Is.True);
        }

        [Test]
        public void SettingsButton_IsAlwaysPresent()
        {
            Assert.That(_control.SettingsButton, Is.Not.Null);
        }

        [Test]
        public void SettingsButton_Click_FiresSettingsRequestedEvent()
        {
            bool eventFired = false;
            _control.SettingsRequested += (s, e) => eventFired = true;

            _control.SettingsButton.PerformClick();

            Assert.That(eventFired, Is.True);
        }
    }
}
