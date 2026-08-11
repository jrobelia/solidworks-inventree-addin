using System;
using System.Collections.Generic;
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

            var service = CreateService();

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

        [Test]
        public async Task PushAsync_WhenGetPartByPkThrowsAfterUpload_ReportsWarningAndReturnsNull()
        {
            const int expectedPk = 42;
            _client.PartByPkToReturn = new InventreePart
            {
                Pk = expectedPk,
                ThumbnailUrl = "/media/test.png",
            };
            _client.ThrowOnGetPartByPk = true;

            var reports = new List<(string Text, StatusSeverity Severity)>();

            using var image = new Bitmap(10, 10);
            var result = await CreateService().PushAsync(
                expectedPk,
                (text, severity) => reports.Add((text, severity)),
                image);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(expectedPk));
            Assert.That(result, Is.Null);
            AssertLastReportWarns(reports, "could not be refreshed");
        }

        [Test]
        public async Task PushAsync_WhenDownloadImageThrowsAfterUpload_ReportsWarningAndReturnsNull()
        {
            const int expectedPk = 42;
            _client.PartByPkToReturn = new InventreePart
            {
                Pk = expectedPk,
                ThumbnailUrl = "/media/test.png",
            };
            _client.ThrowOnDownload = new Exception("download failed");

            var reports = new List<(string Text, StatusSeverity Severity)>();

            using var image = new Bitmap(10, 10);
            var result = await CreateService().PushAsync(
                expectedPk,
                (text, severity) => reports.Add((text, severity)),
                image);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(expectedPk));
            Assert.That(_client.DownloadImageCallCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(result, Is.Null);
            AssertLastReportWarns(reports, "preview");
        }

        [Test]
        public async Task PushAsync_WhenPartHasNoThumbnailUrl_ReportsWarningAndReturnsNull()
        {
            const int expectedPk = 42;
            _client.PartByPkToReturn = new InventreePart { Pk = expectedPk };

            var reports = new List<(string Text, StatusSeverity Severity)>();

            using var image = new Bitmap(10, 10);
            var result = await CreateService().PushAsync(
                expectedPk,
                (text, severity) => reports.Add((text, severity)),
                image);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(expectedPk));
            Assert.That(_client.DownloadImageCallCount, Is.EqualTo(0));
            Assert.That(result, Is.Null);
            AssertLastReportWarns(reports, "thumbnail URL");
        }

        private PartThumbnailService CreateService() => new PartThumbnailService(_client, null);

        private static void AssertLastReportWarns(List<(string Text, StatusSeverity Severity)> reports, string expectedText)
        {
            Assert.That(reports, Has.Count.GreaterThanOrEqualTo(1));
            var lastReport = reports[reports.Count - 1];
            Assert.That(lastReport.Severity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(lastReport.Text, Does.Contain(expectedText).IgnoreCase);
        }
    }
}
