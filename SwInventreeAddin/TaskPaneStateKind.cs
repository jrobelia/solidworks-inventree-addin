namespace SwInventreeAddin
{
    /// <summary>
    /// The Task Pane's state kind — the lifecycle contract in
    /// docs/agents/task-pane-lifecycle.md. Derived from the document snapshot;
    /// never stored independently.
    /// </summary>
    /// <remarks>
    /// <see cref="Populated"/> is reserved for #92: document transitions never
    /// produce it while the temporary ViewModel session owns populated state.
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

        /// <summary>A populated session valid for the current generation — #92 only.</summary>
        Populated,
    }
}
