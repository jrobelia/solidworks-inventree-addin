using System;
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
        public PropertyMappingConfig GetMapping()
        {
            // Source path takes priority when configured and the file exists.
            if (!string.IsNullOrEmpty(_sourcePath) && File.Exists(_sourcePath))
                return Fetch(_sourcePath!);

            if (File.Exists(_localPath))
                return Fetch(_localPath);

            // First run — write defaults so the user has a file to edit.
            var defaults = new PropertyMappingConfig();
            try
            {
                SaveMapping(defaults);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load mapping file: {_localPath}", ex);
            }

            return defaults;
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

                if (string.Compare(config.SchemaVersion, PropertyMappingConfig.CurrentSchemaVersion, StringComparison.Ordinal) < 0)
                {
                    var defaults = new PropertyMappingConfig();
                    if (string.IsNullOrEmpty(config.BomColumnIpn))       config.BomColumnIpn       = defaults.BomColumnIpn;
                    if (string.IsNullOrEmpty(config.BomColumnQty))       config.BomColumnQty       = defaults.BomColumnQty;
                    if (string.IsNullOrEmpty(config.BomColumnReference)) config.BomColumnReference = defaults.BomColumnReference;
                    if (string.IsNullOrEmpty(config.BomColumnNote))      config.BomColumnNote      = defaults.BomColumnNote;
                }

                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load mapping file: {path}", ex);
            }
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
