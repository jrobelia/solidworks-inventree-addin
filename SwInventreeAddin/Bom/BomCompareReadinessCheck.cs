using System;
using System.Threading.Tasks;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// Evaluates whether a BOM Compare can proceed given the current Task Pane state.
    /// Encapsulates the four pre-flight rules that gate the BOM Compare workflow:
    /// auto-fetch if the InvenTree PK is not yet in memory, PK-in-memory check,
    /// PK-stamped-in-document check, and four-way revision comparison.
    /// </summary>
    internal sealed class BomCompareReadinessCheck
    {
        private readonly IBomReadinessSource _source;

        public BomCompareReadinessCheck(IBomReadinessSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
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

            // Auto-fetch if we don't already have the PK in memory.
            if (_source.CurrentInvenTreePk == 0)
                await _source.FetchPartAsync().ConfigureAwait(false);

            if (_source.CurrentInvenTreePk == 0)
                return new BomCompareReadiness(BomCompareOutcome.PkNotFound, partNumber, swRev, itRev);

            // PK must be stamped in the SolidWorks Document Properties.
            _source.RefreshCurrentProperties();
            if (string.IsNullOrWhiteSpace(_source.CurrentPk))
                return new BomCompareReadiness(BomCompareOutcome.PkNotStamped, partNumber, swRev, itRev);

            // Re-read rev values after the refresh.
            swRev = _source.CurrentRevision?.Trim() ?? string.Empty;
            itRev = _source.RevisionPreview?.Trim()  ?? string.Empty;

            var revOrder = RevisionComparer.Compare(swRev, itRev);

            return revOrder switch
            {
                RevisionOrder.ItIsNewer => new BomCompareReadiness(BomCompareOutcome.ItIsNewer,  partNumber, swRev, itRev),
                RevisionOrder.Ambiguous => new BomCompareReadiness(BomCompareOutcome.Ambiguous,  partNumber, swRev, itRev),
                RevisionOrder.SwIsNewer => new BomCompareReadiness(BomCompareOutcome.SwIsNewer,  partNumber, swRev, itRev),
                _                       => new BomCompareReadiness(BomCompareOutcome.Ready,      partNumber, swRev, itRev),
            };
        }

        /// <summary>
        /// Pushes the SolidWorks revision to InvenTree. Call only when the caller has
        /// confirmed a <see cref="BomCompareOutcome.SwIsNewer"/> result.
        /// </summary>
        public Task PushRevisionAsync() => _source.PushRevisionToInventreeAsync();
    }
}
