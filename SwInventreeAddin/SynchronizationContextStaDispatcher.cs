using System;
using System.Threading;

namespace SwInventreeAddin
{
    /// <summary>
    /// Production <see cref="IHostStaDispatcher"/>: captures
    /// <see cref="SynchronizationContext.Current"/> and the constructing
    /// managed thread id. Constructed on the SolidWorks UI thread inside
    /// <c>TaskPaneControl</c>; ADR-0002's managed-thread-id rule applies —
    /// inside a Dispatcher.Invoke callback the ambient context is a fresh
    /// wrapper that never reference-equals the captured one, so "already on
    /// the host thread" is a thread check, not a context check.
    /// </summary>
    public sealed class SynchronizationContextStaDispatcher : IHostStaDispatcher
    {
        private readonly SynchronizationContext? _context;
        private readonly int _hostThreadId;

        public SynchronizationContextStaDispatcher()
        {
            _context = SynchronizationContext.Current;
            _hostThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsOnHostThread => Environment.CurrentManagedThreadId == _hostThreadId;

        public void Run(Action action)
        {
            if (IsOnHostThread || _context == null)
                action();
            else
                _context.Send(_ => action(), null);
        }
    }
}
