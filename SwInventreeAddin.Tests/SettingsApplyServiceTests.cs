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

            var result = await service.TestConnectionAsync(CreateInput(), client, CancellationToken.None);

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

            await service.TestConnectionAsync(CreateInput(), client, CancellationToken.None);

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

            var result = await service.TestConnectionAsync(CreateInput(), client, CancellationToken.None);

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

            var result = await service.TestConnectionAsync(CreateInput(), client, CancellationToken.None);

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
                () => service.TestConnectionAsync(input, OkClient(), CancellationToken.None));
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
                () => service.TestConnectionAsync(input, OkClient(), CancellationToken.None));
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
                () => service.TestConnectionAsync(input, OkClient(), CancellationToken.None));
        }

        [Test]
        public void TestConnectionAsync_WhenClientIsNull_ThrowsArgumentNullException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            Assert.ThrowsAsync<ArgumentNullException>(
                () => service.TestConnectionAsync(CreateInput(), null!, CancellationToken.None));
        }

        // ── Bounded probe + caller lifecycle (#234) ─────────────────────
        // The service bounds every probe to ~4 s internally; the caller's
        // token is a lifecycle signal only — its cancellation propagates
        // unclassified, while a timeout surfaces as Unreachable.

        [Test]
        public async Task TestConnectionAsync_WhenProbeExceedsInternalBound_ReturnsUnreachableWithTimeoutWording()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            // The handler answers only when its request token is cancelled —
            // with the caller token None, only the service's own bound can end it.
            using var client = new HttpClient(new AwaitsCancellationHandler());

            var probe = service.TestConnectionAsync(CreateInput(), client, CancellationToken.None);
            var finished = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.That(finished, Is.SameAs(probe),
                "the internal probe bound must fire well inside 10 s");

            var result = await probe;
            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Unreachable));
            Assert.That(result.Message, Does.Contain("timed out"));
        }

        [Test]
        public async Task TestConnectionAsync_WhenCallerCancelsMidRequest_PropagatesOperationCanceled()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            using var client = new HttpClient(new AwaitsCancellationHandler());
            using var cts = new CancellationTokenSource();

            var probe = service.TestConnectionAsync(CreateInput(), client, cts.Token);
            cts.Cancel();

            var finished = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.That(finished, Is.SameAs(probe),
                "caller cancellation must end the probe promptly");
            Assert.CatchAsync<OperationCanceledException>(() => probe);
        }

        [Test]
        public void TestConnectionAsync_WhenCallerTokenAlreadyCancelled_PropagatesOperationCanceled()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                () => service.TestConnectionAsync(CreateInput(), client, cts.Token));
        }

        // ── URL-only save (#238) ────────────────────────────────────
        // A URL with no credential is a valid persisted state — "server
        // only" on the configuration axis. Apply saves it and reports the
        // missing credential as the outcome instead of throwing.

        [Test]
        public async Task ApplyAsync_WhenNoCredential_PersistsUrlAndReportsCredentialNeeded()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.RawApiKey = string.Empty;

            var result = await service.ApplyAsync(input, client);

            Assert.Multiple(() =>
            {
                Assert.That(configProvider.LastSavedConfig, Is.Not.Null,
                    "the URL-only save must persist");
                Assert.That(configProvider.LastSavedConfig!.Url,
                            Is.EqualTo("https://example.com"));
                Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.Empty);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.CredentialRejected));
                Assert.That(result.Message, Does.Contain("credential").IgnoreCase);
                Assert.That(handler.LastRequest, Is.Null,
                    "no credential to probe with — the save must not hit the network");
            });
        }

        // ── Clearing the server URL (#253) ────────────────────────────
        // An empty URL is a legal save — "clear the saved server". The
        // record persists with an empty URL and the previously saved key,
        // input credentials are ignored, and nothing is probed.

        [Test]
        public async Task ApplyAsync_WhenUrlCleared_PersistsEmptyUrlAndKeepsSavedKey()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = string.Empty;
            input.RawApiKey = string.Empty;

            var result = await service.ApplyAsync(input, OkClient());

            Assert.Multiple(() =>
            {
                Assert.That(configProvider.LastSavedConfig, Is.Not.Null,
                    "a cleared URL still persists the record");
                Assert.That(configProvider.LastSavedConfig!.Url, Is.Empty);
                Assert.That(configProvider.LastSavedConfig.ApiKey,
                            Is.EqualTo("saved-key"), "the saved key survives a URL clear");
                Assert.That(configProvider.LastSavedConfig.IsConfigured, Is.False);
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotConfigured));
                Assert.That(result.Succeeded, Is.False);
            });
        }

        [Test]
        public async Task ApplyAsync_WhenUrlCleared_IgnoresTypedCredentialsAndSkipsProbe()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "should-not-be-used" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.Url = "   ";
            input.Username = "user";
            input.Password = "pass";
            input.RawApiKey = "typed-key";

            await service.ApplyAsync(input, client);

            Assert.Multiple(() =>
            {
                Assert.That(tokenService.LastUrl, Is.Null,
                    "no server to resolve against — the token service must not run");
                Assert.That(handler.LastRequest, Is.Null,
                    "a cleared URL never probes");
                Assert.That(configProvider.LastSavedConfig!.ApiKey,
                            Is.EqualTo("saved-key"),
                            "the saved key wins — a typed credential on a clear save is not resolved");
            });
        }

        [Test]
        public async Task ApplyAsync_WhenUrlCleared_PersistsMappingFieldsFromInput()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key");
            configProvider.Config!.MappingSourcePath = "\\\\share\\old.json";

            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = string.Empty;
            input.RawApiKey = string.Empty;
            input.SharedMappingPath = "\\\\share\\mapping.json";
            input.BomKeyword = "custom-bom";
            input.WaitForServerAssignedIpn = false;

            await service.ApplyAsync(input, OkClient());

            var saved = configProvider.LastSavedConfig!;
            Assert.Multiple(() =>
            {
                Assert.That(saved.MappingSourcePath, Is.EqualTo("\\\\share\\mapping.json"));
                Assert.That(saved.BomKeyword, Is.EqualTo("custom-bom"));
                Assert.That(saved.WaitForServerAssignedIpn, Is.False);
            });
        }

        [Test]
        public async Task ApplyAsync_WhenUrlClearedWithNothingSaved_PersistsEmptyKey()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = string.Empty;
            input.RawApiKey = string.Empty;

            var result = await service.ApplyAsync(input, OkClient());

            Assert.Multiple(() =>
            {
                Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.Empty);
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotConfigured));
            });
        }

        [Test]
        public void ApplyAsync_WhenUrlClearedAndProviderReadFails_ThrowsSettingsApplyException()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key")
            {
                ThrowOnGet = new InvalidOperationException("read failed"),
            };
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = string.Empty;

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("Failed to save server settings"));
                Assert.That(ex.Message, Does.Contain("read failed"),
                    "the provider read on the clear path is wrapped like any other pre-persistence failure");
                Assert.That(configProvider.LastSavedConfig, Is.Null);
            });
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

        // ── Probe skip (#249) ───────────────────────────────────────────
        // A save that changed nothing connection-relevant still validates,
        // resolves, and persists — but returns the no-verdict NotProbed
        // result instead of paying probe latency.

        [Test]
        public async Task ApplyAsync_WhenProbeConnectionFalse_PersistsWithoutProbing()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
            using var client = new HttpClient(handler);

            var input = CreateInput();
            input.ProbeConnection = false;

            var result = await service.ApplyAsync(input, client);

            Assert.Multiple(() =>
            {
                Assert.That(configProvider.LastSavedConfig, Is.Not.Null,
                    "the save still persists");
                Assert.That(configProvider.LastSavedConfig!.ApiKey, Is.EqualTo("api-key"));
                Assert.That(handler.LastRequest, Is.Null,
                    "no probe request is issued");
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotProbed));
                Assert.That(result.Succeeded, Is.False);
            });
        }

        [Test]
        public void ApplyAsync_WhenProbeConnectionFalse_StillValidatesBeforePersisting()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = "http://example.com";
            input.ProbeConnection = false;

            var ex = Assert.ThrowsAsync<SettingsApplyException>(
                () => service.ApplyAsync(input, OkClient()));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("https://"),
                    "the skip is post-persistence only — validation still runs");
                Assert.That(configProvider.LastSavedConfig, Is.Null);
            });
        }

        [Test]
        public async Task ApplyAsync_WhenProbeConnectionFalseAndUrlCleared_StillReportsNotConfigured()
        {
            var configProvider = new StubConfigProvider("https://example.com", "saved-key");
            var tokenService = new StubInventreeTokenService { TokenToReturn = "token" };
            var service = new SettingsApplyService(configProvider, tokenService);

            var input = CreateInput();
            input.Url = string.Empty;
            input.ProbeConnection = false;

            var result = await service.ApplyAsync(input, OkClient());

            Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotConfigured),
                "the URL-clear path is unchanged by the skip flag");
        }

        // The stub mirrors the skip so window- and VM-level tests cross the
        // same apply-seam contract as the real service.
        [Test]
        public async Task StubApplyService_WhenProbeConnectionFalse_PersistsAndReturnsNotProbed()
        {
            var configProvider = new StubConfigProvider("https://example.com", "key");
            var stub = new StubSettingsApplyService(configProvider);

            var input = CreateInput();
            input.ProbeConnection = false;

            var result = await stub.ApplyAsync(input, OkClient());

            Assert.Multiple(() =>
            {
                Assert.That(configProvider.LastSavedConfig, Is.Not.Null,
                    "the stub still persists through its provider");
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.NotProbed));
                Assert.That(result.Succeeded, Is.False);
            });
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

        // Answers only when the request token is cancelled — the only way out is
        // the service's own probe bound or the caller's lifecycle token.
        private sealed class AwaitsCancellationHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }
    }
}
