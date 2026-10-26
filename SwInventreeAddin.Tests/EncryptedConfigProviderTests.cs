using System;
using System.IO;
using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    // Uses a temp file so real APPDATA is never touched during tests. The
    // IConfigProvider contract runs here and against StubConfigProvider —
    // one suite pins the semantics every adapter must mirror, so only
    // file-backed specifics stay below: corrupt and locked files, legacy key
    // migration, and physical file removal on delete.
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

        // ── IConfigProvider contract ──────────────────────────────────────

        [Test]
        public void Get_WhenNothingSaved_ReturnsNull() =>
            ConfigProviderContract.Get_WhenNothingSaved_ReturnsNull(_provider);

        [Test]
        public void SaveThenGet_RoundTripsSavedValues() =>
            ConfigProviderContract.SaveThenGet_RoundTripsSavedValues(_provider);

        [Test]
        public void Save_Overwrite_LastWriteWins() =>
            ConfigProviderContract.Save_Overwrite_LastWriteWins(_provider);

        [Test]
        public void Delete_AfterSave_GetReturnsNull() =>
            ConfigProviderContract.Delete_AfterSave_GetReturnsNull(_provider);

        [Test]
        public void Delete_WhenNothingSaved_IsANoOp() =>
            ConfigProviderContract.Delete_WhenNothingSaved_IsANoOp(_provider);

        // ── File-backed specifics ─────────────────────────────────────────

        [Test]
        public void GetServerConfig_WhenFileIsCorrupt_ReturnsNull()
        {
            // A corrupt or non-portable settings.dat must read as "not
            // configured" — never block add-in load with a thrown decrypt error.
            File.WriteAllBytes(_tempFilePath, new byte[] { 0xFF, 0xFE, 0x00, 0x01 });

            Assert.That(_provider.GetServerConfig(), Is.Null);
        }

        [Test]
        public void GetServerConfig_WhenPayloadDecryptsButIsNotJson_ReturnsNull()
        {
            var cipher = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes("this is not json"),
                null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_tempFilePath, cipher);

            Assert.That(_provider.GetServerConfig(), Is.Null);
        }

        [Test]
        public void GetServerConfig_StoredUrlNotHttps_ReadsAsUnconfigured()
        {
            // Defence in depth: the save path already refuses non-https URLs;
            // a stored value that fails the same check must not reach client
            // construction after a reload.
            _provider.SaveServerConfig(new ServerConfig
            {
                Url = "http://inventree.example.com",
                ApiKey = "key",
            });

            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.IsConfigured, Is.False);
        }

        [Test]
        public void GetServerConfig_StoredUrlNotParseable_ReadsAsUnconfigured()
        {
            _provider.SaveServerConfig(new ServerConfig
            {
                Url = "not a url",
                ApiKey = "key",
            });

            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.IsConfigured, Is.False);
        }

        [Test]
        public void GetServerConfig_StoredUrlHttps_RoundTrips()
        {
            _provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://inventree.example.com",
                ApiKey = "key",
            });

            var result = _provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Url, Is.EqualTo("https://inventree.example.com"));
            Assert.That(result.IsConfigured, Is.True);
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
        public void DeleteServerConfig_WhenFileExists_RemovesTheSettingsFile()
        {
            _provider.SaveServerConfig(new ServerConfig { Url = "http://example.com", ApiKey = "key" });
            Assert.That(File.Exists(_tempFilePath), Is.True);

            _provider.DeleteServerConfig();

            Assert.That(File.Exists(_tempFilePath), Is.False);
        }

        [Test]
        public void DeleteServerConfig_WhenFileIsLocked_ThrowsInvalidOperationException()
        {
            _provider.SaveServerConfig(new ServerConfig { Url = "http://example.com", ApiKey = "key" });

            using var stream = new FileStream(_tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.That(() => _provider.DeleteServerConfig(),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("delete"));
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

