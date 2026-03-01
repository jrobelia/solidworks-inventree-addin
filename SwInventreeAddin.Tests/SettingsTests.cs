using System;
using System.IO;
using NUnit.Framework;
using SwInventreeAddin.Config;

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
}

