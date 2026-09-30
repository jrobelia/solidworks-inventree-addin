using System;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin
{
    /// <summary>
    /// Owns the Task Pane's active-document state: a monotonically increasing
    /// document <see cref="Generation"/>, the current
    /// <see cref="TaskPaneDocumentSnapshot"/>, and the derived
    /// <see cref="TaskPaneStateKind"/>.
    /// UI-free — no WPF, SolidWorks COM, InvenTree, status text, or
    /// confirmation concerns. The host captures SolidWorks values on the STA
    /// thread and installs complete snapshots here.
    /// </summary>
    /// <remarks>
    /// Transition contract:
    /// <list type="bullet">
    /// <item>A document token equal (Ordinal) to the stored one is
    /// <see cref="TaskPaneDocumentTransition.Refreshed"/> — the new snapshot
    /// replaces the old in place and <see cref="Generation"/> is unchanged.
    /// Duplicate SolidWorks load/activation callbacks for the same document
    /// therefore self-classify as refreshes.</item>
    /// <item>A different token — or any update while empty — is
    /// <see cref="TaskPaneDocumentTransition.Activated"/> and advances
    /// <see cref="Generation"/>.</item>
    /// <item><see cref="ClearDocument"/> always produces
    /// <see cref="TaskPaneStateKind.Empty"/> and advances
    /// <see cref="Generation"/> — closing the last document invalidates the
    /// prior generation.</item>
    /// </list>
    /// <see cref="Changed"/> is raised synchronously on the transition caller's
    /// thread, exactly once per call, strictly after the new state is installed
    /// and visible through <see cref="Kind"/>/<see cref="Generation"/>/
    /// <see cref="Document"/>. This module does no thread marshalling; the
    /// ViewModel translates the event into WPF notifications on the UI thread.
    /// </remarks>
    public sealed class TaskPaneState
    {
        private string? _documentToken;
        private TaskPaneDocumentSnapshot? _document;
        private int _generation;
        private int _populatedGeneration = -1;

        /// <summary>
        /// The Task Pane's state kind. Derived from the snapshot and the
        /// populated signal — it cannot disagree with the installed state.
        /// </summary>
        public TaskPaneStateKind Kind =>
            _document == null ? TaskPaneStateKind.Empty
            : _document.DocumentType == DocumentType.Drawing ? TaskPaneStateKind.Unsupported
            : _populatedGeneration == _generation ? TaskPaneStateKind.Populated
            : string.IsNullOrEmpty(_document.Ipn) && _document.StampedPartPk == 0
                ? TaskPaneStateKind.Unlinked
            : TaskPaneStateKind.Linked;

        /// <summary>
        /// The document generation — starts at 0 and increases monotonically.
        /// Owned solely by this module; callers never supply a value.
        /// </summary>
        public int Generation => _generation;

        /// <summary>
        /// The generation the populated signal was last bound to; -1 when no
        /// Part Sync session result is installed. The marker self-invalidates
        /// the moment <see cref="Generation"/> advances, so a stale marker can
        /// never report <see cref="TaskPaneStateKind.Populated"/> for a newer
        /// document.
        /// </summary>
        public int PopulatedGeneration => _populatedGeneration;

        /// <summary>
        /// The active document's snapshot; null iff
        /// <see cref="Kind"/> is <see cref="TaskPaneStateKind.Empty"/>.
        /// </summary>
        public TaskPaneDocumentSnapshot? Document => _document;

        /// <summary>Raised once per transition, after the new state is installed.</summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Installs a complete document snapshot atomically. Classifies as
        /// <see cref="TaskPaneDocumentTransition.Activated"/> when
        /// <paramref name="documentToken"/> differs from the stored token (or
        /// no document is active), otherwise
        /// <see cref="TaskPaneDocumentTransition.Refreshed"/>.
        /// </summary>
        /// <param name="documentToken">
        /// Opaque active-document identity resolved by the host — never a COM
        /// object.
        /// </param>
        public TaskPaneDocumentTransition ApplyDocumentUpdate(
            string documentToken, TaskPaneDocumentSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(documentToken))
                throw new ArgumentException(
                    "Document token must be a non-empty opaque identity.", nameof(documentToken));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.DocumentType == DocumentType.Unknown)
                throw new ArgumentException(
                    "Unknown document type means no document — call ClearDocument instead.",
                    nameof(snapshot));

            var transition = _document == null
                || !string.Equals(_documentToken, documentToken, StringComparison.Ordinal)
                    ? TaskPaneDocumentTransition.Activated
                    : TaskPaneDocumentTransition.Refreshed;

            if (transition == TaskPaneDocumentTransition.Activated)
                _generation++;

            _documentToken = documentToken;
            _document = snapshot;
            Changed?.Invoke(this, EventArgs.Empty);
            return transition;
        }

        /// <summary>
        /// Clears the active document, producing
        /// <see cref="TaskPaneStateKind.Empty"/> and advancing
        /// <see cref="Generation"/>.
        /// </summary>
        public void ClearDocument()
        {
            _documentToken = null;
            _document = null;
            _generation++;
            _populatedGeneration = -1;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Binds the populated signal to the current generation: a Part Sync
        /// session result is installed for the document that is active right
        /// now. Valid only while a Part/Assembly document is installed
        /// (<see cref="TaskPaneStateKind.Unlinked"/> or
        /// <see cref="TaskPaneStateKind.Linked"/> — a typed-IPN fetch can
        /// populate before any stamp exists).
        /// </summary>
        public void MarkPopulated()
        {
            if (_document == null || _document.DocumentType == DocumentType.Drawing)
                throw new InvalidOperationException(
                    "Populated requires an installed Part/Assembly document.");

            _populatedGeneration = _generation;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Releases the populated signal. Harmless when not populated — the
        /// marker is generation-bound, so clearing it can never affect a newer
        /// document generation.
        /// </summary>
        public void ClearPopulated()
        {
            _populatedGeneration = -1;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>How a document update classified inside <see cref="TaskPaneState"/>.</summary>
    public enum TaskPaneDocumentTransition
    {
        /// <summary>A different active document (or the first) — generation advanced.</summary>
        Activated,

        /// <summary>The same active document re-captured — generation unchanged.</summary>
        Refreshed,
    }
}
