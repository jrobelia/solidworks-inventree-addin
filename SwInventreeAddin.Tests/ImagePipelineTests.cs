using System.Drawing;
using System.IO;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class ImagePipelineTests
    {
        private static Image MakeImage(int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(Color.White);
            return bmp;
        }

        private static Size DecodePngSize(byte[] png)
        {
            using var ms = new MemoryStream(png);
            using var img = Image.FromStream(ms);
            return img.Size;
        }

        [Test]
        public void Process_WithNoCrop_ReturnsPngByteArray()
        {
            using var img = MakeImage(100, 100);

            var result = ImagePipeline.Process(img, Rectangle.Empty);

            Assert.That(result[0], Is.EqualTo(137));
            Assert.That(result[1], Is.EqualTo(80));
            Assert.That(result[2], Is.EqualTo(78));
            Assert.That(result[3], Is.EqualTo(71));
        }

        [Test]
        public void Process_WithNoCrop_ImageFitsWithin800x800()
        {
            using var img = MakeImage(1200, 900);

            var result = ImagePipeline.Process(img, Rectangle.Empty);
            var size   = DecodePngSize(result);

            Assert.That(size.Width,  Is.LessThanOrEqualTo(800));
            Assert.That(size.Height, Is.LessThanOrEqualTo(800));
        }

        [Test]
        public void Process_WithNoCrop_SmallImageIsNotUpscaled()
        {
            using var img = MakeImage(100, 80);

            var result = ImagePipeline.Process(img, Rectangle.Empty);
            var size   = DecodePngSize(result);

            Assert.That(size.Width,  Is.EqualTo(100));
            Assert.That(size.Height, Is.EqualTo(80));
        }

        [Test]
        public void Process_WithCropRect_CropsBeforeResize()
        {
            using var img = MakeImage(400, 400);
            var crop = new Rectangle(50, 50, 200, 200);

            var result = ImagePipeline.Process(img, crop);
            var size   = DecodePngSize(result);

            // Cropped to 200x200 -- fits within 800x800 so no resize occurs.
            Assert.That(size.Width,  Is.EqualTo(200));
            Assert.That(size.Height, Is.EqualTo(200));
        }

        [Test]
        public void Process_PreservesAspectRatio()
        {
            using var img = MakeImage(1600, 800);

            var result = ImagePipeline.Process(img, Rectangle.Empty);
            var size   = DecodePngSize(result);

            // Width capped at 800; height scales to 400.
            Assert.That(size.Width,  Is.EqualTo(800));
            Assert.That(size.Height, Is.EqualTo(400));
        }
    }
}