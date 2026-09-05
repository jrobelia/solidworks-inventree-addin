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

        [Test]
        public void SaveThenGet_RoundTripsMappingSourcePath()
        {
            var config = new ServerConfig
            {
                Url               = "http://example.com",
                ApiKey            = "key",
                MappingSourcePath = @"\\server\share\mapping.json",
            };

            _provider.SaveServerConfig(config);
            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.MappingSourcePath, Is.EqualTo(@"\\server\share\mapping.json"));
        }

        [Test]
        public void SaveThenGet_WhenMappingSourcePathIsNull_RoundTripsAsNull()
        {
            var config = new ServerConfig { Url = "http://example.com", ApiKey = "key" };

            _provider.SaveServerConfig(config);
            var result = _provider.GetServerConfig();

            Assert.That(result!.MappingSourcePath, Is.Null);
        }

        [Test]
        public void SaveThenGet_RoundTripsWaitForServerAssignedIpn()
        {
            var config = new ServerConfig
            {
                Url                      = "http://example.com",
                ApiKey                   = "key",
                WaitForServerAssignedIpn = false,
            };

            _provider.SaveServerConfig(config);
            var result = _provider.GetServerConfig();

            Assert.That(result!.WaitForServerAssignedIpn, Is.False);
        }

        [Test]
        public void GetServerConfig_LegacyWaitForServerAssignedIpn_MigratesToNewKey()
        {
            var legacyJson =
                "{\"Url\":\"http://example.com\",\"ApiKey\":\"key\",\"WaitForAutoPartNumber\":false}";
            var plain = System.Text.Encoding.UTF8.GetBytes(legacyJson);
            var cipher = System.Security.Cryptography.ProtectedData.Protect(
                plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"sw-inventree-migration-{Guid.NewGuid()}.dat");
            System.IO.File.WriteAllBytes(path, cipher);

            var provider = new EncryptedConfigProvider(path);
            var result = provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.WaitForServerAssignedIpn, Is.False);

            System.IO.File.Delete(path);
        }
    }
}

