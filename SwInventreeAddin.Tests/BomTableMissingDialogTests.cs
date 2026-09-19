using System.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class BomTableMissingDialogTests
    {
        [Test, Timeout(10000)]
        public void ShowDialog_CentersOnOwnerWindow()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();

            var dialog = new BomTableMissingDialog("inventree", form.Handle);
            var wait = HiddenTestWindow.WaitForCenteredOnOwnerAsync(dialog, form.Handle);

            dialog.ShowDialog();
            wait.GetAwaiter().GetResult();
        }
    }
}
