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
    ///   1. Local file exists → use it (editable).
    ///   2. Source path configured and file exists → use it (read-only).
    ///   3. Neither → write defaults to local path and return them (first run).
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
                return Load(_sourcePath);

            if (File.Exists(_localPath))
                return Load(_localPath);

            // First run — write defaults so the user has a file to edit.
            var defaults = new PropertyMappingConfig();
            SaveMapping(defaults);
            return defaults;
        }

        /// <inheritdoc/>
        public void SaveMapping(PropertyMappingConfig config)
        {
            EnsureDirectory(_localPath);
            var json = JsonSerializer.Serialize(config,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_localPath, json, Encoding.UTF8);
        }

        /// <inheritdoc/>
        public void CopyToLocal()
        {
            if (string.IsNullOrEmpty(_sourcePath) || !File.Exists(_sourcePath))
                throw new InvalidOperationException(
                    "No source path is configured or the source file does not exist.");

            var config = Load(_sourcePath);
            SaveMapping(config);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static PropertyMappingConfig Load(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<PropertyMappingConfig>(json)
                ?? new PropertyMappingConfig();
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
