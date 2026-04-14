namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Maps InvenTree field roles to SolidWorks custom property names.
    /// Serialised as a human-readable JSON file; not encrypted.
    /// </summary>
    public class PropertyMappingConfig
    {
        /// <summary>Current schema version — bump when adding new required fields.</summary>
        public const string CurrentSchemaVersion = "3";

        /// <summary>Schema version — bump when adding new required fields.</summary>
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>SW custom property that holds the InvenTree IPN.</summary>
        public string IpnProperty { get; set; } = "PartNo";

        /// <summary>SW custom property mapped to InvenTree Part.name.</summary>
        public string NameProperty { get; set; } = "Description";

        /// <summary>SW custom property mapped to InvenTree Part.notes.</summary>
        public string NotesProperty { get; set; } = "Notes";

        /// <summary>SW custom property mapped to InvenTree Part.revision.</summary>
        public string RevisionProperty { get; set; } = "Revision";

        /// <summary>SW custom property mapped to InvenTree Part.description (task 6).</summary>
        public string DescriptionProperty { get; set; } = "Description Long";

        /// <summary>SW custom property that stores the InvenTree primary key (written on create; apply-only).</summary>
        public string PkProperty { get; set; } = "InvenTree PK";

        // BOM column header mappings (comma-separated aliases, case-insensitive)
        public string BomColumnIpn       { get; set; } = "IPN, Internal Part Number, Part Number";
        public string BomColumnQty       { get; set; } = "Qty, Quantity";
        public string BomColumnReference { get; set; } = "Reference";
        public string BomColumnNote      { get; set; } = "Note, Notes";
    }
}
