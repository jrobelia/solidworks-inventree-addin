namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The add-in's evaluation of whether the current <see cref="PropertyMappingConfig"/> can be used.
    /// </summary>
    public enum MappingHealth
    {
        /// <summary>Current mapping schema version, valid mappings, no duplicates, and readable.</summary>
        Healthy,

        /// <summary>Older mapping schema version. Fetch is allowed, but Apply, Push, Create Part, and BOM Compare are locked until the file is saved with the current schema.</summary>
        NeedsUpgrade,

        /// <summary>Corrupt, locked, missing, unreadable, or duplicate SolidWorks Document Property names. No Part Sync is allowed.</summary>
        Invalid
    }
}
