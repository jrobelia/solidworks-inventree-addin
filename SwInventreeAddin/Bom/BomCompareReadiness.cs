namespace SwInventreeAddin.Bom
{
    internal enum BomCompareOutcome
    {
        /// <summary>All pre-flight checks passed. BOM Compare may proceed.</summary>
        Ready,

        /// <summary>The IPN was not found in InvenTree. Part must be created first.</summary>
        PkNotFound,

        /// <summary>
        /// The InvenTree PK has not been stamped into this assembly's SolidWorks Document
        /// Properties. Run Part Sync first to stamp it.
        /// </summary>
        PkNotStamped,

        /// <summary>
        /// InvenTree is at a newer revision than this file. The file is stale — do not
        /// push its BOM.
        /// </summary>
        ItIsNewer,

        /// <summary>
        /// Revision order cannot be determined automatically. Resolve manually before
        /// running BOM Compare.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// SolidWorks is ahead of InvenTree. The caller should offer to push the revision
        /// before opening BOM Compare.
        /// </summary>
        SwIsNewer,

        /// <summary>
        /// No SolidWorks BOM table matching the configured keyword was found in the active
        /// assembly. The caller should warn the user and not open BOM Compare.
        /// </summary>
        BomTableMissing,

        /// <summary>
        /// The IPN or Qty BOM Column Alias is blank. The caller should warn the user and
        /// still allow the compare window to open.
        /// </summary>
        BomColumnAliasesMissing,

        /// <summary>
        /// The auto-populate fetch stopped at a typed confirmation (duplicate
        /// IPN, Link Mismatch). The caller prompts through
        /// <see cref="BomCompareReadiness.FetchResult"/>, resumes the
        /// coordinator's pending confirmation, and re-runs the check.
        /// </summary>
        FetchConfirmationRequired,

        /// <summary>
        /// The auto-populate fetch returned a non-success, non-confirmation
        /// outcome — server failure, stale/cancelled lifecycle, invalid
        /// operation, or a terminal duplicate. The caller inspects
        /// <see cref="BomCompareReadiness.FetchResult"/> and presents it
        /// honestly (never as "create the part").
        /// </summary>
        FetchFailed,
    }

    internal sealed class BomCompareReadiness
    {
        public BomCompareOutcome Outcome { get; }
        public string PartNumber { get; }
        public string SwRevision { get; }
        public string ItRevision { get; }

        /// <summary>
        /// The coordinator fetch result produced by the auto-populate fetch;
        /// non-null when <see cref="Outcome"/> is
        /// <see cref="BomCompareOutcome.FetchConfirmationRequired"/>,
        /// <see cref="BomCompareOutcome.PkNotFound"/> (ensure returned
        /// <see cref="PartSyncOutcome.PartNotFound"/>), or
        /// <see cref="BomCompareOutcome.FetchFailed"/>.
        /// </summary>
        public PartSyncResult? FetchResult { get; }

        /// <summary>
        /// The identifier a not-found dialog names — the document IPN when one
        /// is stamped, otherwise the InvenTree Part PK the fetch looked up.
        /// Falls back to a generic label when neither is available.
        /// </summary>
        public string NotFoundIdentifier =>
            !string.IsNullOrEmpty(PartNumber)
                ? $"'{PartNumber}'"
                : FetchResult?.PartPk > 0
                    ? $"InvenTree Part PK {FetchResult!.PartPk}"
                    : "The part";

        public BomCompareReadiness(
            BomCompareOutcome outcome,
            string partNumber,
            string swRevision,
            string itRevision,
            PartSyncResult? fetchResult = null)
        {
            Outcome = outcome;
            PartNumber = partNumber;
            SwRevision = swRevision;
            ItRevision = itRevision;
            FetchResult = fetchResult;
        }
    }
}
