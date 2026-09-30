using System.Collections.Generic;

namespace SwInventreeAddin
{
    /// <summary>The typed outcome of a Part Sync coordinator operation.</summary>
    public enum PartSyncOutcome
    {
        /// <summary>The operation completed and any session/document mutations were committed.</summary>
        Success,

        /// <summary>The operation completed but a follow-up step degraded (e.g. thumbnail preview).</summary>
        SucceededWithWarning,

        /// <summary>The addressed part does not exist in InvenTree.</summary>
        PartNotFound,

        /// <summary>An IPN resolves to several parts and exactly one matches the SW revision; user confirmation is required.</summary>
        DuplicateIpnConfirmation,

        /// <summary>An IPN resolves to several parts and none match the SW revision.</summary>
        DuplicateNoRevisionMatch,

        /// <summary>An IPN resolves to several parts and several match the SW revision.</summary>
        DuplicateAmbiguous,

        /// <summary>The stamped PK resolves to a part whose IPN/revision disagrees with the document; user confirmation is required.</summary>
        LinkMismatchConfirmation,

        /// <summary>An Apply targets mapped Document Property names absent from the document; user confirmation is required.</summary>
        MissingPropertyConfirmation,

        /// <summary>The captured operation token was no longer current when the result committed — a stale async completion.</summary>
        Stale,

        /// <summary>The user declined a confirmation prompt.</summary>
        Cancelled,

        /// <summary>The operation was not valid right now (no session, no client, unhealthy mapping, disposed, or unknown confirmation handle).</summary>
        InvalidOperation,

        /// <summary>The operation failed; <see cref="PartSyncResult.Diagnostic"/> carries the detail.</summary>
        Failed,
    }

    /// <summary>How the coordinator classified a SolidWorks Document Property change notification.</summary>
    public enum PartSyncPropertyChange
    {
        /// <summary>The notification echoed an add-in-originated write and was consumed.</summary>
        EchoConsumed,

        /// <summary>Identity diverged, no session, or mapping unhealthy — the caller should run a full evaluation.</summary>
        Reevaluated,

        /// <summary>A same-document refresh was installed; nothing user-visible to reset.</summary>
        Refreshed,

        /// <summary>A mapped non-identity property diverged from the session — a user edit; clear stale status.</summary>
        RefreshedDivergent,

        /// <summary>A property this mapping does not use changed; no effect.</summary>
        Ignored,
    }

    /// <summary>A document-side field the coordinator can Apply (InvenTree → SolidWorks).</summary>
    public enum ApplyField { Name, Notes, Description, Pk }

    /// <summary>A part-side field the coordinator can Push (SolidWorks → InvenTree).</summary>
    public enum PushField { Name, Notes, Description, Revision }

    /// <summary>
    /// The immutable typed outcome of a coordinator operation. Carries no UI
    /// strings and no callbacks — the ViewModel owns outcome → status mapping.
    /// Payload setters are internal so the type is read-only to consumers.
    /// </summary>
    public sealed class PartSyncResult
    {
        public PartSyncResult(PartSyncOutcome outcome) => Outcome = outcome;

        public PartSyncOutcome Outcome { get; }

        /// <summary>Opaque correlation token; non-null on every confirmation outcome.</summary>
        public PartSyncConfirmationHandle? Confirmation { get; internal set; }

        /// <summary>The request IPN (IPN-addressed fetch) or the IPN stamped on the document (PK-path write-back).</summary>
        public string? Ipn { get; internal set; }

        /// <summary>The request/stamped part PK for PK-addressed outcomes; 0 otherwise.</summary>
        public int PartPk { get; internal set; }

        /// <summary>The SolidWorks revision used for duplicate-candidate matching.</summary>
        public string? SwRevision { get; internal set; }

        /// <summary>All duplicate-IPN candidates as immutable projections.</summary>
        public IReadOnlyList<PartSnapshot>? Candidates { get; internal set; }

        /// <summary>The revision-matched duplicate candidate offered for confirmation.</summary>
        public PartSnapshot? MatchedCandidate { get; internal set; }

        /// <summary>The installed/created part projection for success-family outcomes.</summary>
        public PartSnapshot? FetchedPart { get; internal set; }

        /// <summary>The document's stamped IPN for link-mismatch prompts.</summary>
        public string? DocumentIpn { get; internal set; }

        /// <summary>The document's revision for link-mismatch prompts.</summary>
        public string? DocumentRevision { get; internal set; }

        /// <summary>Mapped Document Property names absent from the document (missing-property confirmation).</summary>
        public IReadOnlyList<string>? MissingProperties { get; internal set; }

        /// <summary>Failure detail or warning text; internal diagnostic, never a UI status string.</summary>
        public string? Diagnostic { get; internal set; }
    }
}
