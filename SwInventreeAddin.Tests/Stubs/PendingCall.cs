using System;
using System.Threading.Tasks;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// A deferred asynchronous call captured by a stub. The stub returns
    /// <see cref="Task"/> to the caller incomplete; the test decides when — and
    /// in what order — calls resolve via <see cref="Complete"/> or
    /// <see cref="Fault"/>. Continuations always run asynchronously so
    /// completing a call never runs consumer code on the test's thread.
    /// </summary>
    public sealed class PendingCall<TRequest, TResult>
    {
        private readonly TaskCompletionSource<TResult> _completion =
            new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingCall(TRequest request) => Request = request;

        /// <summary>The request argument the stub was called with.</summary>
        public TRequest Request { get; }

        /// <summary>The task the stub returned for this call; incomplete until completed or faulted.</summary>
        public Task<TResult> Task => _completion.Task;

        /// <summary>Resolves <see cref="Task"/> with <paramref name="result"/>.</summary>
        public void Complete(TResult result) => _completion.SetResult(result);

        /// <summary>Faults <see cref="Task"/> with <paramref name="exception"/>.</summary>
        public void Fault(Exception exception) => _completion.SetException(exception);
    }
}
