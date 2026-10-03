using System;
using System.Threading.Tasks;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// Evaluates whether a BOM Compare can proceed given the current Task Pane state.
    /// Encapsulates the pre-flight rules that gate the BOM Compare workflow:
    /// BOM table existence for the configured keyword, a populated Part Sync
    /// session, the PK-stamped-in-document check, the fetched part's Assembly
    /// flag, four-way revision comparison, and the IPN and Qty BOM Column
    /// Aliases.
    /// </summary>
    /// <remarks>
    /// All state reads go through <see cref="IBomReadinessContext"/> — one
    /// coherent capture per check — and the only command is the explicit
    /// push-revision operation. The check evaluates the existing session only
    /// and never fetches: Compare BOM is session-gated, so a session-absent
    /// snapshot is the dropped-session race, not a fetch trigger. SolidWorks
    /// is never read on a post-await continuation: the context marshals the
    /// capture onto the host STA thread.
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
        /// Runs all pre-flight checks over a single snapshot and returns the
        /// readiness outcome. Evaluates the existing session only — never fetches.
        /// </summary>
        public Task<BomCompareReadiness> CheckAsync()
        {
            var snapshot = _context.CaptureSnapshot();

            BomCompareReadiness Result(BomCompareOutcome outcome) =>
                new BomCompareReadiness(
                    outcome,
                    snapshot.Ipn,
                    snapshot.SwRevision.Trim(),
                    snapshot.FetchedRevision.Trim());

            // If there is no SolidWorks BOM table for the configured keyword,
            // there is nothing to compare.
            if (!_bomService.HasBomTable(_bomKeyword))
                return Task.FromResult(Result(BomCompareOutcome.BomTableMissing));

            // A session-absent snapshot means the session dropped between
            // button enablement and this check — a stop, never a fetch trigger.
            if (snapshot.InMemoryPartPk == 0)
                return Task.FromResult(Result(BomCompareOutcome.SessionNotPopulated));

            // PK must be stamped in the SolidWorks Document Properties.
            if (string.IsNullOrWhiteSpace(snapshot.StampedPkText))
                return Task.FromResult(Result(BomCompareOutcome.PkNotStamped));

            // A part that cannot hold a BOM fails before any revision
            // question — compare-open against it is meaningless and the
            // push would be refused afterwards anyway.
            if (!snapshot.FetchedPartIsAssembly)
                return Task.FromResult(Result(BomCompareOutcome.PartNotAssembly));

            var revOrder = RevisionComparer.Compare(
                snapshot.SwRevision.Trim(), snapshot.FetchedRevision.Trim());

            return Task.FromResult(revOrder switch
            {
                RevisionOrder.ItIsNewer => Result(BomCompareOutcome.ItIsNewer),
                RevisionOrder.Ambiguous => Result(BomCompareOutcome.Ambiguous),
                RevisionOrder.SwIsNewer => Result(BomCompareOutcome.SwIsNewer),
                _ => snapshot.Mapping.GetMissingBomCompareAliases().Count > 0
                        ? Result(BomCompareOutcome.BomColumnAliasesMissing)
                        : Result(BomCompareOutcome.Ready),
            });
        }

        /// <summary>
        /// Pushes the SolidWorks revision to InvenTree. Call only when the caller has
        /// confirmed a <see cref="BomCompareOutcome.SwIsNewer"/> result.
        /// </summary>
        public Task<PartSyncResult> PushRevisionAsync() => _context.PushRevisionAsync();
    }
}
