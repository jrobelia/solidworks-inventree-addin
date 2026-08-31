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
        /// <returns>Null if the settings file does not exist.</returns>
        public ServerConfig? GetServerConfig()
        {
            if (!File.Exists(_filePath))
                return null;

            byte[] cipherBytes;
            try
            {
                cipherBytes = File.ReadAllBytes(_filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not read settings file at {_filePath}: {ex.Message}", ex);
            }

            byte[] plainBytes;
            try
            {
                plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Settings file is corrupt or was created by a different user account.", ex);
            }

            try
            {
                var json = Encoding.UTF8.GetString(plainBytes);
                var config = JsonSerializer.Deserialize<ServerConfig>(json);
                MigrateLegacyWaitForAutoPartNumber(config, json);
                return config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Settings file could not be deserialised after decryption.", ex);
            }
        }

        // No-longer-needed explicit interface bridge removed: interface now returns ServerConfig? directly.

        /// <inheritdoc/>
        public void SaveServerConfig(ServerConfig config)
        {
            var json       = JsonSerializer.Serialize(config);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var cipher     = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(_filePath, cipher);
        }

        /// <summary>
        /// Copies the legacy <c>WaitForAutoPartNumber</c> value into
        /// <see cref="ServerConfig.WaitForServerAssignedIpn"/> when the new key
        /// is missing from the encrypted file.
        /// </summary>
        private static void MigrateLegacyWaitForAutoPartNumber(ServerConfig? config, string json)
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
