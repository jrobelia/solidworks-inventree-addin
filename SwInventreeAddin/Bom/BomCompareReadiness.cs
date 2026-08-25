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
    }

    internal sealed class BomCompareReadiness
    {
        public BomCompareOutcome Outcome    { get; }
        public string            PartNumber { get; }
        public string            SwRevision { get; }
        public string            ItRevision { get; }

        public BomCompareReadiness(
            BomCompareOutcome outcome,
            string            partNumber,
            string            swRevision,
            string            itRevision)
        {
            Outcome    = outcome;
            PartNumber = partNumber;
            SwRevision = swRevision;
            ItRevision = itRevision;
        }
    }
}
