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
            Assert.That(configProvider.LastSavedConfig, Is.Null);
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

        // ── Probe outcome reporting (#232) ──────────────────────────────
        // Apply persists first, then probes: a failed probe comes back as a
        // ConnectionProbeResult instead of throwing, and the save is kept.

        [Test]
        public async Task ApplyAsync_WhenProbeSucceeds_ReturnsConnected()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var result = await service.ApplyAsync(CreateInput(), OkClient());

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Connected));
            Assert.That(result.Succeeded, Is.True);
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        public async Task ApplyAsync_WhenProbeRejectsCredential_PersistsAndReportsCredentialRejected(
            HttpStatusCode statusCode)
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(statusCode, "denied");
            using var client = new HttpClient(handler);

            var result = await service.ApplyAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.CredentialRejected));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("api-key"));
        }

        [Test]
        public async Task ApplyAsync_WhenProbeReturnsOtherError_PersistsAndReportsServerError()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
            using var client = new HttpClient(handler);

            var result = await service.ApplyAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.ServerError));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("500"));
            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
        }

        [Test]
        public async Task ApplyAsync_WhenServerUnreachable_PersistsAndReportsUnreachable()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new FailingHttpMessageHandler(new HttpRequestException("connection refused"));
            using var client = new HttpClient(handler);

            var result = await service.ApplyAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Unreachable));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("Could not reach"));
            Assert.That(configProvider.LastSavedConfig, Is.Not.Null);
            Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("api-key"));
        }

        [Test]
        public async Task ApplyAsync_WhenProbeFails_MessageDoesNotContainTheApiKey()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "denied");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.RawApiKey = "inv-secret-456";

            var result = await service.ApplyAsync(input, client);

            Assert.That(result.Message, Does.Not.Contain("inv-secret-456"));
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
        public void ApplyAsync_WhenUrlIsNotAValidUri_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "https://";

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
            Assert.That(configProvider.LastSavedConfig, Is.Null);
        }

        // ── TestConnectionAsync (#232) ──────────────────────────────────
        // Test Connection resolves credentials and probes the draft values
        // exactly like Apply, but never persists.

        [Test]
        public async Task TestConnectionAsync_WhenServerReturnsOk_ReturnsConnected()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var result = await service.TestConnectionAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Connected));
            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public async Task TestConnectionAsync_WhenServerReturnsOk_DoesNotPersist()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            await service.TestConnectionAsync(CreateInput(), client);

            Assert.That(configProvider.LastSavedConfig, Is.Null);
        }

        [Test]
        public async Task TestConnectionAsync_WhenServerRejectsCredential_ReturnsCredentialRejected()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.Unauthorized, "Unauthorized");
            using var client = new HttpClient(handler);

            var result = await service.TestConnectionAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.CredentialRejected));
            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public async Task TestConnectionAsync_WhenHttpRequestThrows_ReturnsUnreachable()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new FailingHttpMessageHandler(new HttpRequestException("connection refused"));
            using var client = new HttpClient(handler);

            var result = await service.TestConnectionAsync(CreateInput(), client);

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Unreachable));
            Assert.That(result.Message, Does.Contain("Could not reach"));
        }

        [Test]
        public void TestConnectionAsync_WhenUrlIsNotAValidUri_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "https://";

            Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(input, OkClient()));
        }

        [Test]
        public void TestConnectionAsync_WhenCredentialMissing_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.RawApiKey = string.Empty;

            Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(input, OkClient()));
        }

        [Test]
        public void TestConnectionAsync_WhenTokenResolutionFails_ThrowsInvalidOperationException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService(); // configured to fail
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Username = "user";
            input.Password = "pass";

            Assert.ThrowsAsync<InvalidOperationException>(
                () => service.TestConnectionAsync(input, OkClient()));
        }

        [Test]
        public void TestConnectionAsync_WhenClientIsNull_ThrowsArgumentNullException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            Assert.ThrowsAsync<ArgumentNullException>(
                () => service.TestConnectionAsync(CreateInput(), null!));
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

        // ── RemoveApiKeyAsync (#232) ────────────────────────────────────
        // Remove clears only the credential: the saved URL, Property Mapping
        // path, BOM keyword, and IPN flag all survive.

        [Test]
        public async Task RemoveApiKeyAsync_ClearsOnlyTheApiKey()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key");
            configProvider.Config!.MappingSourcePath = "\\\\share\\mapping.json";
            configProvider.Config.BomKeyword = "custom-bom";
            configProvider.Config.WaitForServerAssignedIpn = false;

            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            await service.RemoveApiKeyAsync();

            var saved = configProvider.LastSavedConfig;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.ApiKey, Is.EqualTo(string.Empty));
            Assert.That(saved.Url, Is.EqualTo("https://example.com"));
            Assert.That(saved.MappingSourcePath, Is.EqualTo("\\\\share\\mapping.json"));
            Assert.That(saved.BomKeyword, Is.EqualTo("custom-bom"));
            Assert.That(saved.WaitForServerAssignedIpn, Is.False);
            Assert.That(configProvider.DeleteCallCount, Is.EqualTo(0));
            Assert.That(configProvider.Config!.ApiKey, Is.EqualTo(string.Empty));
        }

        [Test]
        public void RemoveApiKeyAsync_WhenNothingSaved_IsANoOp()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            Assert.DoesNotThrowAsync(() => service.RemoveApiKeyAsync());

            Assert.That(configProvider.LastSavedConfig, Is.Null);
            Assert.That(configProvider.DeleteCallCount, Is.EqualTo(0));
        }

        [Test]
        public void RemoveApiKeyAsync_WhenProviderThrows_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key")
            {
                ThrowOnSave = new InvalidOperationException("write failed"),
            };
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.RemoveApiKeyAsync());

            Assert.That(ex!.Message, Does.Contain("Failed to remove the API key"));
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
