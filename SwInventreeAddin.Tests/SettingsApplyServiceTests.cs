using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class SettingsApplyServiceTests
    {
        [Test]
        public async Task ApplyAsync_WhenConfigProviderThrows_ThrowsSettingsApplyExceptionWithConfigKind()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key")
            {
                ThrowOnSave = new InvalidOperationException("save failed"),
            };
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service      = new SettingsApplyService(configProvider, tokenService);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(CreateInput()));

            Assert.That(ex!.ErrorKind, Is.EqualTo(SettingsApplyErrorKind.Config));
            Assert.That(ex.Message, Does.Contain("Failed to save server settings"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenResolutionFails_ThrowsSettingsApplyExceptionWithConfigKind()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService(); // configured to fail
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input));

            Assert.That(ex!.ErrorKind, Is.EqualTo(SettingsApplyErrorKind.Config));
            Assert.That(ex.Message, Does.Contain("Failed to save server settings"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenSucceeds_SavesConfigWithResolvedToken()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "resolved-token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            await service.ApplyAsync(input);

            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("resolved-token"));
        }

        [Test]
        public async Task ApplyAsync_WhenRawApiKeyProvided_SavesConfigWithoutCallingTokenService()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "should-not-be-used" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.RawApiKey = "raw-key";

            await service.ApplyAsync(input);

            Assert.That(tokenService.LastUrl, Is.Null);
            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("raw-key"));
        }

        [Test]
        public async Task ApplyAsync_WhenUrlIsHttp_ThrowsSettingsApplyExceptionWithConfigKind()
        {
            var configProvider = new StubConfigProvider("http://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "http://example.com";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input));

            Assert.That(ex!.ErrorKind, Is.EqualTo(SettingsApplyErrorKind.Config));
            Assert.That(ex.Message, Does.Contain("https://"));
        }

        private static SettingsApplyInput CreateInput()
        {
            return new SettingsApplyInput
            {
                Url                   = "https://example.com",
                RawApiKey             = "api-key",
                SharedMappingPath     = null,
                BomKeyword            = "inventree",
                WaitForAutoPartNumber = true,
            };
        }
    }
}
