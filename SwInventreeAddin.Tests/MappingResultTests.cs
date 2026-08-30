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
        public void CanFetch_NewerSchema_IsTrue()
        {
            var result = new MappingResult(MappingHealth.NewerSchema, PropertyMappingConfig.WithDefaults(), "upgrade add-in");

            Assert.That(result.CanFetch, Is.True);
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
        public void CanFetch_NeedsUpgrade_IsTrue()
        {
            var result = new MappingResult(MappingHealth.NeedsUpgrade, PropertyMappingConfig.WithDefaults(), "upgrade");

            Assert.That(result.CanFetch, Is.True);
        }
    }
}
