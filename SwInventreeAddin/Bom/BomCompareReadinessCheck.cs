using System;
using System.Threading.Tasks;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// Evaluates whether a BOM Compare can proceed given the current Task Pane state.
    /// Encapsulates the pre-flight rules that gate the BOM Compare workflow:
    /// BOM table existence for the configured keyword, the IPN and Qty BOM Column Aliases,
    /// auto-fetch if the InvenTree PK is not yet in memory, PK-in-memory check,
    /// PK-stamped-in-document check, and four-way revision comparison.
    /// </summary>
    /// <remarks>
    /// All state reads go through <see cref="IBomReadinessContext"/> snapshots —
    /// one coherent capture per rule stage — and the only commands are the
    /// explicit ensure-populated and push-revision operations. SolidWorks is
    /// never read on a post-await continuation: the context marshals the
    /// re-capture onto the host STA thread.
    /// </remarks>
    internal sealed class BomCompareReadinessCheck
    {
        private readonly IBomReadinessContext _context;
        private readonly IAssemblyBomService _bomService;
        private readonly string _bomKeyword;

        public BomCompareReadinessCheck(
            IBomReadinessContext context,
            IAssemblyBomService bomService,
            string bomKeyword)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _bomService = bomService ?? throw new ArgumentNullException(nameof(bomService));
            _bomKeyword = bomKeyword ?? throw new ArgumentNullException(nameof(bomKeyword));
        }

        /// <summary>
        /// Runs all pre-flight checks and returns the readiness outcome.
        /// Automatically fetches from InvenTree if the PK is not yet held in memory.
        /// </summary>
        public async Task<BomCompareReadiness> CheckAsync()
        {
            var snapshot = _context.CaptureSnapshot();

            BomCompareReadiness Result(BomCompareOutcome outcome, PartSyncResult? fetchResult = null) =>
                new BomCompareReadiness(
                    outcome,
                    snapshot.Ipn,
                    snapshot.SwRevision.Trim(),
                    snapshot.FetchedRevision.Trim(),
                    fetchResult);

            // If there is no SolidWorks BOM table for the configured keyword, there is
            // nothing to compare and we should not incur an InvenTree round-trip.
            if (!_bomService.HasBomTable(_bomKeyword))
                return Result(BomCompareOutcome.BomTableMissing);

            // Auto-fetch if we don't already have the PK in memory.
            if (snapshot.InMemoryPartPk == 0)
            {
                var ensure = await _context.EnsurePartPopulatedAsync().ConfigureAwait(false);
                if (IsConfirmationOutcome(ensure.Outcome))
                    return Result(BomCompareOutcome.FetchConfirmationRequired, ensure);
            }

            // Re-capture: the ensure may have installed a session or stamped
            // values — never read SolidWorks on this continuation directly.
            snapshot = _context.CaptureSnapshot();

            if (snapshot.InMemoryPartPk == 0)
                return Result(BomCompareOutcome.PkNotFound);

            // PK must be stamped in the SolidWorks Document Properties.
            if (string.IsNullOrWhiteSpace(snapshot.StampedPkText))
                return Result(BomCompareOutcome.PkNotStamped);

            var revOrder = RevisionComparer.Compare(
                snapshot.SwRevision.Trim(), snapshot.FetchedRevision.Trim());

            return revOrder switch
            {
                RevisionOrder.ItIsNewer => Result(BomCompareOutcome.ItIsNewer),
                RevisionOrder.Ambiguous => Result(BomCompareOutcome.Ambiguous),
                RevisionOrder.SwIsNewer => Result(BomCompareOutcome.SwIsNewer),
                _ => snapshot.Mapping.GetMissingBomCompareAliases().Count > 0
                        ? Result(BomCompareOutcome.BomColumnAliasesMissing)
                        : Result(BomCompareOutcome.Ready),
            };
        }

        /// <summary>
        /// Pushes the SolidWorks revision to InvenTree. Call only when the caller has
        /// confirmed a <see cref="BomCompareOutcome.SwIsNewer"/> result.
        /// </summary>
        public Task<PartSyncResult> PushRevisionAsync() => _context.PushRevisionAsync();

        private static bool IsConfirmationOutcome(PartSyncOutcome outcome) =>
            outcome == PartSyncOutcome.DuplicateIpnConfirmation
            || outcome == PartSyncOutcome.LinkMismatchConfirmation
            || outcome == PartSyncOutcome.MissingPropertyConfirmation;
    }
}
