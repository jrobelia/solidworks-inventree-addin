using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class MessageDialogTests
    {
        [Test, Timeout(10000)]
        public void ShowDialog_CentersOnOwnerWindow_AndCloseYieldsDefaultResult()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();

            var vm = new MessageDialogViewModel(
                "BOM Compare",
                "Test message",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            var dialog = new MessageDialog(vm, form.Handle);
            var wait = HiddenTestWindow.WaitForCenteredOnOwnerAsync(dialog, form.Handle);

            // Closing without clicking a button resolves to the cancel-equivalent result.
            var result = dialog.ShowDialog();
            wait.GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(MessageDialogResult.Cancel));
        }
    }
}
