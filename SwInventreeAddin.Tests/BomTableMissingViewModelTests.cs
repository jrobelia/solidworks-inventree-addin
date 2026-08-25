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
            Assert.That(vm.Message, Does.Contain("BOM Sync"));
            Assert.That(vm.Message, Does.Contain("BOM Table Keyword"));
        }

        [Test]
        public void Constructor_NullBomKeyword_Throws()
        {
            Assert.That(() => new BomTableMissingViewModel(null!),
                Throws.ArgumentNullException);
        }
    }
}
