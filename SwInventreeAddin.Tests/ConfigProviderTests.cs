using System;
using System.IO;
using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class ConfigProviderTests
    {
        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.GetTempFileName();
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        private JsonFileConfigProvider CreateProvider(string serverName = "staging") =>
            new JsonFileConfigProvider(_tempFilePath, serverName);

        [Test]
        public void GetServerConfig_ValidJson_ReadsUrlCorrectly()
        {
            File.WriteAllText(_tempFilePath,
                @"{ ""servers"": { ""staging"": { ""url"": ""http://staging.example.com"", ""api_key"": ""abc123"" } } }");

            var config = CreateProvider("staging").GetServerConfig();

            Assert.That(config.Url, Is.EqualTo("http://staging.example.com"));
        }

        [Test]
        public void GetServerConfig_ValidJson_ReadsApiKeyCorrectly()
        {
            File.WriteAllText(_tempFilePath,
                @"{ ""servers"": { ""staging"": { ""url"": ""http://staging.example.com"", ""api_key"": ""abc123"" } } }");

            var config = CreateProvider("staging").GetServerConfig();

            Assert.That(config.ApiKey, Is.EqualTo("abc123"));
        }

        [Test]
        public void GetServerConfig_MissingFile_ThrowsDescriptiveException()
        {
            File.Delete(_tempFilePath);
            var provider = new JsonFileConfigProvider(_tempFilePath, "staging");

            var ex = Assert.Throws<FileNotFoundException>(() => provider.GetServerConfig());
            Assert.That(ex.Message, Does.Contain(_tempFilePath));
        }

        [Test]
        public void GetServerConfig_MalformedJson_ThrowsDescriptiveException()
        {
            File.WriteAllText(_tempFilePath, "{ this is not valid json }}}");
            var provider = CreateProvider("staging");

            Assert.Throws<Exception>(() => provider.GetServerConfig());
        }

        [Test]
        public void GetServerConfig_ReturnsStagingEntry_NotProductionEntry()
        {
            File.WriteAllText(_tempFilePath, @"{
                ""servers"": {
                    ""production"": { ""url"": ""http://production.example.com"", ""api_key"": ""prod-key"" },
                    ""staging"":    { ""url"": ""http://staging.example.com"",    ""api_key"": ""staging-key"" }
                }
            }");

            var config = CreateProvider("staging").GetServerConfig();

            Assert.That(config.Url,    Is.EqualTo("http://staging.example.com"));
            Assert.That(config.ApiKey, Is.EqualTo("staging-key"));
        }
    }
}
