using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class MappingEditorViewModelTests
    {
        private StubPropertyMappingProvider _provider = null!;

        [SetUp]
        public void SetUp()
            => _provider = new StubPropertyMappingProvider { Config = PropertyMappingConfig.WithDefaults() };

        private MappingEditorViewModel CreateVm(StubPropertyMappingProvider? provider = null)
            => new MappingEditorViewModel(provider ?? _provider);

        // ── Constructor and placeholders ───────────────────────────────────────

        [Test]
        public void Constructor_LoadsCurrentMappingValues()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty   = "MyIPN",
                    NameProperty  = "MyName",
                    BomColumnIpn  = "IPN, PartNo",
                }
            };

            var vm = CreateVm(provider);

            Assert.That(vm.IpnProperty,       Is.EqualTo("MyIPN"));
            Assert.That(vm.NameProperty,      Is.EqualTo("MyName"));
            Assert.That(vm.BomColumnIpn,      Is.EqualTo("IPN, PartNo"));
            Assert.That(vm.BomColumnQty,      Is.EqualTo(string.Empty));
            Assert.That(vm.BomColumnIpnPlaceholder, Is.EqualTo("IPN, Part IPN, Internal Part Number, Part Number"));
        }

        [Test]
        public void Constructor_OlderSchema_MissingBomFieldsAreBlankWithDefaultPlaceholders()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = "2",
                    IpnProperty   = "PartNo",
                    NameProperty  = "Description",
                    NotesProperty = "Notes",
                    RevisionProperty = "Revision",
                    DescriptionProperty = "Description Long",
                    PkProperty    = "InvenTree PK",
                }
            };

            var vm = CreateVm(provider);

            Assert.That(vm.BomColumnIpn,        Is.EqualTo(string.Empty));
            Assert.That(vm.BomColumnQty,        Is.EqualTo(string.Empty));
            Assert.That(vm.BomColumnReference,  Is.EqualTo(string.Empty));
            Assert.That(vm.BomColumnNote,       Is.EqualTo(string.Empty));

            Assert.That(vm.BomColumnIpnPlaceholder,       Is.EqualTo("IPN, Part IPN, Internal Part Number, Part Number"));
            Assert.That(vm.BomColumnQtyPlaceholder,       Is.EqualTo("Qty, Quantity"));
            Assert.That(vm.BomColumnReferencePlaceholder, Is.EqualTo("Reference"));
            Assert.That(vm.BomColumnNotePlaceholder,      Is.EqualTo("Note, Notes"));
        }

        // ── Draft and revert ───────────────────────────────────────────────────

        [Test]
        public void Cancel_AfterChangingIpn_RevertsToOriginal()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "OldIPN",
                }
            };

            var vm = CreateVm(provider);
            vm.IpnProperty = "NewIPN";

            vm.Cancel();

            Assert.That(vm.IpnProperty, Is.EqualTo("OldIPN"));
            Assert.That(vm.ErrorMessage, Is.Null);
        }

        [Test]
        public void Save_ValidChanges_PersistsToProvider()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "OldIPN",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var vm = CreateVm(provider);
            vm.IpnProperty = "NewIPN";

            var saved = vm.Save();

            Assert.That(saved, Is.True);
            Assert.That(provider.LastSaved, Is.Not.Null);
            Assert.That(provider.LastSaved!.IpnProperty, Is.EqualTo("NewIPN"));
            Assert.That(provider.LastSaved!.SchemaVersion, Is.EqualTo(PropertyMappingConfig.CurrentSchemaVersion));
        }

        [Test]
        public void Save_WhenProviderThrows_RevertsAndReturnsFalse()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "OldIPN",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                },
                ThrowOnSave = new InvalidOperationException("Cannot write file.")
            };

            var vm = CreateVm(provider);
            vm.IpnProperty = "NewIPN";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Is.EqualTo("Cannot write file."));
            Assert.That(vm.IpnProperty, Is.EqualTo("OldIPN"));
        }

        [Test]
        public void Save_ValidationFailure_RevertsAndSetsErrorMessage()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "OldIPN",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var vm = CreateVm(provider);
            vm.IpnProperty = "NewIPN";
            vm.BomColumnIpn = "";   // blank alias

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Contains("BOM Column Alias for IPN"));
            Assert.That(vm.IpnProperty, Is.EqualTo("OldIPN"));
        }

        // ── Duplicate property name validation ─────────────────────────────────

        [Test]
        public void Save_DuplicateSolidWorksPropertyNames_FailsAndReverts()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "PartNo",
                    NameProperty = "Description",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var vm = CreateVm(provider);
            vm.NameProperty = "PartNo";   // duplicate with IPN

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Contains("Duplicate").IgnoreCase);
            Assert.That(vm.NameProperty, Is.EqualTo("Description"));
        }

        // ── BOM alias validation ───────────────────────────────────────────────

        [TestCase("")]
        [TestCase("   ")]
        public void Save_BomAliasBlank_Fails(string alias)
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = alias;

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("BOM Column Alias for IPN").And.Contain("blank").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasLeadingComma_Fails()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = ",IPN";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("BOM Column Alias for IPN").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasTrailingComma_Fails()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = "IPN,";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("BOM Column Alias for IPN").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasBlankBetweenCommas_Fails()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = "IPN,,PartNo";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("blank entry").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasDuplicateWithinColumn_Fails()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = "IPN, ipn";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("duplicate alias").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasSharedAcrossColumns_Fails()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn  = "IPN";
            vm.BomColumnQty  = "IPN";

            var saved = vm.Save();

            Assert.That(saved, Is.False);
            Assert.That(vm.ErrorMessage, Does.Contain("used for more than one field").IgnoreCase);
        }

        [Test]
        public void Save_BomAliasWithSpacesAfterComma_Passes()
        {
            var provider = ValidProvider();
            var vm = CreateVm(provider);
            vm.BomColumnIpn = "IPN, Part IPN";

            var saved = vm.Save();

            Assert.That(saved, Is.True);
            Assert.That(provider.LastSaved, Is.Not.Null);
            Assert.That(provider.LastSaved!.BomColumnIpn, Is.EqualTo("IPN, Part IPN"));
        }

        // ── Unknown top-level key round-trip ───────────────────────────────────

        [Test]
        public void Save_PreservesUnknownTopLevelJsonKeys()
        {
            var futureDoc = JsonDocument.Parse("\"future-value\"");
            var intDoc    = JsonDocument.Parse("42");

            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion       = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty         = "PartNo",
                    BomColumnIpn        = "IPN",
                    BomColumnQty        = "Qty",
                    BomColumnReference  = "Reference",
                    BomColumnNote       = "Note",
                    ExtensionData       = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["UnknownFutureKey"] = futureDoc.RootElement,
                        ["AnotherUnknown"]   = intDoc.RootElement,
                    }
                }
            };

            var vm = CreateVm(provider);
            vm.IpnProperty = "NewPartNo";

            var saved = vm.Save();

            Assert.That(saved, Is.True);
            Assert.That(provider.LastSaved, Is.Not.Null);
            Assert.That(provider.LastSaved!.IpnProperty, Is.EqualTo("NewPartNo"));
            Assert.That(provider.LastSaved!.ExtensionData, Does.ContainKey("UnknownFutureKey"));
            Assert.That(provider.LastSaved!.ExtensionData, Does.ContainKey("AnotherUnknown"));
            Assert.That(provider.LastSaved!.ExtensionData["UnknownFutureKey"].GetString(), Is.EqualTo("future-value"));
            Assert.That(provider.LastSaved!.ExtensionData["AnotherUnknown"].GetInt32(), Is.EqualTo(42));
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private StubPropertyMappingProvider ValidProvider()
        {
            return new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "PartNo",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };
        }
    }
}
