namespace SwInventreeAddin.SolidWorks
{
    /// <summary>
    /// The type of SolidWorks document currently active.
    /// Stored on TaskPaneViewModel so all future per-type logic has a single source of truth.
    /// </summary>
    public enum DocumentType
    {
        Unknown,
        Part,
        Assembly,
        Drawing,
    }
}
