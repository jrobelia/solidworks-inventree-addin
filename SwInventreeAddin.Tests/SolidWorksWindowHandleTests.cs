using System;
using System.Diagnostics;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class SolidWorksWindowHandleTests
    {
        [TearDown]
        public void TearDown() => SolidWorksWindowHandle.Set(IntPtr.Zero);

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
