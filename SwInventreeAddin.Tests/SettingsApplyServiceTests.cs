using System;
using System.Net;
using System.Net.Http;
using System.Threading;
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
        public async Task ApplyAsync_WhenConfigProviderThrows_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key")
            {
                ThrowOnSave = new InvalidOperationException("save failed"),
            };
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service      = new SettingsApplyService(configProvider, tokenService);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(CreateInput()));

            Assert.That(ex.Message, Does.Contain("Failed to save server settings"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenResolutionFails_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService(); // configured to fail
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input));

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
        public async Task ApplyAsync_WhenUrlIsHttp_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("http://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "http://example.com";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input));

            Assert.That(ex.Message, Does.Contain("https://"));
        }

        [Test]
        public async Task TestConnectionAsync_WhenServerReturnsOk_Completes()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            Assert.DoesNotThrowAsync(() => service.TestConnectionAsync(CreateInput(), client));
        }

        [Test]
        public void TestConnectionAsync_WhenServerReturnsError_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "Unauthorized");
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(CreateInput(), client));

            Assert.That(ex.Message, Does.Contain("Server responded"));
        }

        [Test]
        public void TestConnectionAsync_WhenHttpRequestThrows_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService   = new StubInventreeTokenService { TokenToReturn = "token" };
            var service        = new SettingsApplyService(configProvider, tokenService);

            var handler = new FailingHttpMessageHandler(new HttpRequestException("connection refused"));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(CreateInput(), client));

            Assert.That(ex.Message, Does.Contain("Could not reach"));
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

        private sealed class FailingHttpMessageHandler : HttpMessageHandler
        {
            private readonly Exception _exception;

            public FailingHttpMessageHandler(Exception exception)
            {
                _exception = exception;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw _exception;
            }
        }
    }
}
