using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class ServerConnectionStatusTests
    {
        [Test]
        public void From_NoSavedConfig_SaysNoServerSettingsSaved()
        {
            var status = ServerConnectionStatus.From(null);

            Assert.That(status.Message, Is.EqualTo("No server settings saved"));
        }

        [Test]
        public void From_NoSavedConfig_HasNoServerUrl()
        {
            var status = ServerConnectionStatus.From(null);

            Assert.That(status.HasServerUrl, Is.False);
        }

        [Test]
        public void From_BlankUrl_SaysNoServerSettingsSaved()
        {
            var status = ServerConnectionStatus.From(new ServerConfig { Url = "   ", ApiKey = "key" });

            Assert.That(status.Message, Is.EqualTo("No server settings saved"));
        }

        [Test]
        public void From_UrlAndApiKey_SaysApiKeySaved()
        {
            var status = ServerConnectionStatus.From(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(status.Message,
                        Is.EqualTo("Server connection configured \u2014 API key saved"));
        }

        [Test]
        public void From_UrlWithoutApiKey_SaysNoApiKeySaved()
        {
            var status = ServerConnectionStatus.From(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = string.Empty });

            Assert.That(status.Message,
                        Is.EqualTo("Server connection configured \u2014 no API key saved"));
        }

        [Test]
        public void From_SavedUrl_ExposesTrimmedServerUrl()
        {
            var status = ServerConnectionStatus.From(
                new ServerConfig { Url = "  https://inventree.example.com  ", ApiKey = "saved-key" });

            Assert.That(status.ServerUrl, Is.EqualTo("https://inventree.example.com"));
        }

        [Test]
        public void From_SavedUrl_HasServerUrl()
        {
            var status = ServerConnectionStatus.From(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(status.HasServerUrl, Is.True);
        }

        [Test]
        public void From_NoSavedConfig_ServerUrlIsEmpty()
        {
            var status = ServerConnectionStatus.From(null);

            Assert.That(status.ServerUrl, Is.Empty);
        }
    }
}
