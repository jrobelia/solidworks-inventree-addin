using System;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
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
    internal sealed class BomCompareReadinessCheck
    {
        private readonly IBomReadinessSource _source;
        private readonly IAssemblyBomService _bomService;
        private readonly string              _bomKeyword;

        public BomCompareReadinessCheck(
            IBomReadinessSource source,
            IAssemblyBomService bomService,
            string bomKeyword)
        {
            _source     = source     ?? throw new ArgumentNullException(nameof(source));
            _bomService = bomService ?? throw new ArgumentNullException(nameof(bomService));
            _bomKeyword = bomKeyword ?? throw new ArgumentNullException(nameof(bomKeyword));
        }

        /// <summary>
        /// Runs all pre-flight checks and returns the readiness outcome.
        /// Automatically fetches from InvenTree if the PK is not yet held in memory.
        /// </summary>
        public async Task<BomCompareReadiness> CheckAsync()
        {
            var partNumber = _source.PartNumber;
            var swRev      = _source.CurrentRevision?.Trim() ?? string.Empty;
            var itRev      = _source.RevisionPreview?.Trim() ?? string.Empty;

            BomCompareReadiness Result(BomCompareOutcome outcome) =>
                new BomCompareReadiness(outcome, partNumber, swRev, itRev);

            // If there is no SolidWorks BOM table for the configured keyword, there is
            // nothing to compare and we should not incur an InvenTree round-trip.
            if (!_bomService.HasBomTable(_bomKeyword))
                return Result(BomCompareOutcome.BomTableMissing);

            // Auto-fetch if we don't already have the PK in memory.
            if (_source.CurrentInvenTreePk == 0)
                await _source.FetchPartAsync().ConfigureAwait(false);

            if (_source.CurrentInvenTreePk == 0)
                return Result(BomCompareOutcome.PkNotFound);

            // PK must be stamped in the SolidWorks Document Properties.
            _source.RefreshCurrentProperties();
            if (string.IsNullOrWhiteSpace(_source.CurrentPk))
                return Result(BomCompareOutcome.PkNotStamped);

            // Re-read rev values after the refresh.
            swRev = _source.CurrentRevision?.Trim() ?? string.Empty;
            itRev = _source.RevisionPreview?.Trim()  ?? string.Empty;

            var revOrder = RevisionComparer.Compare(swRev, itRev);
            var mapping  = _source.CurrentMapping;

            return revOrder switch
            {
                RevisionOrder.ItIsNewer => Result(BomCompareOutcome.ItIsNewer),
                RevisionOrder.Ambiguous => Result(BomCompareOutcome.Ambiguous),
                RevisionOrder.SwIsNewer => Result(BomCompareOutcome.SwIsNewer),
                _ => string.IsNullOrWhiteSpace(mapping.BomColumnIpn)
                     || string.IsNullOrWhiteSpace(mapping.BomColumnQty)
                        ? Result(BomCompareOutcome.BomColumnAliasesMissing)
                        : Result(BomCompareOutcome.Ready),
            };
        }

        /// <summary>
        /// Pushes the SolidWorks revision to InvenTree. Call only when the caller has
        /// confirmed a <see cref="BomCompareOutcome.SwIsNewer"/> result.
        /// </summary>
        public Task PushRevisionAsync() => _source.PushRevisionToInventreeAsync();
    }
}
