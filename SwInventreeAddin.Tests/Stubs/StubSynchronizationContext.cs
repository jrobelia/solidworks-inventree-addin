using System.Threading;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// SynchronizationContext that counts Send/Post calls and executes callbacks inline.
    /// Useful for verifying that ViewModels marshal UI-bound updates through the captured context.
    /// </summary>
    public sealed class StubSynchronizationContext : SynchronizationContext
    {
        public int SendCount { get; private set; }
        public int PostCount { get; private set; }

        public override void Send(SendOrPostCallback d, object? state)
        {
            SendCount++;
            d(state);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }
    }
}
