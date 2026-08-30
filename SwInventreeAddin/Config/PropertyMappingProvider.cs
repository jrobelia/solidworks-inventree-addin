using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

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

        /// <summary>Uses the default %APPDATA% local path and optional source path.</summary>
        public PropertyMappingProvider(string? sourcePath = null)
            : this(DefaultLocalPath(), sourcePath) { }

        /// <summary>Explicit paths — used by tests to avoid touching APPDATA.</summary>
        public PropertyMappingProvider(string localPath, string? sourcePath)
        {
            _localPath  = localPath;
            _sourcePath = sourcePath;
        }

        /// <inheritdoc/>
        public bool IsReadOnly =>
            !string.IsNullOrEmpty(_sourcePath) &&
            File.Exists(_sourcePath);

        /// <inheritdoc/>
        public string LocalFilePath => _localPath;

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
                var defaults = new PropertyMappingConfig();
                SaveMapping(defaults);
                return new MappingResult(MappingHealth.Healthy, defaults);
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
        public PropertyMappingConfig GetMapping()
        {
            var result = GetMappingResult();

            if (result.Health == MappingHealth.Invalid)
                throw new InvalidOperationException(
                    result.ErrorMessage ?? "The mapping configuration is invalid.");

            return result.Config;
        }

        /// <inheritdoc/>
        public void SaveMapping(PropertyMappingConfig config)
        {
            try
            {
                EnsureDirectory(_localPath);
                var json = JsonSerializer.Serialize(config,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_localPath, json, Encoding.UTF8);
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

                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load mapping file: {path}", ex);
            }
        }

        private static MappingResult Classify(PropertyMappingConfig config, string path)
        {
            var duplicate = FindDuplicatePropertyName(config);
            if (duplicate != null)
                return new MappingResult(
                    MappingHealth.Invalid,
                    config,
                    $"Invalid mapping file: {path}. {duplicate}");

            if (string.Equals(
                    config.SchemaVersion,
                    PropertyMappingConfig.CurrentSchemaVersion,
                    StringComparison.Ordinal))
                return new MappingResult(MappingHealth.Healthy, config);

            return new MappingResult(MappingHealth.NeedsUpgrade, config);
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
            Add(config.PkProperty,         "InvenTree PK");

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
