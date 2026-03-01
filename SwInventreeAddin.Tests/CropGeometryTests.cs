using System.Drawing;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for CropGeometry — the pure-math crop logic.
    /// No UI, no STA, no window instantiation.
    /// </summary>
    [TestFixture]
    public class CropGeometryTests
    {
        private CropGeometry _geo = null!;

        [SetUp]
        public void SetUp()
        {
            _geo = new CropGeometry(400, 300);
        }

        [Test]
        public void SquareLock_IsOnByDefault()
        {
            Assert.That(_geo.SquareLock, Is.True);
        }

        [Test]
        public void WithSquareLock_DragProducesSquareRect()
        {
            _geo.SquareLock = true;
            _geo.SimulateDrag(new Point(50, 50), new Point(200, 120));

            var rect = _geo.CropRectangle;
            Assert.That(rect.Width, Is.EqualTo(rect.Height),
                "Square lock: width and height should be equal");
        }

        [Test]
        public void WithoutSquareLock_DragProducesNonSquareRect()
        {
            _geo.SquareLock = false;
            _geo.SimulateDrag(new Point(50, 50), new Point(200, 120));

            var rect = _geo.CropRectangle;
            Assert.That(rect.Width, Is.Not.EqualTo(rect.Height),
                "Without square lock: width (150) and height (70) should differ");
        }

        [Test]
        public void SmallDrag_DoesNotProduceCropRect()
        {
            _geo.SimulateDrag(new Point(50, 50), new Point(52, 52));

            Assert.That(_geo.CropRectangle, Is.EqualTo(Rectangle.Empty));
            Assert.That(_geo.HasCrop, Is.False);
        }

        [Test]
        public void ValidDrag_SetsHasCropTrue()
        {
            _geo.SimulateDrag(new Point(10, 10), new Point(200, 200));

            Assert.That(_geo.HasCrop, Is.True);
        }

        [Test]
        public void MoveRect_ClampsToImageBounds()
        {
            _geo.SimulateDrag(new Point(10, 10), new Point(110, 110));
            var originalSize = _geo.CropRectangle.Size;

            // Move far past the right/bottom edge
            _geo.OnMouseDown(new Point(60, 60));   // inside the rect
            _geo.OnMouseMove(new Point(500, 500));
            _geo.OnMouseUp(new Point(500, 500));

            var rect = _geo.CropRectangle;
            Assert.That(rect.Right,  Is.LessThanOrEqualTo(400));
            Assert.That(rect.Bottom, Is.LessThanOrEqualTo(300));
            Assert.That(rect.Size,   Is.EqualTo(originalSize), "Move should not change size");
        }

        [Test]
        public void DisplayToImage_CentresAndScalesCorrectly()
        {
            // Display is 800x600, image is 400x300 => scale = 2, offset = (0, 0)
            var result = _geo.DisplayToImage(new Point(200, 150), 800, 600);

            Assert.That(result, Is.EqualTo(new Point(100, 75)));
        }

        [Test]
        public void ImageToDisplay_RoundTrips()
        {
            var orig    = new Point(100, 75);
            var display = _geo.ImageToDisplay(orig, 800, 600);
            var back    = _geo.DisplayToImage(display, 800, 600);

            Assert.That(back, Is.EqualTo(orig));
        }
    }
}
