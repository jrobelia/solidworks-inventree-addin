using System;
using System.Diagnostics;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class WindowHandleOwnerTests
    {
        [TearDown]
        public void TearDown() => SolidWorksWindowHandle.Set(IntPtr.Zero);

        [Test]
        public void Handle_ReturnsSuppliedHandleUnchanged()
        {
            var expected = new IntPtr(0x12345678);
            var owner    = new WindowHandleOwner(expected);

            Assert.That(owner.Handle, Is.EqualTo(expected));
        }

        [Test]
        public void Handle_SupportsZeroHandle()
        {
            var owner = new WindowHandleOwner(IntPtr.Zero);

            Assert.That(owner.Handle, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void SolidWorksWindowHandle_Get_ReturnsSetValue()
        {
            var expected = new IntPtr(0xDEADBEEF);
            SolidWorksWindowHandle.Set(expected);

            Assert.That(SolidWorksWindowHandle.Get(), Is.EqualTo(expected));
        }

        [Test]
        public void SolidWorksWindowHandle_Get_WhenNotSet_FallsBackToProcessMainWindowHandle()
        {
            SolidWorksWindowHandle.Set(IntPtr.Zero);

            var fallback = Process.GetCurrentProcess().MainWindowHandle;

            Assert.That(SolidWorksWindowHandle.Get(), Is.EqualTo(fallback));
        }
    }
}
