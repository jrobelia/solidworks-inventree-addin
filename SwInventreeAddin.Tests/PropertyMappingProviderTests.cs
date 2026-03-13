using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class PropertyMappingProviderTests
    {
        private string _localPath  = null!;
        private string _sourcePath = null!;

        [SetUp]
        public void SetUp()
        {
            var tmp    = Path.GetTempPath();
            _localPath  = Path.Combine(tmp, $"mapping_local_{Guid.NewGuid():N}.json");
            _sourcePath = Path.Combine(tmp, $"mapping_source_{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_localPath))  File.Delete(_localPath);
            if (File.Exists(_sourcePath)) File.Delete(_sourcePath);
        }

        // ── GetMapping ────────────────────────────────────────────────────────

        [Test]
        public void GetMapping_NoLocalNoSource_WritesDefaultsToLocalPathAndReturns()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            var config = provider.GetMapping();

            Assert.That(config.IpnProperty,      Is.EqualTo("PartNo"));
            Assert.That(config.NameProperty,      Is.EqualTo("Description"));
            Assert.That(config.NotesProperty,     Is.EqualTo("Notes"));
            Assert.That(config.RevisionProperty,  Is.EqualTo("Revision"));
            Assert.That(File.Exists(_localPath),  Is.True,
                "First-run should have written defaults to local path.");

            // Verify the written file round-trips: second call loads from local file.
            var loaded = provider.GetMapping();
            Assert.That(loaded.IpnProperty, Is.EqualTo("PartNo"),
                "Second call should load the written defaults from the local file.");
        }

        [Test]
        public void GetMapping_LocalExists_LoadsFromLocal()
        {
            WriteJson(_localPath, new PropertyMappingConfig { IpnProperty = "MyIPN" });

            var config = new PropertyMappingProvider(_localPath, null).GetMapping();

            Assert.That(config.IpnProperty, Is.EqualTo("MyIPN"));
        }

        [Test]
        public void GetMapping_SourceConfiguredNoLocal_LoadsFromSource()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });

            var config = new PropertyMappingProvider(_localPath, _sourcePath).GetMapping();

            Assert.That(config.IpnProperty, Is.EqualTo("SourceIPN"));
        }

        [Test]
        public void GetMapping_SourceConfiguredAndLocalExists_SourceTakesPrecedence()
        {
            WriteJson(_localPath,  new PropertyMappingConfig { IpnProperty = "LocalIPN"  });
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });

            var config = new PropertyMappingProvider(_localPath, _sourcePath).GetMapping();

            // Source path takes priority when configured — local is only used when no source is set.
            Assert.That(config.IpnProperty, Is.EqualTo("SourceIPN"));
        }

        // ── IsReadOnly ────────────────────────────────────────────────────────

        [Test]
        public void IsReadOnly_SourceConfiguredNoLocal_ReturnsTrue()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig());

            var provider = new PropertyMappingProvider(_localPath, _sourcePath);

            Assert.That(provider.IsReadOnly, Is.True);
        }

        [Test]
        public void IsReadOnly_SourceConfiguredAndLocalExists_ReturnsTrue()
        {
            WriteJson(_localPath,  new PropertyMappingConfig());
            WriteJson(_sourcePath, new PropertyMappingConfig());

            var provider = new PropertyMappingProvider(_localPath, _sourcePath);

            // Source configured → always read-only regardless of whether a local file also exists.
            Assert.That(provider.IsReadOnly, Is.True);
        }

        [Test]
        public void IsReadOnly_NoSourceConfigured_ReturnsFalse()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            Assert.That(provider.IsReadOnly, Is.False);
        }

        // ── SaveMapping ───────────────────────────────────────────────────────

        [Test]
        public void SaveMapping_WritesJsonToLocalPath()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "XPN" });
            var loaded = provider.GetMapping();

            Assert.That(File.Exists(_localPath),  Is.True);
            Assert.That(loaded.IpnProperty,       Is.EqualTo("XPN"));
        }

        [Test]
        public void SaveMapping_CreatesDirectoryIfMissing()
        {
            var deepPath = Path.Combine(Path.GetTempPath(),
                $"sp_test_{Guid.NewGuid():N}", "sub", "mapping.json");
            try
            {
                var provider = new PropertyMappingProvider(deepPath, null);
                provider.SaveMapping(new PropertyMappingConfig());

                Assert.That(File.Exists(deepPath), Is.True);
            }
            finally
            {
                var dir = Path.GetDirectoryName(Path.GetDirectoryName(deepPath));
                if (dir != null && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
        }

        // ── LocalFilePath ─────────────────────────────────────────────────────

        [Test]
        public void LocalFilePath_ReturnsPathPassedToConstructor()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            Assert.That(provider.LocalFilePath, Is.EqualTo(_localPath));
        }

        // ── CopyToLocal ───────────────────────────────────────────────────────

        [Test]
        public void CopyToLocal_CopiesSourceContentToLocalPath()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });
            var provider = new PropertyMappingProvider(_localPath, _sourcePath);

            provider.CopyToLocal();

            // Local file is created with the source content.
            Assert.That(File.Exists(_localPath), Is.True);

            // Source path is still configured, so IsReadOnly stays true and GetMapping still
            // returns source content.  To switch to editable mode the caller must clear the
            // MappingSourcePath in settings (select the Local radio button and save).
            Assert.That(provider.IsReadOnly, Is.True);
            Assert.That(provider.GetMapping().IpnProperty, Is.EqualTo("SourceIPN"));
        }

        [Test]
        public void CopyToLocal_NoSourceConfigured_ThrowsInvalidOperationException()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            Assert.That(() => provider.CopyToLocal(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CopyToLocal_SourceFileDoesNotExist_ThrowsInvalidOperationException()
        {
            // _sourcePath intentionally not written
            var provider = new PropertyMappingProvider(_localPath, _sourcePath);

            Assert.That(() => provider.CopyToLocal(),
                Throws.TypeOf<InvalidOperationException>());
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WriteJson(string path, PropertyMappingConfig config)
        {
            File.WriteAllText(path,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }),
                System.Text.Encoding.UTF8);
        }
    }
}
