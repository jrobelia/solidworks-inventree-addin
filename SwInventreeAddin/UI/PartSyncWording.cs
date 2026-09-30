namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Status wording for Part Sync outcomes shared by the Task Pane status
    /// line and the BOM Compare error dialog — one source so both surfaces
    /// say the same thing.
    /// </summary>
    internal static class PartSyncWording
    {
        /// <summary>
        /// The duplicate-IPN sentence for the two confirmation-free outcomes
        /// (<see cref="PartSyncOutcome.DuplicateNoRevisionMatch"/> and
        /// <see cref="PartSyncOutcome.DuplicateAmbiguous"/>); other outcomes
        /// fall back to the diagnostic or outcome name.
        /// </summary>
        public static string DuplicateIpnStatus(PartSyncResult result) => result.Outcome switch
        {
            PartSyncOutcome.DuplicateNoRevisionMatch =>
                $"{result.Candidates?.Count ?? 0} parts share IPN ‘{result.Ipn}’ but none match " +
                $"SW revision {RevisionLabel(result.SwRevision)}. Resolve in InvenTree.",
            PartSyncOutcome.DuplicateAmbiguous =>
                $"{result.Candidates?.Count ?? 0} parts share IPN ‘{result.Ipn}’ and revision " +
                $"{RevisionLabel(result.SwRevision)}. Resolve duplicates in InvenTree.",
            _ => result.Diagnostic ?? result.Outcome.ToString(),
        };

        /// <summary>A revision for display — "(blank)" when unset.</summary>
        public static string RevisionLabel(string? revision) =>
            string.IsNullOrEmpty(revision) ? "(blank)" : revision;
    }
}
