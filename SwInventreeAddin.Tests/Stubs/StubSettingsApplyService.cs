using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubSettingsApplyService : ISettingsApplyService
    {
        public StubSettingsApplyService(IConfigProvider? configProvider = null)
        {
            ConfigProvider = configProvider;
        }

        public SettingsApplyInput? LastInput { get; private set; }
        public HttpClient? LastApplyClient { get; private set; }
        public SettingsApplyInput? LastTestInput { get; private set; }
        public HttpClient? LastTestClient { get; private set; }
        public CancellationToken LastTestToken { get; private set; }
        public int TestCallCount { get; private set; }
        public int RemoveCallCount { get; private set; }

        public Exception? ExceptionToThrowOnApply { get; set; }
        public Exception? ExceptionToThrowOnTestConnection { get; set; }
        public Exception? ExceptionToThrowOnRemove { get; set; }

        /// <summary>
        /// The probe outcome returned when ApplyAsync does not throw. Defaults to
        /// a successful connection.
        /// </summary>
        public ConnectionProbeResult ResultToReturnOnApply { get; set; } = ConnectedResult();

        /// <summary>
        /// The probe outcome returned when TestConnectionAsync does not throw.
        /// Defaults to a successful connection.
        /// </summary>
        public ConnectionProbeResult ResultToReturnOnTestConnection { get; set; } = ConnectedResult();

        /// <summary>
        /// When set, TestConnectionAsync holds the probe open until this source
        /// completes or the caller's token is cancelled — mirroring the real
        /// service honouring the lifecycle token — so a test can keep a probe
        /// in flight across window events.
        /// </summary>
        public TaskCompletionSource<ConnectionProbeResult>? PendingTestResult { get; set; }

        /// <summary>
        /// Deliberate contract violation for exercising guard clauses: when
        /// true, TestConnectionAsync ignores the caller's cancellation token
        /// and keeps waiting on <see cref="PendingTestResult"/>, so a test can
        /// deliver a normal verdict after the caller cancelled — e.g. the
        /// SettingsWindow guard that discards an open-probe result landing
        /// after Close. The real service always honours the token, so leave
        /// this false unless a test exists to prove a guard discards the late
        /// result. Meaningful only when PendingTestResult is set.
        /// </summary>
        public bool IgnoreCallerCancellation { get; set; }

        /// <summary>
        /// Optional provider the remove call delegates to, mirroring the real
        /// service's read-modify-write, so UI tests can observe the
        /// cleared-credential post-state.
        /// </summary>
        public IConfigProvider? ConfigProvider { get; }

        public Task<ConnectionProbeResult> ApplyAsync(SettingsApplyInput input, HttpClient client)
        {
            LastInput = input;
            LastApplyClient = client;

            if (ExceptionToThrowOnApply != null)
                throw ExceptionToThrowOnApply;

            // Mirror the real service's persist-then-probe contract: a normal
            // return means the settings were saved, so a re-read of the provider
            // must observe them. A complete username+password pair resolves to a
            // stand-in token, as the real token service would produce.
            bool clearing = string.IsNullOrWhiteSpace(input.Url);

            if (ConfigProvider != null)
            {
                string apiKey;
                if (clearing)
                {
                    // The clear path ignores input credentials — the saved key wins.
                    apiKey = ConfigProvider.GetServerConfig()?.ApiKey ?? string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(input.RawApiKey))
                {
                    apiKey = input.RawApiKey.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(input.Username) &&
                         !string.IsNullOrWhiteSpace(input.Password))
                {
                    apiKey = "stub-resolved-token";
                }
                else
                {
                    apiKey = ConfigProvider.GetServerConfig()?.ApiKey ?? string.Empty;
                }

                ConfigProvider.SaveServerConfig(new ServerConfig
                {
                    Url = input.Url.Trim(),
                    ApiKey = apiKey,
                    MappingSourcePath = input.SharedMappingPath,
                    BomKeyword = input.BomKeyword,
                    WaitForServerAssignedIpn = input.WaitForServerAssignedIpn,
                });
            }

            if (clearing)
            {
                return Task.FromResult(new ConnectionProbeResult(
                    ConnectionProbeStatus.NotConfigured,
                    "Server URL cleared — the saved API key was kept. Nothing was probed."));
            }

            // Mirror the real service's probe skip (#249): the input persisted
            // above, but no connection-relevant field changed — no verdict.
            if (!input.ProbeConnection)
            {
                return Task.FromResult(new ConnectionProbeResult(
                    ConnectionProbeStatus.NotProbed,
                    "Settings saved — the connection was not probed."));
            }

            return Task.FromResult(ResultToReturnOnApply);
        }

        public async Task<ConnectionProbeResult> TestConnectionAsync(
            SettingsApplyInput input, HttpClient client, CancellationToken cancellationToken)
        {
            LastInput = input;
            LastTestInput = input;
            LastTestClient = client;
            LastTestToken = cancellationToken;
            TestCallCount++;

            if (ExceptionToThrowOnTestConnection != null)
                throw ExceptionToThrowOnTestConnection;

            if (PendingTestResult != null)
            {
                // Misbehave knob: skip the cancellation race entirely so the
                // pending source's normal result is delivered even after the
                // caller's token has fired.
                if (IgnoreCallerCancellation)
                    return await PendingTestResult.Task.ConfigureAwait(false);

                var cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
                if (await Task.WhenAny(PendingTestResult.Task, cancelled).ConfigureAwait(false) == cancelled)
                    throw new OperationCanceledException();

                return await PendingTestResult.Task.ConfigureAwait(false);
            }

            return ResultToReturnOnTestConnection;
        }

        public Task RemoveApiKeyAsync()
        {
            RemoveCallCount++;

            if (ExceptionToThrowOnRemove != null)
                throw ExceptionToThrowOnRemove;

            var config = ConfigProvider?.GetServerConfig();
            if (config != null)
            {
                config.ApiKey = string.Empty;
                ConfigProvider!.SaveServerConfig(config);
            }

            return Task.CompletedTask;
        }

        private static ConnectionProbeResult ConnectedResult() =>
            new ConnectionProbeResult(ConnectionProbeStatus.Connected, "Connection successful.");
    }
}
