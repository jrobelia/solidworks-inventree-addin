using System.IO;
using System.Linq;
using NUnit.Framework;

namespace SwInventreeAddin.Tests.Capture
{
    [TestFixture]
    public class SurfaceCaptureCatalogTests
    {
        [Test]
        public void Catalog_ThreeSettingsWindowStates_AreRegistered()
        {
            var states = SurfaceCapture.Catalog
                .Where(e => e.Surface == "SettingsWindow")
                .Select(e => e.State)
                .ToList();

            Assert.That(states, Is.EquivalentTo(new[] { "fresh", "configured", "change-credential" }));
        }

        [Test]
        public void Catalog_SurfaceStatePairs_AreUnique()
        {
            var keys = SurfaceCapture.Catalog.Select(e => (e.Surface, e.State)).ToList();

            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
        }

        [Test]
        public void Catalog_EntryNames_AreFilesystemSafe()
        {
            var invalid = Path.GetInvalidFileNameChars();

            foreach (var entry in SurfaceCapture.Catalog)
            {
                Assert.That(entry.Surface, Is.Not.Null.And.Not.Empty);
                Assert.That(entry.State, Is.Not.Null.And.Not.Empty);
                Assert.That(entry.Surface.IndexOfAny(invalid), Is.EqualTo(-1), entry.Surface);
                Assert.That(entry.State.IndexOfAny(invalid), Is.EqualTo(-1), entry.State);
            }
        }

        [Test]
        public void PngPathFor_SettingsWindowFresh_ReturnsPredictablePath()
        {
            var path = SurfaceCapture.PngPathFor("SettingsWindow", "fresh");

            Assert.That(path, Is.EqualTo(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Captures", "SettingsWindow-fresh.png")));
        }
    }
}
