using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for MappingResult — the cross-seam value object that carries
    /// PropertyMappingConfig, MappingHealth, and the derived command permissions.
    /// </summary>
    [TestFixture]
    public class MappingResultTests
    {
        [Test]
        public void ResolvedFilePath_SetInConstructor_IsExposed()
        {
            var result = new MappingResult(
                MappingHealth.Healthy,
                PropertyMappingConfig.WithDefaults(),
                resolvedFilePath: "C:\\mapping.json");

            Assert.That(result.ResolvedFilePath, Is.EqualTo("C:\\mapping.json"));
        }

        [Test]
        public void Source_DefaultIsLocal()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.Source, Is.EqualTo(MappingSource.Local));
        }

        [Test]
        public void Source_SetInConstructor_IsExposed()
        {
            var result = new MappingResult(
                MappingHealth.Healthy,
                PropertyMappingConfig.WithDefaults(),
                source: MappingSource.Shared);

            Assert.That(result.Source, Is.EqualTo(MappingSource.Shared));
        }

        [Test]
        public void Message_SetInConstructor_IsExposed()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults(), "schema mismatch");

            Assert.That(result.Message, Is.EqualTo("schema mismatch"));
        }

        [Test]
        public void CanUseForPartSync_NewerSchema_IsFalse()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults(), "upgrade add-in");

            Assert.That(result.CanUseForPartSync, Is.False);
        }

        [Test]
        public void CanFetch_NewerSchema_IsFalse()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults(), "upgrade add-in");

            Assert.That(result.CanFetch, Is.False);
        }

        [Test]
        public void CanFetch_Invalid_IsFalse()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults(), "invalid");

            Assert.That(result.CanFetch, Is.False);
        }

        [Test]
        public void CanUseForPartSync_Healthy_IsTrue()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.CanUseForPartSync, Is.True);
        }

        [Test]
        public void CanFetch_Healthy_IsTrue()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.CanFetch, Is.True);
        }

        [Test]
        public void CanUseForPartSync_NeedsUpgrade_IsFalse()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults(), "upgrade");

            Assert.That(result.CanUseForPartSync, Is.False);
        }

        [Test]
        public void CanFetch_NeedsUpgrade_IsFalse()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults(), "upgrade");

            Assert.That(result.CanFetch, Is.False);
        }

        [Test]
        public void CanEdit_NeedsUpgrade_IsTrue()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults(), "upgrade");

            Assert.That(result.CanEdit, Is.True);
        }

        [Test]
        public void CanEdit_NewerSchema_IsFalse()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults(), "upgrade add-in");

            Assert.That(result.CanEdit, Is.False);
        }

        [Test]
        public void CanEdit_Invalid_IsFalse()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults(), "invalid");

            Assert.That(result.CanEdit, Is.False);
        }

        [Test]
        public void MessageOrDefault_WhenMessageIsNull_FallsBackToHealthLabel()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.MessageOrDefault, Is.EqualTo("The Property Mapping file is up to date and valid."));
        }

        [Test]
        public void MessageOrDefault_NeedsUpgrade_FallsBackToHealthLabel()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults());

            Assert.That(result.MessageOrDefault, Is.EqualTo("The Property Mapping Schema is out of date."));
        }

        [Test]
        public void ToolTip_Healthy_IsNull()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.ToolTip, Is.Null);
        }

        [Test]
        public void ToolTip_NeedsUpgrade_SuggestsEditAndSave()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults());

            Assert.That(result.ToolTip, Does.Contain("Edit the Property Mapping").IgnoreCase);
        }

        [Test]
        public void ToolTip_NewerSchema_SuggestsUpgradeAddIn()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults());

            Assert.That(result.ToolTip, Does.Contain("add-in").IgnoreCase);
        }

        [Test]
        public void ToolTip_Invalid_WhenMessageSupplied_AppendsHelpToMessage()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults(), "Detail.");

            Assert.That(result.ToolTip, Does.StartWith("Detail."));
            Assert.That(result.ToolTip, Does.Contain(MappingResult.InvalidMappingHelp));
        }

        [Test]
        public void ToolTip_Invalid_WhenMessageIsNull_UsesDefaultAndHelp()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults());

            Assert.That(result.ToolTip, Does.Contain("The Property Mapping file is invalid."));
            Assert.That(result.ToolTip, Does.Contain(MappingResult.InvalidMappingHelp));
        }

        [Test]
        public void FullStatusMessage_Healthy_ReturnsDefaultMessage()
        {
            var result = new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

            Assert.That(result.FullStatusMessage, Is.EqualTo("The Property Mapping file is up to date and valid."));
        }

        [Test]
        public void FullStatusMessage_NeedsUpgrade_CombinesStateAndAction()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults());

            Assert.That(result.FullStatusMessage, Does.Contain("The Property Mapping Schema is out of date."));
            Assert.That(result.FullStatusMessage, Does.Contain("Edit the Property Mapping and save to enable Part Sync."));
        }

        [Test]
        public void FullStatusMessage_NewerSchema_CombinesStateAndAction()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults());

            Assert.That(result.FullStatusMessage, Does.Contain("The Property Mapping Schema is newer than this add-in."));
            Assert.That(result.FullStatusMessage, Does.Contain("Upgrade the add-in to enable Part Sync."));
        }

        [Test]
        public void FullStatusMessage_Invalid_WhenMessageIsNull_UsesDefaultAndHelp()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults());

            Assert.That(result.FullStatusMessage, Does.Contain("The Property Mapping file is invalid."));
            Assert.That(result.FullStatusMessage, Does.Contain(MappingResult.InvalidMappingHelp));
        }

        [Test]
        public void FullStatusMessage_Invalid_WhenMessageIsSupplied_IncludesStateMessageAndHelp()
        {
            var result = new MappingResult(MappingHealth.Invalid, PropertyMappingConfig.WithDefaults(), "Missing file.");

            Assert.That(result.FullStatusMessage, Does.StartWith("The Property Mapping file is invalid."));
            Assert.That(result.FullStatusMessage, Does.Contain("Missing file."));
            Assert.That(result.FullStatusMessage, Does.Contain(MappingResult.InvalidMappingHelp));
        }
    }
}
