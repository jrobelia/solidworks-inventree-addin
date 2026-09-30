using System;
using System.Drawing;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin
{
    /// <summary>
    /// The single owner of the Part Sync session and its lifecycle: the owned
    /// <see cref="TaskPaneState"/>, operation-token machinery that rejects
    /// stale async completions, the pending-write echo set, and the narrow
    /// Fetch/Create/Apply/Push behavior consumed by the Task Pane ViewModel,
    /// the BOM readiness adapter, and tests.
    /// </summary>
    /// <remarks>
    /// Every member is entered on the host STA thread unless noted. Async
    /// members do network/image work off-thread via
    /// <c>ConfigureAwait(false)</c> and commit through the injected
    /// <see cref="IHostStaDispatcher"/>, revalidating the operation token
    /// <em>inside</em> the marshalled commit. Typed outcomes are returned —
    /// the ViewModel owns status wording and confirmation prompts.
    /// </remarks>
    public interface IPartSyncCoordinator
    {
        // ── Read-only state surface ──────────────────────────────────────────

        /// <summary>Reports <see cref="TaskPaneStateKind.Populated"/> once a session is installed for the current generation.</summary>
        TaskPaneStateKind Kind { get; }

        /// <summary>Immutable document snapshot; null iff <see cref="Kind"/> is <see cref="TaskPaneStateKind.Empty"/>.</summary>
        TaskPaneDocumentSnapshot? Document { get; }

        /// <summary>The owned state's document generation.</summary>
        int Generation { get; }

        /// <summary>Immutable projection of the current session's part; null = no valid session.</summary>
        PartSnapshot? FetchedPart { get; }

        /// <summary>Defensive copy of the session's thumbnail bytes; null when none.</summary>
        byte[]? ThumbnailBytes { get; }

        /// <summary>The resolved property mapping — a defensive copy; defaults when no provider.</summary>
        PropertyMappingConfig CurrentMapping { get; }

        /// <summary>The part's public web URL; null without a session or a configured client.</summary>
        Uri? GetPartWebUrl();

        /// <summary>
        /// Raised once per committed mutation (document transition, session
        /// install/clear, post-Push field update, thumbnail set) on the host
        /// STA thread, strictly after the change is visible through the surface.
        /// </summary>
        event EventHandler? Changed;

        // ── Lifecycle (synchronous, STA) ─────────────────────────────────────

        /// <summary>
        /// STA capture (active-document token + mapped properties) → install
        /// into the owned <see cref="TaskPaneState"/>. Activated: drops the
        /// session unconditionally (no adoption — #292) and clears pending
        /// writes. Refreshed: keeps pending writes, revalidates the session
        /// against the new snapshot. Empty path clears the document once and
        /// reports Activated.
        /// </summary>
        TaskPaneDocumentTransition UpdateDocument();

        /// <summary>Clears document state, drops the session, clears pending writes.</summary>
        void NotifyDocumentClosed();

        /// <summary>
        /// Consumes a matching pending-write echo first; otherwise captures and
        /// installs a refresh and classifies the change.
        /// </summary>
        PartSyncPropertyChange NotifyDocumentPropertyChanged(string name, string newValue);

        /// <summary>Rebinds the client, advances the lifecycle revision, drops the installed session.</summary>
        void UpdateClient(IInventreeClient? client);

        /// <summary>Rebinds the mapping provider, advances the lifecycle revision, rebuilds the installed session against the new mapping.</summary>
        void UpdateMapping(IPropertyMappingProvider? provider);

        /// <summary>Advances the revision, drops session and pending confirmation; later operations return <see cref="PartSyncOutcome.InvalidOperation"/>.</summary>
        void Dispose();

        // ── Operations ───────────────────────────────────────────────────────

        /// <summary>
        /// Fetches the part for <paramref name="ipn"/> — or the stamped Part PK
        /// when the document carries one. Mints a session-family token at
        /// capture, clears the current session, works off-thread, and commits
        /// through the dispatcher with token revalidation inside the commit.
        /// </summary>
        Task<PartSyncResult> FetchAsync(string ipn);

        /// <summary>
        /// Explicit BOM populate: <see cref="PartSyncOutcome.Success"/>
        /// immediately when a session is current; otherwise the same fetch flow
        /// addressed by document identity (stamped PK else the document's IPN —
        /// never the textbox). Confirmation outcomes propagate for prompting.
        /// </summary>
        Task<PartSyncResult> EnsurePartPopulatedAsync();

        /// <summary>
        /// Mints the session-family token a Create Part dialog is opened under;
        /// supersedes in-flight fetches and is itself staled by later mints.
        /// </summary>
        PartSyncOperationToken BeginCreatePart();

        /// <summary>
        /// Synchronous STA commit for a created part: validates the token,
        /// writes PK/IPN/Name (each pending-registered before its write),
        /// substitute-refreshes the document, installs the session.
        /// </summary>
        PartSyncResult CompleteCreatePart(PartSyncOperationToken token, InventreePart part);

        /// <summary>
        /// Synchronous STA Apply of one field. Returns
        /// <see cref="PartSyncOutcome.MissingPropertyConfirmation"/> when the
        /// mapped Document Property does not exist; resume through
        /// <see cref="ResumeConfirmationAsync"/>.
        /// </summary>
        PartSyncResult Apply(ApplyField field);

        /// <summary>
        /// STA capture of the mapped property value → client update off-thread
        /// → validated commit (token + session identity) mutating the session
        /// part and raising <see cref="Changed"/>.
        /// </summary>
        Task<PartSyncResult> PushAsync(PushField field);

        /// <summary>
        /// <c>ImagePipeline.Process</c> on STA → upload → re-fetch/download the
        /// thumbnail off-thread → validated commit installing the thumbnail.
        /// The viewport capture and crop dialog live in the host, which passes
        /// the image and rectangle in.
        /// </summary>
        Task<PartSyncResult> PushImageAsync(Image image, Rectangle cropRect);

        /// <summary>
        /// Resumes the pending confirmation identified by
        /// <paramref name="handle"/>. For duplicate candidates,
        /// <paramref name="selectedCandidatePk"/> chooses by immutable PK and
        /// is validated against the captured candidate set. An unknown or
        /// obsolete handle returns <see cref="PartSyncOutcome.InvalidOperation"/>;
        /// a stale operation token returns <see cref="PartSyncOutcome.Stale"/>;
        /// <paramref name="approved"/> false returns
        /// <see cref="PartSyncOutcome.Cancelled"/> and clears the pending state.
        /// </summary>
        Task<PartSyncResult> ResumeConfirmationAsync(
            PartSyncConfirmationHandle handle, bool approved, int? selectedCandidatePk = null);
    }
}
