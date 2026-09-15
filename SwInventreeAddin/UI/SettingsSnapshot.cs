using System;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// A value snapshot of every editable Settings field. The dialog compares the current
    /// snapshot against the saved one to decide whether Apply/Save are enabled.
    /// </summary>
    internal struct SettingsSnapshot : IEquatable<SettingsSnapshot>
    {
        public SettingsSnapshot(string url, string apiKey, string username, string password,
                                string sharedPath, string bomKeyword, bool useLocalMapping,
                                CredentialEntryMode mode)
        {
            Url = url;
            ApiKey = apiKey;
            Username = username;
            Password = password;
            SharedPath = sharedPath;
            BomKeyword = bomKeyword;
            UseLocalMapping = useLocalMapping;
            Mode = mode;
        }

        public string Url { get; }

        public string ApiKey { get; }

        public string Username { get; }

        public string Password { get; }

        public string SharedPath { get; }

        public string BomKeyword { get; }

        public bool UseLocalMapping { get; }

        public CredentialEntryMode Mode { get; }

        public static bool operator ==(SettingsSnapshot left, SettingsSnapshot right) => left.Equals(right);

        public static bool operator !=(SettingsSnapshot left, SettingsSnapshot right) => !left.Equals(right);

        public bool Equals(SettingsSnapshot other) =>
            string.Equals(Url, other.Url, StringComparison.Ordinal)
            && string.Equals(ApiKey, other.ApiKey, StringComparison.Ordinal)
            && string.Equals(Username, other.Username, StringComparison.Ordinal)
            && string.Equals(Password, other.Password, StringComparison.Ordinal)
            && string.Equals(SharedPath, other.SharedPath, StringComparison.Ordinal)
            && string.Equals(BomKeyword, other.BomKeyword, StringComparison.Ordinal)
            && UseLocalMapping == other.UseLocalMapping
            && Mode == other.Mode;

        public override bool Equals(object? obj) => obj is SettingsSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Url ?? string.Empty).GetHashCode();
                hash = (hash * 31) + (ApiKey ?? string.Empty).GetHashCode();
                hash = (hash * 31) + (Username ?? string.Empty).GetHashCode();
                hash = (hash * 31) + (Password ?? string.Empty).GetHashCode();
                hash = (hash * 31) + (SharedPath ?? string.Empty).GetHashCode();
                hash = (hash * 31) + (BomKeyword ?? string.Empty).GetHashCode();
                hash = (hash * 31) + UseLocalMapping.GetHashCode();
                hash = (hash * 31) + Mode.GetHashCode();
                return hash;
            }
        }
    }
}
