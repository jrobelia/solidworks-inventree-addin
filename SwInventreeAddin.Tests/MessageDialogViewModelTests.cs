using System;
using System.Windows.Forms;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class MessageDialogViewModelTests
    {
        private MessageDialogViewModel CreateVm(
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.Warning)
            => new MessageDialogViewModel("Title", "Message", buttons, icon);

        [Test]
        public void ButtonVisibility_Ok_OnlyOkIsVisible()
        {
            var vm = CreateVm(MessageBoxButtons.OK);

            Assert.That((vm.IsOkVisible, vm.IsCancelVisible, vm.IsYesVisible, vm.IsNoVisible),
                        Is.EqualTo((true, false, false, false)));
        }

        [Test]
        public void ButtonVisibility_OkCancel_OkAndCancelAreVisible()
        {
            var vm = CreateVm(MessageBoxButtons.OKCancel);

            Assert.That((vm.IsOkVisible, vm.IsCancelVisible, vm.IsYesVisible, vm.IsNoVisible),
                        Is.EqualTo((true, true, false, false)));
        }

        [Test]
        public void ButtonVisibility_YesNo_YesAndNoAreVisible()
        {
            var vm = CreateVm(MessageBoxButtons.YesNo);

            Assert.That((vm.IsOkVisible, vm.IsCancelVisible, vm.IsYesVisible, vm.IsNoVisible),
                        Is.EqualTo((false, false, true, true)));
        }

        // Only the button sets the MessageDialog helpers produce are supported;
        // anything else must fail loudly rather than show the wrong choices.

        [Test]
        [TestCase(MessageBoxButtons.YesNoCancel)]
        [TestCase(MessageBoxButtons.RetryCancel)]
        [TestCase(MessageBoxButtons.AbortRetryIgnore)]
        public void Ctor_UnsupportedButtonSet_Throws(MessageBoxButtons buttons)
        {
            Assert.That(() => CreateVm(buttons), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Result_BeforeAnyButton_IsNone()
        {
            var vm = CreateVm();

            Assert.That(vm.Result, Is.EqualTo(MessageDialogResult.None));
        }

        [Test]
        public void ClickOk_SetsResultOkAndRequestsClose()
        {
            var vm = CreateVm(MessageBoxButtons.OKCancel);
            var closeRequested = false;
            vm.CloseRequested += (s, e) => closeRequested = true;

            vm.ClickOk();

            Assert.That((vm.Result, closeRequested),
                        Is.EqualTo((MessageDialogResult.Ok, true)));
        }

        [Test]
        public void ClickCancel_SetsResultCancelAndRequestsClose()
        {
            var vm = CreateVm(MessageBoxButtons.OKCancel);
            var closeRequested = false;
            vm.CloseRequested += (s, e) => closeRequested = true;

            vm.ClickCancel();

            Assert.That((vm.Result, closeRequested),
                        Is.EqualTo((MessageDialogResult.Cancel, true)));
        }

        [Test]
        public void ClickYes_SetsResultYesAndRequestsClose()
        {
            var vm = CreateVm(MessageBoxButtons.YesNo);
            var closeRequested = false;
            vm.CloseRequested += (s, e) => closeRequested = true;

            vm.ClickYes();

            Assert.That((vm.Result, closeRequested),
                        Is.EqualTo((MessageDialogResult.Yes, true)));
        }

        [Test]
        public void ClickNo_SetsResultNoAndRequestsClose()
        {
            var vm = CreateVm(MessageBoxButtons.YesNo);
            var closeRequested = false;
            vm.CloseRequested += (s, e) => closeRequested = true;

            vm.ClickNo();

            Assert.That((vm.Result, closeRequested),
                        Is.EqualTo((MessageDialogResult.No, true)));
        }

        // The window's X button and Esc key dismiss with the same result a
        // WinForms MessageBox would produce: Cancel, else No, else Ok.

        [Test]
        public void SetCloseResult_OkOnly_SetsOk()
        {
            var vm = CreateVm(MessageBoxButtons.OK);

            vm.SetCloseResult();

            Assert.That(vm.Result, Is.EqualTo(MessageDialogResult.Ok));
        }

        [Test]
        public void SetCloseResult_OkCancel_SetsCancel()
        {
            var vm = CreateVm(MessageBoxButtons.OKCancel);

            vm.SetCloseResult();

            Assert.That(vm.Result, Is.EqualTo(MessageDialogResult.Cancel));
        }

        [Test]
        public void SetCloseResult_YesNo_SetsNo()
        {
            var vm = CreateVm(MessageBoxButtons.YesNo);

            vm.SetCloseResult();

            Assert.That(vm.Result, Is.EqualTo(MessageDialogResult.No));
        }

        [Test]
        public void IconGlyph_Warning_IsWarningTriangle()
        {
            var vm = CreateVm(icon: MessageBoxIcon.Warning);

            Assert.That(vm.IconGlyph, Is.EqualTo("\uE7BA"));
        }

        [Test]
        public void IconGlyph_Error_IsErrorBadge()
        {
            var vm = CreateVm(icon: MessageBoxIcon.Error);

            Assert.That(vm.IconGlyph, Is.EqualTo("\uE783"));
        }

        [Test]
        public void IconGlyph_Question_IsQuestionMark()
        {
            var vm = CreateVm(icon: MessageBoxIcon.Question);

            Assert.That(vm.IconGlyph, Is.EqualTo("\uE897"));
        }

        [Test]
        [TestCase(MessageBoxIcon.Warning,     MessageDialogIconKind.Warning)]
        [TestCase(MessageBoxIcon.Error,       MessageDialogIconKind.Error)]
        [TestCase(MessageBoxIcon.Stop,        MessageDialogIconKind.Error)]
        [TestCase(MessageBoxIcon.Question,    MessageDialogIconKind.Question)]
        [TestCase(MessageBoxIcon.Information, MessageDialogIconKind.Information)]
        [TestCase(MessageBoxIcon.None,        MessageDialogIconKind.None)]
        public void IconKind_MapsFromMessageBoxIcon(MessageBoxIcon icon, MessageDialogIconKind expected)
        {
            var vm = CreateVm(icon: icon);

            Assert.That(vm.IconKind, Is.EqualTo(expected));
        }

        [Test]
        public void IsIconVisible_NoneIcon_IsFalse()
        {
            var vm = CreateVm(icon: MessageBoxIcon.None);

            Assert.That(vm.IsIconVisible, Is.False);
        }

        [Test]
        public void IsIconVisible_WarningIcon_IsTrue()
        {
            var vm = CreateVm(icon: MessageBoxIcon.Warning);

            Assert.That(vm.IsIconVisible, Is.True);
        }
    }
}
