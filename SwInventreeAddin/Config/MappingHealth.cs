namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The add-in's evaluation of whether the current <see cref="PropertyMappingConfig"/> can be used.
    /// </summary>
    public enum MappingHealth
    {
        /// <summary>Current Property Mapping Schema, valid mappings, no duplicates, and readable. All Part Sync actions are allowed.</summary>
        Healthy,

        /// <summary>The Property Mapping Schema is older than the add-in's supported version. No Part Sync is allowed until the file is saved with the current schema.</summary>
        NeedsUpgrade,

        /// <summary>The Property Mapping Schema is newer than this add-in version. No Part Sync is allowed until the add-in is upgraded.</summary>
        NewerSchema,

        /// <summary>The Property Mapping file is corrupt, locked, missing, unreadable, or has duplicate SolidWorks Document Property names. No Part Sync is allowed.</summary>
        Invalid
    }
}
