using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Logging;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for <see cref="FileAddinLogger"/> — the production file adapter.
    /// Every test writes into a fresh temporary directory, never the real
    /// %LOCALAPPDATA% path.
    /// </summary>
    [TestFixture]
    public class FileAddinLoggerTests
    {
        private string _dir = null!;
        private string LogPath => Path.Combine(_dir, "inventree-addin.log");

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SwInventreeAddinTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        private FileAddinLogger CreateLogger(params string[] secrets) =>
            new FileAddinLogger(secrets, _dir);

        [Test]
        public void LogInformation_WritesTimestampedEntry()
        {
            using (var logger = CreateLogger())
                logger.LogInformation("Fetch started");

            var text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("INFO"));
            Assert.That(text, Does.Contain("Fetch started"));
        }

        [Test]
        public void LogError_WritesEntryWithExceptionDetail()
        {
            using (var logger = CreateLogger())
                logger.LogError("Push failed", new InvalidOperationException("host gone"));

            var text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("ERROR"));
            Assert.That(text, Does.Contain("Push failed"));
            Assert.That(text, Does.Contain("InvalidOperationException"));
            Assert.That(text, Does.Contain("host gone"));
        }

        [Test]
        public void Write_RedactsRegisteredSecretsInMessageAndException()
        {
            const string apiKey = "s3cr3t-key-12345";
            using (var logger = CreateLogger(apiKey))
            {
                logger.LogInformation($"connected with {apiKey}");
                logger.LogError("auth failure", new InvalidOperationException($"key {apiKey} rejected"));
            }

            var text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Not.Contain(apiKey), "the registered secret must never reach the file");
            Assert.That(text, Does.Contain("***"));
        }

        [Test]
        public void Write_NullOrEmptySecret_IsIgnored()
        {
            using (var logger = new FileAddinLogger(new string?[] { null, string.Empty }, _dir))
                logger.LogInformation("plain entry");

            var text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("plain entry"));
        }

        [Test]
        public void Write_CrossesOneMegabyte_RollsAndKeepsThreeFiles()
        {
            var payload = new string('x', 4000);
            using (var logger = CreateLogger())
            {
                // Each entry is ~4 KB; 700 entries ≈ 2.8 MB — forces at least
                // two rollovers past the 1 MB cap.
                for (var i = 0; i < 700; i++)
                    logger.LogInformation($"{i:D5} {payload}");
            }

            var files = Directory.GetFiles(_dir);
            Assert.That(files.Length, Is.EqualTo(3),
                "the active file plus two archives — the oldest is overwritten");
            Assert.That(files, Does.Contain(LogPath));
            Assert.That(new FileInfo(LogPath).Length, Is.LessThan(2 * 1024 * 1024));
        }

        [Test]
        public void Write_ConcurrentThreads_AllEntriesLand()
        {
            const int writers = 8;
            const int entriesPerWriter = 25;
            using (var logger = CreateLogger())
            {
                var tasks = Enumerable.Range(0, writers)
                    .Select(w => Task.Run(() =>
                    {
                        for (var i = 0; i < entriesPerWriter; i++)
                            logger.LogInformation($"writer-{w} entry-{i}");
                    }))
                    .ToArray();
                Task.WaitAll(tasks);
            }

            var text = File.ReadAllText(LogPath);
            for (var w = 0; w < writers; w++)
                for (var i = 0; i < entriesPerWriter; i++)
                    Assert.That(text, Does.Contain($"writer-{w} entry-{i}"));
        }

        [Test]
        public void Write_AfterDispose_IsIgnored()
        {
            var logger = CreateLogger();
            logger.LogInformation("before dispose");
            logger.Dispose();
            logger.LogInformation("after dispose");

            var text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("before dispose"));
            Assert.That(text, Does.Not.Contain("after dispose"));
        }
    }
}
