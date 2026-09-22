using System;
using System.Threading.Tasks;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// Production <see cref="IBomReadinessContext"/> over
    /// <see cref="IPartSyncCoordinator"/>: snapshot capture refreshes the
    /// document values through the coordinator's light refresh
    /// (<see cref="IPartSyncCoordinator.RefreshDocument"/>) marshalled onto
    /// the host STA thread, so it is callable from a thread-pool
    /// continuation — SolidWorks COM never runs off the STA thread. The
    /// light refresh (not the full <see cref="IPartSyncCoordinator.UpdateDocument"/>)
    /// is deliberate: revalidation would drop the session an ensure-fetch
    /// just installed on a document with no stamped PK.
    /// </summary>
    internal sealed class CoordinatorBomReadinessContext : IBomReadinessContext
    {
        private readonly IPartSyncCoordinator _coordinator;
        private readonly IHostStaDispatcher _dispatcher;

        public CoordinatorBomReadinessContext(
            IPartSyncCoordinator coordinator, IHostStaDispatcher dispatcher)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <inheritdoc/>
        public BomReadinessSnapshot CaptureSnapshot()
        {
            BomReadinessSnapshot? snapshot = null;
            _dispatcher.Run(() =>
            {
                _coordinator.RefreshDocument();
                var doc = _coordinator.Document;
                snapshot = new BomReadinessSnapshot(
                    ipn: doc?.Ipn ?? string.Empty,
                    inMemoryPartPk: _coordinator.FetchedPart?.Pk ?? 0,
                    stampedPkText: doc?.PkText ?? string.Empty,
                    swRevision: doc?.Revision ?? string.Empty,
                    fetchedRevision: _coordinator.FetchedPart?.Revision ?? string.Empty,
                    mapping: _coordinator.CurrentMapping);
            });
            return snapshot!;
        }

        /// <inheritdoc/>
        public Task<PartSyncResult> EnsurePartPopulatedAsync() =>
            _coordinator.EnsurePartPopulatedAsync();

        /// <inheritdoc/>
        public Task<PartSyncResult> PushRevisionAsync() =>
            _coordinator.PushAsync(PushField.Revision);
    }
}
