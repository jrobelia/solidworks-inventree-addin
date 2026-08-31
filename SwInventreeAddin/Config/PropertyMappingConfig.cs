using System;
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
        /// Describes one of the 11 string fields and its built-in default so
        /// <see cref="WithDefaults"/>, <see cref="Normalized"/>, and
        /// <see cref="WithDefaults(PropertyMappingConfig?)"/> share the same map.
        /// </summary>
        private sealed class StringField
        {
            public Func<PropertyMappingConfig, string?>    Get    { get; }
            public Action<PropertyMappingConfig, string?>  Set    { get; }
            public string?                                 Default { get; }

            public StringField(Func<PropertyMappingConfig, string?> get,
                               Action<PropertyMappingConfig, string?> set,
                               string? @default)
            {
                Get    = get;
                Set    = set;
                Default = @default;
            }
        }

        private static readonly StringField[] StringFields =
        {
            new StringField(c => c.SchemaVersion,       (c, v) => c.SchemaVersion       = v, CurrentSchemaVersion),
            new StringField(c => c.IpnProperty,         (c, v) => c.IpnProperty         = v, "PartNo"),
            new StringField(c => c.NameProperty,        (c, v) => c.NameProperty        = v, "Description"),
            new StringField(c => c.NotesProperty,       (c, v) => c.NotesProperty       = v, "Notes"),
            new StringField(c => c.RevisionProperty,    (c, v) => c.RevisionProperty    = v, "Revision"),
            new StringField(c => c.DescriptionProperty, (c, v) => c.DescriptionProperty = v, "Description Long"),
            new StringField(c => c.PkProperty,          (c, v) => c.PkProperty          = v, "InvenTree PK"),
            new StringField(c => c.BomColumnIpn,        (c, v) => c.BomColumnIpn        = v, "IPN, Part IPN, Internal Part Number, Part Number"),
            new StringField(c => c.BomColumnQty,        (c, v) => c.BomColumnQty        = v, "Qty, Quantity"),
            new StringField(c => c.BomColumnReference,  (c, v) => c.BomColumnReference  = v, "Reference"),
            new StringField(c => c.BomColumnNote,       (c, v) => c.BomColumnNote       = v, "Note, Notes")
        };

        /// <summary>
        /// Returns a shallow copy of the config with a distinct <see cref="ExtensionData"/> dictionary.
        /// String properties are copied by reference (use <see cref="Normalized"/> when whitespace
        /// should be coalesced to <c>null</c>).
        /// </summary>
        public PropertyMappingConfig Clone()
        {
            var copy = (PropertyMappingConfig)MemberwiseClone();
            copy.ExtensionData = new Dictionary<string, JsonElement>(
                ExtensionData, StringComparer.OrdinalIgnoreCase);
            return copy;
        }

        /// <summary>
        /// Returns a copy of the config with all string properties coalesced from
        /// pure-whitespace to <c>null</c>, and a distinct <see cref="ExtensionData"/> dictionary.
        /// </summary>
        public PropertyMappingConfig Normalized()
        {
            var copy = Clone();
            foreach (var field in StringFields)
            {
                var value = field.Get(copy);
                field.Set(copy, string.IsNullOrWhiteSpace(value) ? null : value);
            }
            return copy;
        }

        /// <summary>
        /// Returns a new config with the current schema and all built-in defaults filled in.
        /// Used for first-run and for callers that need an effective mapping.
        /// </summary>
        public static PropertyMappingConfig WithDefaults()
        {
            var config = new PropertyMappingConfig();
            foreach (var field in StringFields)
                field.Set(config, field.Default);
            return config;
        }

        /// <summary>
        /// Returns a new config with built-in defaults, then overlays any non-null values
        /// from <paramref name="overrides"/>.
        /// </summary>
        public static PropertyMappingConfig WithDefaults(PropertyMappingConfig? overrides)
        {
            var merged = WithDefaults();
            if (overrides == null)
                return merged;

            foreach (var field in StringFields)
            {
                var value = field.Get(overrides);
                if (value != null)
                    field.Set(merged, value);
            }
            merged.ExtensionData = overrides.ExtensionData;
            return merged;
        }
    }
}
