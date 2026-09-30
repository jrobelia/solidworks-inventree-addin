using System;

namespace SwInventreeAddin
{
    /// <summary>
    /// Routes work onto the host STA thread — the SolidWorks/UI thread the
    /// add-in was constructed on. Implementations: inline on the host thread,
    /// synchronous <c>Send</c> off it, inline when no context was captured
    /// (unit tests). "Host thread" is a managed-thread-id check per ADR-0002 —
    /// never a <see cref="System.Threading.SynchronizationContext"/> comparison.
    /// </summary>
    public interface IHostStaDispatcher
    {
        /// <summary>True when called on the thread the dispatcher was captured on.</summary>
        bool IsOnHostThread { get; }

        /// <summary>
        /// Runs <paramref name="action"/> on the host thread. Inline when already
        /// there or when no context was captured; a synchronous Send otherwise —
        /// callers observe the action's effects as soon as Run returns.
        /// </summary>
        void Run(Action action);
    }
}
