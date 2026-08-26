using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class WindowCenteringTests
    {
        [Test]
        public void CalculateCenteredPosition_DialogFitsInOwner_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = 100, Top = 100, Right = 500, Bottom = 400 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 200, Bottom = 100 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(200));
            Assert.That(top, Is.EqualTo(200));
        }

        [Test]
        public void CalculateCenteredPosition_NegativeOwnerOrigin_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = -200, Top = -100, Right = 200, Bottom = 200 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 200, Bottom = 100 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(-100));
            Assert.That(top, Is.EqualTo(0));
        }

        [Test]
        public void CalculateCenteredPosition_OwnerSmallerThanDialog_ReturnsCenter()
        {
            var owner = new WindowCentering.NativeRect { Left = 50, Top = 50, Right = 150, Bottom = 150 };
            var dialog = new WindowCentering.NativeRect { Left = 0, Top = 0, Right = 300, Bottom = 200 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(-50));
            Assert.That(top, Is.EqualTo(0));
        }

        [Test]
        public void CalculateCenteredPosition_DialogAtNonZeroOrigin_IgnoresDialogOrigin()
        {
            var owner = new WindowCentering.NativeRect { Left = 100, Top = 100, Right = 500, Bottom = 400 };
            var dialog = new WindowCentering.NativeRect { Left = 20, Top = 30, Right = 220, Bottom = 130 };

            var (left, top) = WindowCentering.CalculateCenteredPosition(owner, dialog);

            Assert.That(left, Is.EqualTo(200));
            Assert.That(top, Is.EqualTo(200));
        }
    }
}
