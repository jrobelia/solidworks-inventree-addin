using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class ByteArrayToBitmapImageConverterTests
    {
        private static byte[] MakePng(int width, int height)
        {
            using var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static BitmapImage? Convert(byte[] bytes) =>
            new ByteArrayToBitmapImageConverter()
                .Convert(bytes, typeof(BitmapImage), null, CultureInfo.InvariantCulture)
                as BitmapImage;

        [Test]
        public void Convert_ImageWiderThanCap_DecodesDownToCap()
        {
            var png = MakePng(ByteArrayToBitmapImageConverter.MaxDecodePixelWidth * 8, 64);

            var image = Convert(png);

            Assert.That(image, Is.Not.Null);
            Assert.That(image!.PixelWidth, Is.EqualTo(ByteArrayToBitmapImageConverter.MaxDecodePixelWidth));
        }

        [Test]
        public void Convert_SmallImage_KeepsNaturalSize()
        {
            var png = MakePng(64, 32);

            var image = Convert(png);

            Assert.That(image, Is.Not.Null);
            Assert.That(image!.PixelWidth, Is.EqualTo(64));
            Assert.That(image.PixelHeight, Is.EqualTo(32));
        }

        [Test]
        public void Convert_NullOrEmpty_ReturnsNull()
        {
            Assert.That(Convert(null!), Is.Null);
            Assert.That(Convert(new byte[0]), Is.Null);
        }

        [Test]
        public void Convert_MalformedBytes_ReturnsNull()
        {
            Assert.That(Convert(new byte[] { 1, 2, 3, 4 }), Is.Null);
        }
    }
}
