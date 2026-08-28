using System;
using System.Threading.Tasks;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubSettingsApplyService : ISettingsApplyService
    {
        public string? TokenToReturn { get; set; } = "stub-token";
        public SettingsApplyInput? LastInput { get; private set; }

        public Exception? ExceptionToThrowOnApply { get; set; }
        public Exception? ExceptionToThrowOnResolve { get; set; }

        public Task<string> ResolveApiKeyAsync(SettingsApplyInput input)
        {
            LastInput = input;

            if (ExceptionToThrowOnResolve != null)
                throw ExceptionToThrowOnResolve;

            return Task.FromResult(TokenToReturn ?? string.Empty);
        }

        public Task ApplyAsync(SettingsApplyInput input)
        {
            LastInput = input;

            if (ExceptionToThrowOnApply != null)
                throw ExceptionToThrowOnApply;

            return Task.CompletedTask;
        }
    }
}
