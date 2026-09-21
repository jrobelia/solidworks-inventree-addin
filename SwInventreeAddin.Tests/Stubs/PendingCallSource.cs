using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// Owns the deferred-completion surface for one stubbed endpoint: while
    /// <see cref="Defer"/> is set, <see cref="Capture"/> appends a
    /// <see cref="PendingCall{TRequest, TResult}"/> to <see cref="Calls"/> and
    /// returns its incomplete task, so the test controls when — and in what
    /// order — calls resolve.
    /// </summary>
    public sealed class PendingCallSource<TRequest, TResult>
    {
        /// <summary>When true, calls are captured as pending instead of completing immediately.</summary>
        public bool Defer { get; set; }

        /// <summary>The pending calls captured while <see cref="Defer"/> was set, in request order.</summary>
        public List<PendingCall<TRequest, TResult>> Calls { get; }
            = new List<PendingCall<TRequest, TResult>>();

        /// <summary>
        /// Captures a pending call for <paramref name="request"/> and returns its
        /// incomplete task. Returns null when <see cref="Defer"/> is not set, so
        /// callers fall through to their immediate-completion path.
        /// </summary>
        public Task<TResult>? Capture(TRequest request)
        {
            if (!Defer) return null;
            var call = new PendingCall<TRequest, TResult>(request);
            Calls.Add(call);
            return call.Task;
        }
    }
}
