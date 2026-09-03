using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;

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

        // ── GetMappingResult / configuration access ───────────────────────────

        [Test]
        public void GetMappingResult_NoLocalNoSource_ReturnsConfigAndWritesDefaultsToLocalPath()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            var result = provider.GetMappingResult();

            Assert.That(result.Config.IpnProperty,      Is.EqualTo("PartNo"));
            Assert.That(result.Config.NameProperty,      Is.EqualTo("Description"));
            Assert.That(result.Config.NotesProperty,     Is.EqualTo("Notes"));
            Assert.That(result.Config.RevisionProperty,  Is.EqualTo("Revision"));
            Assert.That(File.Exists(_localPath),  Is.True,
                "First-run should have written defaults to local path.");

            // Verify the written file round-trips: second call fetches from local file.
            var fetched = provider.GetMappingResult().Config;
            Assert.That(fetched.IpnProperty, Is.EqualTo("PartNo"),
                "Second call should fetch the written defaults from the local file.");
        }

        [Test]
        public void GetMappingResult_LocalExists_ReturnsConfigFromLocal()
        {
            WriteJson(_localPath, new PropertyMappingConfig { IpnProperty = "MyIPN" });

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Config.IpnProperty, Is.EqualTo("MyIPN"));
        }

        [Test]
        public void GetMappingResult_SourceConfiguredNoLocal_ReturnsConfigFromSource()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            Assert.That(result.Config.IpnProperty, Is.EqualTo("SourceIPN"));
        }

        [Test]
        public void GetMappingResult_SourceConfiguredAndLocalExists_SourceTakesPrecedence()
        {
            WriteJson(_localPath,  new PropertyMappingConfig { IpnProperty = "LocalIPN"  });
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            // Source path takes priority when configured — local is only used when no source is set.
            Assert.That(result.Config.IpnProperty, Is.EqualTo("SourceIPN"));
        }

        [Test]
        public void GetMappingResult_SourceConfiguredButMissing_LocalExists_ReturnsInvalid()
        {
            WriteJson(_localPath, new PropertyMappingConfig { IpnProperty = "LocalIPN" });
            // _sourcePath is intentionally not written.

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            AssertMissingSourceInvalid(result);
        }

        [Test]
        public void GetMappingResult_SourceConfiguredButMissing_NoLocal_ReturnsInvalid()
        {
            // Both _sourcePath and _localPath intentionally not written.

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            AssertMissingSourceInvalid(result);
            Assert.That(File.Exists(_localPath), Is.False,
                "When a configured source path is missing, do not silently fall back to first-run defaults.");
        }

        // ── ResolvedFilePath ──────────────────────────────────────────────────

        [Test]
        public void GetMappingResult_SourceConfiguredAndExists_ResolvedFilePathIsSource()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            Assert.That(result.ResolvedFilePath, Is.EqualTo(_sourcePath));
        }

        [Test]
        public void GetMappingResult_NoSourceConfigured_ResolvedFilePathIsLocal()
        {
            WriteJson(_localPath, new PropertyMappingConfig());

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.ResolvedFilePath, Is.EqualTo(_localPath));
        }

        [Test]
        public void GetMappingResult_FirstRun_ResolvedFilePathIsLocal()
        {
            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.ResolvedFilePath, Is.EqualTo(_localPath));
        }

        [Test]
        public void GetMappingResult_SourceMissing_ResolvedFilePathFallsBackToLocal()
        {
            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            Assert.That(result.ResolvedFilePath, Is.EqualTo(_localPath));
        }

        // ── Error handling ────────────────────────────────────────────────────

        [Test]
        public void SaveMapping_WhenFileIsReadOnly_ThrowsActionableInvalidOperationException()
        {
            var provider = new PropertyMappingProvider(_localPath, null);
            provider.SaveMapping(new PropertyMappingConfig());
            File.SetAttributes(_localPath, FileAttributes.ReadOnly);

            try
            {
                Assert.That(() => provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "Changed" }),
                    Throws.TypeOf<InvalidOperationException>().With.Message.Contains(_localPath)
                                                         .And.Message.Contains("read-only").IgnoreCase);
            }
            finally
            {
                File.SetAttributes(_localPath, FileAttributes.Normal);
            }
        }

        [Test]
        public void SaveMapping_WhenPathIsAFileUsedAsDirectory_ThrowsInvalidOperationExceptionWithPath()
        {
            // Create a file whose name will be treated as a directory by SaveMapping.
            var badPath = Path.Combine(_localPath, "mapping.json");
            File.WriteAllText(_localPath, "blocking the directory");

            Assert.That(() => new PropertyMappingProvider(badPath, null).SaveMapping(new PropertyMappingConfig()),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains(badPath));
        }

        // ── SaveMapping ───────────────────────────────────────────────────────

        [Test]
        public void SaveMapping_WritesJsonToLocalPath()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "XPN" });
            var fetched = provider.GetMappingResult().Config;

            Assert.That(File.Exists(_localPath),  Is.True);
            Assert.That(fetched.IpnProperty,      Is.EqualTo("XPN"));
        }

        [Test]
        public void SaveMapping_SparseConfig_DoesNotInjectDefaults()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "XPN" });
            var json = File.ReadAllText(_localPath);

            Assert.That(json, Does.Contain("\"IpnProperty\""));
            Assert.That(json, Does.Not.Contain("\"SchemaVersion\""));
            Assert.That(json, Does.Not.Contain("\"NameProperty\""));
            Assert.That(json, Does.Not.Contain("\"BomColumnQty\""));
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

        // ── SaveMapping target ────────────────────────────────────────────────

        [Test]
        public void SaveMapping_WritesToSourceWhenSourceFileExists()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });
            WriteJson(_localPath,  new PropertyMappingConfig { IpnProperty = "LocalIPN" });

            var provider = new PropertyMappingProvider(_localPath, _sourcePath);
            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "SavedToSource" });

            var sourceJson = File.ReadAllText(_sourcePath);
            Assert.That(sourceJson, Does.Contain("SavedToSource"));

            var localJson = File.ReadAllText(_localPath);
            Assert.That(localJson, Does.Contain("LocalIPN"));
        }

        [Test]
        public void SaveMapping_WritesToLocalWhenNoSourceIsConfigured()
        {
            var provider = new PropertyMappingProvider(_localPath, null);
            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "SavedToLocal" });

            var localJson = File.ReadAllText(_localPath);
            Assert.That(localJson, Does.Contain("SavedToLocal"));
        }

        // ── BOM column migration ──────────────────────────────────────────────

        [Test]
        public void GetMappingResult_SchemaV1File_DoesNotBackfillBomColumnDefaults()
        {
            var v1Json = @"{
                ""SchemaVersion"": ""1"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision""
            }";
            File.WriteAllText(_localPath, v1Json);
            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();
            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
            Assert.That(result.Config.BomColumnIpn,       Is.Null);
            Assert.That(result.Config.BomColumnQty,       Is.Null);
            Assert.That(result.Config.BomColumnReference, Is.Null);
            Assert.That(result.Config.BomColumnNote,      Is.Null);
        }

        [Test]
        public void GetMappingResult_SchemaV2File_DoesNotBackfillBomColumnDefaults()
        {
            var v2Json = @"{
                ""SchemaVersion"": ""2"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description Long"",
                ""PkProperty"": ""InvenTree PK""
            }";
            File.WriteAllText(_localPath, v2Json);
            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();
            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
            Assert.That(result.Config.BomColumnIpn, Is.Null);
            Assert.That(result.Config.BomColumnQty, Is.Null);
        }

        [Test]
        public void PropertyMappingConfig_BomColumnAliasCsv_IsSplitCaseInsensitively()
        {
            var config = PropertyMappingConfig.WithDefaults();
            var aliases = config.BomColumnQty!
                .Split(',')
                .Select(s => s.Trim())
                .ToList();
            Assert.That(aliases, Does.Contain("Qty"));
            Assert.That(aliases, Does.Contain("Quantity"));
        }

        // ── GetMappingResult / MappingHealth ──────────────────────────────────

        [Test]
        public void GetMappingResult_NoLocalNoSource_WritesDefaultsAndReturnsHealthy()
        {
            var provider = new PropertyMappingProvider(_localPath, null);

            var result = provider.GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Healthy));
            Assert.That(result.CanUseForPartSync, Is.True);
            Assert.That(result.CanFetch, Is.True);
            Assert.That(result.CanEdit, Is.True);
            Assert.That(File.Exists(_localPath), Is.True,
                "First-run should have written defaults to local path.");
            Assert.That(result.Config.SchemaVersion, Is.EqualTo(PropertyMappingConfig.CurrentSchemaVersion));
        }

        [Test]
        public void GetMappingResult_LocalCurrentSchema_ReturnsHealthy()
        {
            WriteJson(_localPath, new PropertyMappingConfig { IpnProperty = "PartNo" });

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Healthy));
            Assert.That(result.Config.IpnProperty, Is.EqualTo("PartNo"));
            Assert.That(result.CanUseForPartSync, Is.True);
            Assert.That(result.CanEdit, Is.True);
        }

        [Test]
        public void GetMappingResult_LocalSchemaV1_ReturnsNeedsUpgradeAndKeepsOriginalSchema()
        {
            var v1Json = @"{
                ""SchemaVersion"": ""1"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision""
            }";
            File.WriteAllText(_localPath, v1Json);

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
            Assert.That(result.Config.SchemaVersion, Is.EqualTo("1"),
                "The runtime config should keep the file's original schema version, not silently upgrade it.");
            Assert.That(result.CanFetch, Is.False);
            Assert.That(result.CanUseForPartSync, Is.False);
            Assert.That(result.CanEdit, Is.True);
        }

        [Test]
        public void GetMappingResult_LocalSchemaV2_ReturnsNeedsUpgrade()
        {
            var v2Json = @"{
                ""SchemaVersion"": ""2"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description Long"",
                ""PkProperty"": ""InvenTree PK""
            }";
            File.WriteAllText(_localPath, v2Json);

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
            Assert.That(result.Config.SchemaVersion, Is.EqualTo("2"));
            Assert.That(result.Message, Does.Contain("Property Mapping Schema is out of date"));
        }

        [Test]
        public void GetMappingResult_NewerSchemaVersion_ReturnsNewerSchema()
        {
            var newerJson = @"{
                ""SchemaVersion"": ""4"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description Long"",
                ""PkProperty"": ""InvenTree PK""
            }";
            File.WriteAllText(_localPath, newerJson);

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.NewerSchema));
            Assert.That(result.Config.SchemaVersion, Is.EqualTo("4"),
                "The runtime config should keep the file's original schema version.");
        }

        [Test]
        public void GetMappingResult_NewerSchemaVersion_ExposesUpgradeMessage()
        {
            var newerJson = @"{
                ""SchemaVersion"": ""4"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description Long"",
                ""PkProperty"": ""InvenTree PK""
            }";
            File.WriteAllText(_localPath, newerJson);

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Message, Does.Contain("newer").IgnoreCase);
            Assert.That(result.Message, Does.Contain("add-in").IgnoreCase);
        }

        [Test]
        public void GetMappingResult_InvalidJson_ReturnsInvalidWithPath()
        {
            File.WriteAllText(_localPath, "not valid json");

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain("Failed to fetch the Property Mapping file"));
            Assert.That(result.Message, Does.Contain(_localPath));
            Assert.That(result.CanFetch, Is.False);
            Assert.That(result.CanUseForPartSync, Is.False);
            Assert.That(result.CanEdit, Is.False);
        }

        [Test]
        public void GetMappingResult_LockedFile_ReturnsInvalidWithPath()
        {
            using var stream = new FileStream(_localPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var content = System.Text.Encoding.UTF8.GetBytes("{}");
            stream.Write(content, 0, content.Length);
            stream.Flush();

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain(_localPath));
        }

        [Test]
        public void GetMappingResult_MissingSchemaVersion_TreatedAsNeedsUpgrade()
        {
            var v1Json = @"{
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision""
            }";
            File.WriteAllText(_localPath, v1Json);

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
        }

        [Test]
        public void GetMappingResult_UnparseableSchemaVersion_ReturnsInvalid()
        {
            File.WriteAllText(_localPath, @"{
                ""SchemaVersion"": ""foo"",
                ""IpnProperty"": ""PartNo""
            }");

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain("Unrecognized Property Mapping Schema version").IgnoreCase);
        }

        [Test]
        public void GetMappingResult_DuplicateDocumentPropertyNames_ReturnsInvalid()
        {
            File.WriteAllText(_localPath, @"{
                ""SchemaVersion"": ""3"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""Description"",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description"",
                ""PkProperty"": ""InvenTree PK""
            }");

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain("Description"));
            Assert.That(result.Message, Does.Contain("duplicate").IgnoreCase);
        }

        [Test]
        public void GetMappingResult_DuplicateIsCaseInsensitiveAndTrims_ReturnsInvalid()
        {
            File.WriteAllText(_localPath, @"{
                ""SchemaVersion"": ""3"",
                ""IpnProperty"": ""PartNo"",
                ""NameProperty"": ""  partno  "",
                ""NotesProperty"": ""Notes"",
                ""RevisionProperty"": ""Revision"",
                ""DescriptionProperty"": ""Description Long"",
                ""PkProperty"": ""InvenTree PK""
            }");

            var result = new PropertyMappingProvider(_localPath, null).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain("PartNo"));
        }

        [Test]
        public void GetMappingResult_SourceTakesPrecedenceForHealth_ReturnsNeedsUpgrade()
        {
            WriteJson(_localPath,  new PropertyMappingConfig { SchemaVersion = "3", IpnProperty = "LocalIPN"  });
            WriteJson(_sourcePath, new PropertyMappingConfig { SchemaVersion = "1", IpnProperty = "SourceIPN" });

            var result = new PropertyMappingProvider(_localPath, _sourcePath).GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.NeedsUpgrade));
            Assert.That(result.Config.IpnProperty, Is.EqualTo("SourceIPN"));
            Assert.That(result.Config.SchemaVersion, Is.EqualTo("1"));
        }

        [Test]
        public void SaveMapping_PreservesUnknownTopLevelKeys()
        {
            File.WriteAllText(_localPath, @"{
                ""SchemaVersion"": ""3"",
                ""IpnProperty"": ""PartNo"",
                ""UnknownFutureKey"": ""future-value"",
                ""AnotherUnknown"": 42
            }");

            var provider = new PropertyMappingProvider(_localPath, null);
            var result   = provider.GetMappingResult();

            Assert.That(result.Health, Is.EqualTo(MappingHealth.Healthy));
            Assert.That(result.Config.ExtensionData.ContainsKey("UnknownFutureKey"), Is.True);
            Assert.That(result.Config.ExtensionData.ContainsKey("AnotherUnknown"), Is.True);

            result.Config.IpnProperty = "NewPartNo";
            provider.SaveMapping(result.Config);

            var savedJson = File.ReadAllText(_localPath);
            Assert.That(savedJson, Does.Contain("UnknownFutureKey"));
            Assert.That(savedJson, Does.Contain("future-value"));
            Assert.That(savedJson, Does.Contain("AnotherUnknown"));
            Assert.That(savedJson, Does.Contain("NewPartNo"));
        }

        [Test]
        public void SaveMapping_RaisesMappingChanged()
        {
            var provider    = new PropertyMappingProvider(_localPath, null);
            var raised      = false;
            provider.MappingChanged += (s, e) => raised = true;

            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "PartNo" });

            Assert.That(raised, Is.True);
        }

        [Test]
        public void SaveMapping_WritesToSource_RaisesMappingChanged()
        {
            WriteJson(_sourcePath, new PropertyMappingConfig { IpnProperty = "SourceIPN" });
            var provider = new PropertyMappingProvider(_localPath, _sourcePath);

            var raised = false;
            provider.MappingChanged += (s, e) => raised = true;

            provider.SaveMapping(new PropertyMappingConfig { IpnProperty = "Updated" });

            Assert.That(raised, Is.True);
            Assert.That(File.ReadAllText(_sourcePath), Does.Contain("Updated"));
        }

        // ── Contract ──────────────────────────────────────────────────────────

        [Test]
        public void RemovedMembers_AreNotDeclaredOnPublicSeam()
        {
            var forbidden = new[] { "GetMapping", "IsReadOnly", "CopyToLocal" };
            var declared =
                new[] { typeof(IPropertyMappingProvider), typeof(PropertyMappingProvider), typeof(StubPropertyMappingProvider) }
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => forbidden.Contains(m.Name) && m.DeclaringType != typeof(object))
                .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
                .ToList();

            var declaredGetters =
                new[] { typeof(IPropertyMappingProvider), typeof(PropertyMappingProvider), typeof(StubPropertyMappingProvider) }
                .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(p => p.Name == "IsReadOnly" && p.DeclaringType != typeof(object))
                .Select(p => $"{p.DeclaringType?.Name}.{p.Name}")
                .ToList();

            Assert.That(declared, Is.Empty,
                "GetMapping(), IsReadOnly and CopyToLocal() must be removed from the public seam.");
            Assert.That(declaredGetters, Is.Empty,
                "IsReadOnly must be removed from the public seam.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AssertMissingSourceInvalid(MappingResult result)
        {
            Assert.That(result.Health, Is.EqualTo(MappingHealth.Invalid));
            Assert.That(result.Message, Does.Contain(_sourcePath),
                "The user must be told which configured source file is missing.");
        }

        private static void WriteJson(string path, PropertyMappingConfig config)
        {
            var merged = PropertyMappingConfig.WithDefaults(config);
            var json = JsonSerializer.Serialize(merged, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
    }
}
