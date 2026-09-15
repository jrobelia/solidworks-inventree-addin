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
            var service = new SettingsApplyService(configProvider, tokenService);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(CreateInput(), OkClient()));

            Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenResolutionFails_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService(); // configured to fail
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenSucceeds_SavesConfigWithResolvedToken()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "resolved-token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            await service.ApplyAsync(input, OkClient());

            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("resolved-token"));
        }

        [Test]
        public async Task ApplyAsync_WhenTokenSucceeds_ProbesPartEndpointWithResolvedToken()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "resolved-token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            await service.ApplyAsync(input, client);

            Assert.That(handler.LastRequest.RequestUri!.ToString(),
                        Is.EqualTo("https://example.com/api/part/?limit=1"));
            Assert.That(handler.LastRequest.Headers.Authorization!.ToString(),
                        Is.EqualTo("Token resolved-token"));
        }

        [Test]
        public async Task ApplyAsync_WhenRawApiKeyProvided_SavesConfigWithoutCallingTokenService()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "should-not-be-used" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.RawApiKey = "raw-key";

            await service.ApplyAsync(input, OkClient());

            Assert.That(tokenService.LastUrl, Is.Null);
            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("raw-key"));
        }

        [Test]
        public async Task ApplyAsync_WhenRawApiKeyProvided_ProbesWithRawKey()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "should-not-be-used" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.RawApiKey = "raw-key";

            await service.ApplyAsync(input, client);

            Assert.That(handler.LastRequest.Headers.Authorization!.ToString(),
                        Is.EqualTo("Token raw-key"));
        }

        [Test]
        public void ApplyAsync_WhenProbeReturnsError_ThrowsSettingsApplyExceptionAndDoesNotSave()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "Unauthorized");
            using var client = new HttpClient(handler);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(CreateInput(), client));

            Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
            Assert.That(configProvider.LastSavedConfig, Is.Null);
        }

        [Test]
        public void ApplyAsync_WhenServerUnreachable_ThrowsSettingsApplyExceptionAndDoesNotSave()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new FailingHttpMessageHandler(new HttpRequestException("connection refused"));
            using var client = new HttpClient(handler);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(CreateInput(), client));

            Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
            Assert.That(configProvider.LastSavedConfig, Is.Null);
        }

        [Test]
        public void ApplyAsync_WhenClientIsNull_ThrowsArgumentNullException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            Assert.ThrowsAsync<ArgumentNullException>(
                () => service.ApplyAsync(CreateInput(), null!));

            Assert.That(configProvider.LastSavedConfig, Is.Null);
        }

        [Test]
        public async Task ApplyAsync_WhenUrlIsHttp_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("http://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "http://example.com";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.That(ex!.Message, Does.Contain("https://"));
        }

        [Test]
        public async Task TestConnectionAsync_WhenServerReturnsOk_Completes()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            Assert.DoesNotThrowAsync(() => service.TestConnectionAsync(CreateInput(), client));
        }

        [Test]
        public void TestConnectionAsync_WhenServerReturnsError_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "Unauthorized");
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(CreateInput(), client));

            Assert.That(ex!.Message, Does.Contain("Server responded"));
        }

        [Test]
        public void TestConnectionAsync_WhenHttpRequestThrows_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new FailingHttpMessageHandler(new HttpRequestException("connection refused"));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(CreateInput(), client));

            Assert.That(ex!.Message, Does.Contain("Could not reach"));
        }

        [Test]
        public void ApplyAsync_WithNoCredentials_ThrowsMessageNamingTheApiKeyMode()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.RawApiKey = string.Empty;

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.That(ex!.Message, Does.Contain("API key").And.Not.Contain("Advanced"));
        }

        // ── RemoveServerConfigAsync (#213) ──────────────────────────────────

        [Test]
        public async Task RemoveServerConfigAsync_DelegatesToConfigProvider()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            await service.RemoveServerConfigAsync();

            Assert.That(configProvider.DeleteCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RemoveServerConfigAsync_WhenNothingSaved_Completes()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            Assert.DoesNotThrowAsync(() => service.RemoveServerConfigAsync());

            Assert.That(configProvider.DeleteCallCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveServerConfigAsync_WhenProviderThrows_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key")
            {
                ThrowOnDelete = new InvalidOperationException("delete failed"),
            };
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.RemoveServerConfigAsync());

            Assert.That(ex!.Message, Does.Contain("Failed to remove server settings"));
        }

        private static HttpClient OkClient() =>
            new HttpClient(new StubHttpMessageHandler(HttpStatusCode.OK, "[]"));

        private static SettingsApplyInput CreateInput()
        {
            return new SettingsApplyInput
            {
                Url = "https://example.com",
                RawApiKey = "api-key",
                SharedMappingPath = null,
                BomKeyword = "inventree",
                WaitForServerAssignedIpn = true,
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
