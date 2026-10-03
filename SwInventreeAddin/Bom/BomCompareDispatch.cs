using System;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// The icon a <see cref="BomCompareAction"/> dialog payload carries. Kept
    /// free of any UI assembly reference — the caller translates it to
    /// <see cref="System.Windows.Forms.MessageBoxIcon"/>.
    /// </summary>
    internal enum BomCompareDialogIcon
    {
        Information,
        Warning,
        Stop,
        Question,
        Error,
    }

    /// <summary>The mechanical step the BOM Compare handler executes next.</summary>
    internal enum BomCompareActionKind
    {
        /// <summary>Open the BOM Compare window.</summary>
        Proceed,

        /// <summary>Show the payload warning dialog, then open the BOM Compare window.</summary>
        WarnAndProceed,

        /// <summary>Show the payload dialog, then stop.</summary>
        ShowMessage,

        /// <summary>Show the dedicated BOM-table-missing dialog, then stop.</summary>
        ShowBomTableMissing,

        /// <summary>
        /// Prompt to push the SolidWorks revision to InvenTree; triage the result
        /// through <see cref="BomCompareDispatch.AfterRevisionPush"/>.
        /// </summary>
        OfferRevisionPush,

        /// <summary>Re-run the readiness check and dispatch again — a push executed.</summary>
        Recheck,

        /// <summary>Stop with no dialog and no status write.</summary>
        StopSilently,

        /// <summary>
        /// The outcome is not one the dispatch recognises — fail fast through the
        /// error dialog; never proceed and never re-loop.
        /// </summary>
        Unrecognized,
    }

    /// <summary>
    /// The immutable descriptor <see cref="BomCompareDispatch"/> returns: which
    /// mechanical step the control executes, plus the dialog payload
    /// (<see cref="Message"/>/<see cref="Title"/>/<see cref="Icon"/>) on the
    /// dialog kinds. Carries no UI-assembly types.
    /// </summary>
    internal sealed class BomCompareAction
    {
        private BomCompareAction(
            BomCompareActionKind kind, string? message, string? title, BomCompareDialogIcon icon)
        {
            Kind = kind;
            Message = message;
            Title = title;
            Icon = icon;
        }

        public BomCompareActionKind Kind { get; }
        public string? Message { get; }
        public string? Title { get; }
        public BomCompareDialogIcon Icon { get; }

        public static BomCompareAction Proceed() =>
            new BomCompareAction(BomCompareActionKind.Proceed, null, null, BomCompareDialogIcon.Information);

        public static BomCompareAction WarnAndProceed(string message) =>
            new BomCompareAction(
                BomCompareActionKind.WarnAndProceed, message,
                "BOM Compare — Missing Alias", BomCompareDialogIcon.Warning);

        public static BomCompareAction ShowMessage(
            string message, string title, BomCompareDialogIcon icon) =>
            new BomCompareAction(BomCompareActionKind.ShowMessage, message, title, icon);

        public static BomCompareAction ShowBomTableMissing() =>
            new BomCompareAction(BomCompareActionKind.ShowBomTableMissing, null, null, BomCompareDialogIcon.Information);

        public static BomCompareAction OfferRevisionPush(string message) =>
            new BomCompareAction(
                BomCompareActionKind.OfferRevisionPush, message,
                "BOM Compare — Revision Mismatch", BomCompareDialogIcon.Question);

        public static BomCompareAction Recheck() =>
            new BomCompareAction(BomCompareActionKind.Recheck, null, null, BomCompareDialogIcon.Information);

        public static BomCompareAction StopSilently() =>
            new BomCompareAction(BomCompareActionKind.StopSilently, null, null, BomCompareDialogIcon.Information);

        public static BomCompareAction Unrecognized(string message) =>
            new BomCompareAction(
                BomCompareActionKind.Unrecognized, message,
                "BOM Compare", BomCompareDialogIcon.Error);
    }

    /// <summary>
    /// The pure <see cref="BomCompareReadiness"/> → <see cref="BomCompareAction"/>
    /// mapping behind Compare BOM. Decides which dialog each outcome gets, which
    /// results stop silently, and the push-once rule; the control executes the
    /// returned action mechanically and re-enters via <see cref="Recheck"/>.
    /// </summary>
    internal static class BomCompareDispatch
    {
        /// <summary>
        /// Maps one readiness result to the next mechanical step.
        /// <paramref name="revisionPushAttempted"/> is the loop state: the caller
        /// sets it once a revision push has executed and never resets it, so a
        /// repeated <see cref="BomCompareOutcome.SwIsNewer"/> gets the still-newer
        /// error instead of a second prompt. <paramref name="mapping"/> is read
        /// only for <see cref="BomCompareOutcome.BomColumnAliasesMissing"/>.
        /// </summary>
        public static BomCompareAction Next(
            BomCompareReadiness readiness, bool revisionPushAttempted, PropertyMappingConfig mapping)
        {
            if (readiness == null) throw new ArgumentNullException(nameof(readiness));
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            switch (readiness.Outcome)
            {
                case BomCompareOutcome.Ready:
                    return BomCompareAction.Proceed();

                case BomCompareOutcome.BomColumnAliasesMissing:
                    return BomCompareAction.WarnAndProceed(DescribeMissingBomColumnAliases(mapping));

                case BomCompareOutcome.PkNotStamped:
                    return BomCompareAction.ShowMessage(
                        "No InvenTree Part PK is stored in this assembly’s Document Properties.\n\n"
                        + "Apply the InvenTree PK to the document first, then try again.",
                        "BOM Compare — PK Missing",
                        BomCompareDialogIcon.Warning);

                case BomCompareOutcome.ItIsNewer:
                    return BomCompareAction.ShowMessage(
                        $"InvenTree is at revision “{readiness.ItRevision}” but this file is revision “{readiness.SwRevision}”.\n\n"
                        + "You have an older file open. Close it — do not push its BOM to InvenTree.",
                        "BOM Compare — Old Revision",
                        BomCompareDialogIcon.Stop);

                case BomCompareOutcome.Ambiguous:
                    return BomCompareAction.ShowMessage(
                        $"Revision mismatch (SolidWorks: {RevisionLabel(readiness.SwRevision)} / InvenTree: {RevisionLabel(readiness.ItRevision)}).\n\n"
                        + "The order cannot be determined automatically. Resolve the revision manually before comparing the BOM.",
                        "BOM Compare — Revision Ambiguous",
                        BomCompareDialogIcon.Warning);

                case BomCompareOutcome.SwIsNewer:
                    return revisionPushAttempted
                        ? BomCompareAction.ShowMessage(
                            "The SolidWorks revision is still newer after the update. Close this file and pull the latest revision from InvenTree.",
                            "BOM Compare",
                            BomCompareDialogIcon.Error)
                        : BomCompareAction.OfferRevisionPush(
                            $"Revision mismatch:\n  SolidWorks:  {RevisionLabel(readiness.SwRevision)}\n  InvenTree:   {RevisionLabel(readiness.ItRevision)}\n\n"
                            + $"Update InvenTree to revision “{RevisionLabel(readiness.SwRevision)}” and proceed?");

                case BomCompareOutcome.BomTableMissing:
                    return BomCompareAction.ShowBomTableMissing();

                case BomCompareOutcome.SessionNotPopulated:
                    // The session dropped between button enablement and the
                    // check — stop silently, like a declined push.
                    return BomCompareAction.StopSilently();

                default:
                    // An outcome added without a dispatch entry must never fall
                    // through to the compare window or re-loop.
                    return BomCompareAction.Unrecognized(
                        $"Unrecognized BOM Compare outcome: {readiness.Outcome}");
            }
        }

        /// <summary>
        /// Triages the result after an <see cref="BomCompareActionKind.OfferRevisionPush"/>
        /// executes; <paramref name="pushResult"/> is null when the user declined
        /// the prompt. Stale is silent — a newer operation owns the pane.
        /// </summary>
        public static BomCompareAction AfterRevisionPush(PartSyncResult? pushResult)
        {
            if (pushResult == null)
                return BomCompareAction.StopSilently();

            switch (pushResult.Outcome)
            {
                case PartSyncOutcome.Success:
                case PartSyncOutcome.SucceededWithWarning:
                    return BomCompareAction.Recheck();
                case PartSyncOutcome.Stale:
                case PartSyncOutcome.Cancelled:
                    return BomCompareAction.StopSilently();
                default:
                    return BomCompareAction.ShowMessage(
                        $"Failed to update revision in InvenTree:{Environment.NewLine}"
                        + (pushResult.Diagnostic ?? pushResult.Outcome.ToString()),
                        "BOM Compare — Revision Update Failed",
                        BomCompareDialogIcon.Error);
            }
        }

        private static string RevisionLabel(string revision) =>
            string.IsNullOrEmpty(revision) ? "(blank)" : revision;

        private static string DescribeMissingBomColumnAliases(PropertyMappingConfig mapping)
        {
            var missing = mapping.GetMissingBomCompareAliases();
            var aliasList = string.Join(" and ", missing);
            var valueList = string.Join(" or ", missing);
            var verb = missing.Count == 1 ? "is" : "are";
            var pronoun = missing.Count == 1 ? "it is" : "they are";
            var aliasWord = missing.Count == 1 ? "Alias" : "Aliases";

            return $"The {aliasList} BOM Column {aliasWord} {verb} blank.\n\n"
                 + $"BOM Compare will not find {valueList} values until {pronoun} set "
                 + "in Settings > Property Mappings.\n\n"
                 + "Click OK to open the comparison anyway.";
        }
    }
}

