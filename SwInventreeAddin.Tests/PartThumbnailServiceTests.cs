using System.Drawing;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class PartThumbnailServiceTests
    {
        private StubInventreeClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _client = new StubInventreeClient();
        }

        [Test]
        public async Task PushAsync_UploadsImage_ReFetchesByPk_AndReturnsThumbnailBytes()
        {
            const int expectedPk = 42;
            var uploadedBytes = new byte[] { 1, 2, 3 };
            var expectedThumb = new byte[] { 4, 5, 6 };

            _client.PartByPkToReturn = new InventreePart
            {
                Pk = expectedPk,
                ThumbnailUrl = "/media/test.png",
            };
            _client.ThumbnailBytesToReturn = expectedThumb;

            var service = new PartThumbnailService(_client, null);

            using var image = new Bitmap(10, 10);
            var result = await service.PushAsync(
                expectedPk,
                (text, severity) => { },
                image);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(expectedPk));
            Assert.That(_client.LastGetPartByPkPk, Is.EqualTo(expectedPk));
            Assert.That(_client.DownloadImageCallCount, Is.EqualTo(1));
            Assert.That(result, Is.SameAs(expectedThumb));
        }
    }
}
