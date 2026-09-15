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
        /// Optional provider the remove call delegates to, mirroring the real
        /// service, so UI tests can observe the deleted-config post-state.
        /// </summary>
        public IConfigProvider? ConfigProvider { get; }

        public Task ApplyAsync(SettingsApplyInput input, HttpClient client)
        {
            LastInput = input;
            LastApplyClient = client;

            if (ExceptionToThrowOnApply != null)
                throw ExceptionToThrowOnApply;

            return Task.CompletedTask;
        }

        public Task TestConnectionAsync(SettingsApplyInput input, HttpClient client)
        {
            LastInput = input;
            LastTestClient = client;

            if (ExceptionToThrowOnTestConnection != null)
                throw ExceptionToThrowOnTestConnection;

            return Task.CompletedTask;
        }

        public Task RemoveServerConfigAsync()
        {
            RemoveCallCount++;

            if (ExceptionToThrowOnRemove != null)
                throw ExceptionToThrowOnRemove;

            ConfigProvider?.DeleteServerConfig();
            return Task.CompletedTask;
        }
    }
}
