using System.Drawing;
using System.Threading;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for the ImageCropForm dialog.
    /// WinForms requires STA; NUnit honours [Apartment].
    /// Tests use SimulateDrag() which works in image coordinates,
    /// bypassing the PictureBox coordinate translation.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ImageCropFormTests
    {
        private ImageCropForm _form = null!;

        [SetUp]
        public void SetUp()
        {
            var img = new Bitmap(400, 300);
            _form = new ImageCropForm(img);
        }

        [TearDown]
        public void TearDown()
        {
            _form?.Dispose();
        }

        [Test]
        public void Title_IsCorrect()
        {
            Assert.That(_form.Text, Is.EqualTo("Crop Image for InvenTree"));
        }

        [Test]
        public void SquareLockCheckBox_IsCheckedByDefault()
        {
            Assert.That(_form.SquareLockCheckBox.Checked, Is.True);
        }

        [Test]
        public void WithSquareLock_DragProducesSquareRect()
        {
            _form.SquareLockCheckBox.Checked = true;
            _form.SimulateDrag(new Point(50, 50), new Point(200, 120));

            var rect = _form.CropRectangle;
            Assert.That(rect.Width, Is.EqualTo(rect.Height),
                "Square lock: width and height should be equal");
        }

        [Test]
        public void WithoutSquareLock_DragProducesNonSquareRect()
        {
            _form.SquareLockCheckBox.Checked = false;
            _form.SimulateDrag(new Point(50, 50), new Point(200, 120));

            var rect = _form.CropRectangle;
            Assert.That(rect.Width, Is.Not.EqualTo(rect.Height),
                "Without square lock: width (150) and height (70) should differ");
        }
    }
}
