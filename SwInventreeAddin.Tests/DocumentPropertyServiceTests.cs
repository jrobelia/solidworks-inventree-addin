using NUnit.Framework;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class DocumentPropertyServiceTests
    {
        private StubDocumentPropertyService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new StubDocumentPropertyService();
        }

        [Test]
        public void GetCustomProperty_ExistingKey_ReturnsStoredValue()
        {
            _service.Seed("PartNo", "R-10K-0402");

            var result = _service.GetCustomProperty("PartNo");

            Assert.That(result, Is.EqualTo("R-10K-0402"));
        }

        [Test]
        public void GetCustomProperty_MissingKey_ReturnsEmptyString()
        {
            var result = _service.GetCustomProperty("Missing");

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SetCustomProperty_StoresValueCorrectly()
        {
            _service.SetCustomProperty("Description", "MyName");

            Assert.That(_service.GetCustomProperty("Description"), Is.EqualTo("MyName"));
        }

        [Test]
        public void SetCustomProperty_ExistingKey_OverwritesValue_NotDuplicates()
        {
            _service.SetCustomProperty("Description", "OldValue");
            _service.SetCustomProperty("Description", "NewValue");

            Assert.That(_service.GetCustomProperty("Description"), Is.EqualTo("NewValue"));
        }
    }
}
