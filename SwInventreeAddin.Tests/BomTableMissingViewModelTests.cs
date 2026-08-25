using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomTableMissingViewModelTests
    {
        [Test]
        public void Message_WithInventreeKeyword_ContainsKeywordAndBomSyncPath()
        {
            var vm = new BomTableMissingViewModel("inventree");

            Assert.That(vm.Message, Does.Contain("inventree"));
            Assert.That(vm.Message, Does.Contain("Create a BOM table"));
            Assert.That(vm.Message, Does.Contain("BOM Sync"));
        }

        [Test]
        public void Constructor_NullBomKeyword_Throws()
        {
            Assert.That(() => new BomTableMissingViewModel(null!),
                Throws.ArgumentNullException);
        }
    }
}
