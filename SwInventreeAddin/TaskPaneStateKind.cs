namespace SwInventreeAddin
{
    /// <summary>
    /// The Task Pane's state kind — the lifecycle contract in
    /// docs/agents/task-pane-lifecycle.md. Derived from the document snapshot;
    /// never stored independently.
    /// </summary>
    /// <remarks>
    /// <see cref="Populated"/> is produced only through
    /// <see cref="TaskPaneState.MarkPopulated"/> — the thin populated-signal the
    /// Part Sync coordinator binds to the current document generation when it
    /// installs a session. Document transitions never produce it directly.
    /// </remarks>
    public enum TaskPaneStateKind
    {
        /// <summary>No active document.</summary>
        Empty,

        /// <summary>Unsupported active document (currently Drawing).</summary>
        Unsupported,

        /// <summary>Supported document with neither IPN nor stamped InvenTree Part PK.</summary>
        Unlinked,

        /// <summary>Supported document with an IPN or stamped InvenTree Part PK.</summary>
        Linked,

        /// <summary>
        /// A populated Part Sync session result is installed for the current
        /// document generation. The rich populated data lives in
        /// PartSyncCoordinator — this kind only reports that it exists.
        /// </summary>
        Populated,
    }
}
