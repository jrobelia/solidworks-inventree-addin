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
    /// Default storage path: %APPDATA%\OA InvenTree Addin\settings.dat
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
                return JsonSerializer.Deserialize<ServerConfig>(json);
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

        private static string DefaultFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "OA InvenTree Addin", "settings.dat");
        }
    }
}
