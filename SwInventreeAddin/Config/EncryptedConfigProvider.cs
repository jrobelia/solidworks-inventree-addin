using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Stores server credentials encrypted with Windows DPAPI (current-user scope).
    /// The file is not portable between machines or user accounts.
    /// Default storage path: %APPDATA%\SwInventreeAddin\settings.dat
    /// </summary>
    public class EncryptedConfigProvider : IConfigProvider
    {
        private readonly string _filePath;

        public EncryptedConfigProvider()
            : this(DefaultFilePath()) { }

        public EncryptedConfigProvider(string filePath)
        {
            _filePath = filePath;
        }

        /// <inheritdoc/>
        /// <returns>
        /// Null if the settings file does not exist or cannot be read, decrypted,
        /// or deserialised — a corrupt or non-portable file means "not configured",
        /// never an exception that blocks add-in load.
        /// </returns>
        public ServerConfig? GetServerConfig()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return null;

                var cipherBytes = File.ReadAllBytes(_filePath);
                var plainBytes = ProtectedData.Unprotect(
                    cipherBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plainBytes);
                var config = JsonSerializer.Deserialize<ServerConfig>(json);
                MigrateLegacyWaitForServerAssignedIpn(config, json);

                // Defence in depth: the save/apply path only writes https URLs,
                // but a stored value is untrusted after load — a non-https or
                // unparseable URL must not reach client construction.
                if (config != null && !string.IsNullOrEmpty(config.Url) &&
                    !(config.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                      Uri.TryCreate(config.Url, UriKind.Absolute, out _)))
                    config.Url = string.Empty;

                return config;
            }
            catch
            {
                return null;
            }
        }

        // No-longer-needed explicit interface bridge removed: interface now returns ServerConfig? directly.

        /// <inheritdoc/>
        public void SaveServerConfig(ServerConfig config)
        {
            var json = JsonSerializer.Serialize(config);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var cipher = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(_filePath, cipher);
        }

        /// <inheritdoc/>
        public void DeleteServerConfig()
        {
            if (!File.Exists(_filePath))
                return;

            try
            {
                File.Delete(_filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not delete settings file at {_filePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Copies the legacy <c>WaitForAutoPartNumber</c> JSON value into
        /// <see cref="ServerConfig.WaitForServerAssignedIpn"/> when the new key
        /// is missing from the encrypted file.
        /// </summary>
        private static void MigrateLegacyWaitForServerAssignedIpn(ServerConfig? config, string json)
        {
            if (config == null) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("WaitForAutoPartNumber", out var legacyValue) &&
                !root.TryGetProperty("WaitForServerAssignedIpn", out _))
            {
                config.WaitForServerAssignedIpn = legacyValue.GetBoolean();
            }
        }

        private static string DefaultFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SwInventreeAddin", "settings.dat");
        }
    }
}
