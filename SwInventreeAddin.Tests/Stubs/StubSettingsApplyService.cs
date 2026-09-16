using System;
using System.Net.Http;
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
        public HttpClient? LastTestClient { get; private set; }
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

            return Task.FromResult(ResultToReturnOnApply);
        }

        public Task<ConnectionProbeResult> TestConnectionAsync(SettingsApplyInput input, HttpClient client)
        {
            LastInput = input;
            LastTestClient = client;

            if (ExceptionToThrowOnTestConnection != null)
                throw ExceptionToThrowOnTestConnection;

            return Task.FromResult(ResultToReturnOnTestConnection);
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
