using System;
using System.Collections.Generic;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// Test <see cref="IHostStaDispatcher"/>: runs work inline by default —
    /// callers observe effects as soon as Run returns, matching production
    /// semantics on the host thread. Set <see cref="DeferRun"/> to park work
    /// in a queue instead, so a test can make document/client/mapping changes
    /// while a coordinator commit is parked, then release the queue and prove
    /// the commit revalidates against the newer state.
    /// </summary>
    public sealed class StubHostStaDispatcher : IHostStaDispatcher
    {
        private readonly Queue<Action> _queue = new Queue<Action>();

        /// <summary>True for every thread — tests treat the caller as the host.</summary>
        public bool IsOnHostThread => true;

        /// <summary>When true, <see cref="Run"/> enqueues work instead of executing it.</summary>
        public bool DeferRun { get; set; }

        /// <summary>Number of parked actions awaiting <see cref="RunNext"/>/<see cref="RunAll"/>.</summary>
        public int QueuedCount => _queue.Count;

        public void Run(Action action)
        {
            if (DeferRun)
                _queue.Enqueue(action);
            else
                action();
        }

        /// <summary>Executes the oldest parked action; no-op when the queue is empty.</summary>
        public void RunNext()
        {
            if (_queue.Count > 0)
                _queue.Dequeue()();
        }

        /// <summary>Executes every parked action in order.</summary>
        public void RunAll()
        {
            while (_queue.Count > 0)
                _queue.Dequeue()();
        }
    }
}
