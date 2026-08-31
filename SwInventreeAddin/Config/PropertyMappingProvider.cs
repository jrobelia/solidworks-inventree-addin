using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Loads and saves the property-name mapping from/to a local JSON file.
    ///
    /// Resolution order:
    ///   1. Source path configured and file exists → use it (read-only).
    ///   2. Local file exists → use it (editable).
    ///   3. Neither → write defaults to local path and return them (first run).
    ///
    /// File I/O, JSON, and access failures are wrapped in
    /// <see cref="InvalidOperationException"/> messages that name the offending path.
    /// </summary>
    public class PropertyMappingProvider : IPropertyMappingProvider
    {
        private readonly string  _localPath;
        private readonly string? _sourcePath;
        private readonly JsonSerializerOptions _saveOptions;

        /// <summary>Uses the default %APPDATA% local path and optional source path.</summary>
        public PropertyMappingProvider(string? sourcePath = null)
            : this(DefaultLocalPath(), sourcePath) { }

        /// <summary>Explicit paths — used by tests to avoid touching APPDATA.</summary>
        public PropertyMappingProvider(string localPath, string? sourcePath)
        {
            _localPath  = localPath;
            _sourcePath = sourcePath;
            _saveOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <inheritdoc/>
        public bool IsReadOnly =>
            !string.IsNullOrEmpty(_sourcePath) &&
            File.Exists(_sourcePath);

        /// <inheritdoc/>
        public string LocalFilePath => _localPath;

        /// <inheritdoc/>
        public event EventHandler? MappingChanged;

        /// <inheritdoc/>
        public MappingResult GetMappingResult()
        {
            string? resolvedPath = null;

            try
            {
                // Source path takes priority when configured and the file exists.
                if (!string.IsNullOrEmpty(_sourcePath) && File.Exists(_sourcePath))
                {
                    resolvedPath = _sourcePath;
                    return Classify(Fetch(resolvedPath!), resolvedPath!);
                }

                if (File.Exists(_localPath))
                {
                    resolvedPath = _localPath;
                    return Classify(Fetch(resolvedPath), resolvedPath);
                }

                // First run — write defaults so the user has a file to edit.
                resolvedPath = _localPath;
                var defaults = PropertyMappingConfig.WithDefaults();
                SaveMapping(defaults);
                return new MappingResult(MappingHealth.Healthy, defaults, MappingResult.GetDefaultMessage(MappingHealth.Healthy));
            }
            catch (Exception ex)
            {
                var message = ex is InvalidOperationException
                    ? ex.Message
                    : $"Failed to load mapping file: {resolvedPath ?? _localPath}";

                return new MappingResult(MappingHealth.Invalid, new PropertyMappingConfig(), message);
            }
        }

        /// <inheritdoc/>
        public MappingResult ValidateMapping(PropertyMappingConfig config)
            => Classify(config, _localPath);

        /// <inheritdoc/>
        public void SaveMapping(PropertyMappingConfig config)
        {
            try
            {
                EnsureDirectory(_localPath);
                var normalized = NormalizeForSave(config);
                var json = JsonSerializer.Serialize(normalized, _saveOptions);
                File.WriteAllText(_localPath, json, Encoding.UTF8);
                MappingChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to save mapping file: {_localPath}", ex);
            }
        }

        /// <inheritdoc/>
        public void CopyToLocal()
        {
            if (string.IsNullOrEmpty(_sourcePath) || !File.Exists(_sourcePath))
                throw new InvalidOperationException(
                    "No source path is configured or the source file does not exist.");

            var config = Fetch(_sourcePath!);
            SaveMapping(config);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static PropertyMappingConfig Fetch(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            try
            {
                var json   = File.ReadAllText(path, Encoding.UTF8);
                var config = JsonSerializer.Deserialize<PropertyMappingConfig>(json)
                    ?? new PropertyMappingConfig();

                config.ExtensionData ??= new Dictionary<string, JsonElement>();
                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load mapping file: {path}", ex);
            }
        }

        internal static MappingResult Classify(PropertyMappingConfig config, string path)
        {
            var duplicate = FindDuplicatePropertyName(config);
            if (duplicate != null)
            {
                var location = string.IsNullOrWhiteSpace(path) ? "" : $"Invalid mapping file: {path}. ";
                return new MappingResult(MappingHealth.Invalid, config, $"{location}{duplicate}");
            }

            var currentVersion = PropertyMappingConfig.CurrentSchemaVersion;

            var comparison = CompareSchemaVersions(config.SchemaVersion, currentVersion);
            if (comparison == 0)
                return new MappingResult(MappingHealth.Healthy, config, MappingResult.GetDefaultMessage(MappingHealth.Healthy));

            if (comparison > 0)
                return new MappingResult(
                    MappingHealth.NewerSchema,
                    config,
                    MappingResult.GetDefaultMessage(MappingHealth.NewerSchema));

            // Older, unversioned (null/empty), or unparseable non-empty schema version.
            if (comparison < 0 || string.IsNullOrWhiteSpace(config.SchemaVersion))
                return new MappingResult(
                    MappingHealth.NeedsUpgrade,
                    config,
                    MappingResult.GetDefaultMessage(MappingHealth.NeedsUpgrade));

            var badVersionLocation = string.IsNullOrWhiteSpace(path) ? "" : $"Invalid mapping file: {path}. ";
            return new MappingResult(
                MappingHealth.Invalid,
                config,
                $"{badVersionLocation}Unrecognized schema version '{config.SchemaVersion}'.");
        }

        private static int? CompareSchemaVersions(string? fileVersion, string currentVersion)
        {
            var fileVer    = TryParseSchemaVersion(fileVersion);
            var currentVer = TryParseSchemaVersion(currentVersion);

            if (fileVer == null || currentVer == null)
                return null;

            return new Version(fileVer!.Major, fileVer.Minor)
                .CompareTo(new Version(currentVer!.Major, currentVer.Minor));
        }

        /// <summary>
        /// Parses a schema version string for comparison.
        /// Single-component versions like "3" are normalized to "3.0" so that
        /// <see cref="Version"/> can compare them. Only major and minor are kept so
        /// "3", "3.0" and "3.0.0" are treated as the same schema release while "3.1"
        /// is correctly seen as newer.
        /// </summary>
        private static Version? TryParseSchemaVersion(string? version)
        {
            if (version == null || string.IsNullOrWhiteSpace(version))
                return null;

            var padded = version.Contains(".") ? version : version + ".0";

            if (Version.TryParse(padded, out var parsed))
                return new Version(parsed.Major, parsed.Minor);

            return null;
        }

        private static string? FindDuplicatePropertyName(PropertyMappingConfig config)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            void Add(string? name, string role)
            {
                if (name == null || string.IsNullOrWhiteSpace(name))
                    return;

                var key = name.Trim();
                if (!map.TryGetValue(key, out var roles))
                {
                    roles = new List<string>();
                    map[key] = roles;
                }

                roles.Add(role);
            }

            Add(config.IpnProperty,        "IPN");
            Add(config.NameProperty,       "Name");
            Add(config.NotesProperty,      "Notes");
            Add(config.RevisionProperty,   "Revision");
            Add(config.DescriptionProperty,"Description");
            Add(config.PkProperty,         "InvenTree Part PK");

            foreach (var kvp in map)
            {
                if (kvp.Value.Count > 1)
                {
                    return $"Duplicate SolidWorks Document Property name '{kvp.Key}' " +
                           $"is used by {string.Join(" and ", kvp.Value)}.";
                }
            }

            return null;
        }

        private static PropertyMappingConfig NormalizeForSave(PropertyMappingConfig config)
        {
            static string? NullIfWhiteSpace(string? value) =>
                string.IsNullOrWhiteSpace(value) ? null : value;

            return new PropertyMappingConfig
            {
                SchemaVersion       = NullIfWhiteSpace(config.SchemaVersion),
                IpnProperty         = NullIfWhiteSpace(config.IpnProperty),
                NameProperty        = NullIfWhiteSpace(config.NameProperty),
                NotesProperty       = NullIfWhiteSpace(config.NotesProperty),
                RevisionProperty    = NullIfWhiteSpace(config.RevisionProperty),
                DescriptionProperty = NullIfWhiteSpace(config.DescriptionProperty),
                PkProperty          = NullIfWhiteSpace(config.PkProperty),
                BomColumnIpn        = NullIfWhiteSpace(config.BomColumnIpn),
                BomColumnQty        = NullIfWhiteSpace(config.BomColumnQty),
                BomColumnReference  = NullIfWhiteSpace(config.BomColumnReference),
                BomColumnNote       = NullIfWhiteSpace(config.BomColumnNote),
                ExtensionData       = config.ExtensionData
            };
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static string DefaultLocalPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SwInventreeAddin", "sw_inventree_property_mappings.json");
        }
    }
}
