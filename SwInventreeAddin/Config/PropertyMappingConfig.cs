using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        public string? SchemaVersion { get; set; }

        /// <summary>SW custom property that holds the InvenTree IPN.</summary>
        public string? IpnProperty { get; set; }

        /// <summary>SW custom property mapped to InvenTree Part.name.</summary>
        public string? NameProperty { get; set; }

        /// <summary>SW custom property mapped to InvenTree Part.notes.</summary>
        public string? NotesProperty { get; set; }

        /// <summary>SW custom property mapped to InvenTree Part.revision.</summary>
        public string? RevisionProperty { get; set; }

        /// <summary>SW custom property mapped to InvenTree Part.description (task 6).</summary>
        public string? DescriptionProperty { get; set; }

        /// <summary>SW custom property that stores the InvenTree primary key (written on create; apply-only).</summary>
        public string? PkProperty { get; set; }

        // BOM column header mappings (comma-separated aliases, case-insensitive)
        public string? BomColumnIpn       { get; set; }
        public string? BomColumnQty       { get; set; }
        public string? BomColumnReference { get; set; }
        public string? BomColumnNote      { get; set; }

        /// <summary>
        /// Round-trips top-level JSON properties that the current add-in does not recognise.
        /// Do not use directly; it is populated and read by the JSON serializer.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> ExtensionData { get; set; }
            = new Dictionary<string, JsonElement>();

        /// <summary>
        /// Returns a new config with the current schema and all built-in defaults filled in.
        /// Used for first-run and for callers that need an effective mapping.
        /// </summary>
        public static PropertyMappingConfig WithDefaults() => new PropertyMappingConfig
        {
            SchemaVersion       = CurrentSchemaVersion,
            IpnProperty         = "PartNo",
            NameProperty        = "Description",
            NotesProperty       = "Notes",
            RevisionProperty    = "Revision",
            DescriptionProperty = "Description Long",
            PkProperty          = "InvenTree PK",
            BomColumnIpn        = "IPN, Part IPN, Internal Part Number, Part Number",
            BomColumnQty        = "Qty, Quantity",
            BomColumnReference  = "Reference",
            BomColumnNote       = "Note, Notes"
        };

        /// <summary>
        /// Returns a new config with built-in defaults, then overlays any non-null values
        /// from <paramref name="overrides"/>.
        /// </summary>
        public static PropertyMappingConfig WithDefaults(PropertyMappingConfig? overrides)
        {
            var merged = WithDefaults();
            if (overrides == null)
                return merged;

            if (overrides.SchemaVersion       != null) merged.SchemaVersion       = overrides.SchemaVersion;
            if (overrides.IpnProperty          != null) merged.IpnProperty          = overrides.IpnProperty;
            if (overrides.NameProperty         != null) merged.NameProperty         = overrides.NameProperty;
            if (overrides.NotesProperty        != null) merged.NotesProperty        = overrides.NotesProperty;
            if (overrides.RevisionProperty     != null) merged.RevisionProperty     = overrides.RevisionProperty;
            if (overrides.DescriptionProperty  != null) merged.DescriptionProperty  = overrides.DescriptionProperty;
            if (overrides.PkProperty           != null) merged.PkProperty           = overrides.PkProperty;
            if (overrides.BomColumnIpn        != null) merged.BomColumnIpn        = overrides.BomColumnIpn;
            if (overrides.BomColumnQty        != null) merged.BomColumnQty        = overrides.BomColumnQty;
            if (overrides.BomColumnReference  != null) merged.BomColumnReference  = overrides.BomColumnReference;
            if (overrides.BomColumnNote       != null) merged.BomColumnNote       = overrides.BomColumnNote;
            merged.ExtensionData = overrides.ExtensionData;
            return merged;
        }
    }
}
