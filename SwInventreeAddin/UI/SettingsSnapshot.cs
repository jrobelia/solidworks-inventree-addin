namespace SwInventreeAddin.UI
{
    /// <summary>
    /// A value snapshot of every editable Settings field. The dialog compares the
    /// current snapshot against the saved one via <see cref="HasPersistableChangeFrom"/>
    /// to decide whether Apply/Save are enabled — the single home of the
    /// dirty-gating rules, so no field-changed handler re-implements them.
    /// </summary>
    internal struct SettingsSnapshot
    {
        public SettingsSnapshot(
            string url, string apiKeyDraft, bool hasSavedApiKey,
            string username, string password,
            string sharedPath, string bomKeyword, bool useLocalMapping,
            bool waitForServerAssignedIpn)
        {
            Url = url;
            ApiKeyDraft = apiKeyDraft;
            HasSavedApiKey = hasSavedApiKey;
            Username = username;
            Password = password;
            SharedPath = sharedPath;
            BomKeyword = bomKeyword;
            UseLocalMapping = useLocalMapping;
            WaitForServerAssignedIpn = waitForServerAssignedIpn;
        }

        public string Url { get; }

        /// <summary>The typed key draft only — never the saved key or a placeholder.</summary>
        public string ApiKeyDraft { get; }

        /// <summary>Whether a key is persisted; its value never enters the snapshot.</summary>
        public bool HasSavedApiKey { get; }

        public string Username { get; }

        public string Password { get; }

        public string SharedPath { get; }

        public string BomKeyword { get; }

        public bool UseLocalMapping { get; }

        public bool WaitForServerAssignedIpn { get; }

        /// <summary>
        /// True when this snapshot differs from <paramref name="saved"/> in a way Apply
        /// can persist: the URL or any non-credential field changed, the key draft is
        /// non-blank, saved-key presence changed, or a complete non-blank
        /// username/password pair was typed. Blank or half-typed credential fields
        /// never count — a stray half-pair must not enable Apply.
        /// </summary>
        public bool HasPersistableChangeFrom(SettingsSnapshot saved)
        {
            if (!string.Equals(Url, saved.Url, System.StringComparison.Ordinal)
                || !string.Equals(SharedPath, saved.SharedPath, System.StringComparison.Ordinal)
                || !string.Equals(BomKeyword, saved.BomKeyword, System.StringComparison.Ordinal)
                || UseLocalMapping != saved.UseLocalMapping
                || WaitForServerAssignedIpn != saved.WaitForServerAssignedIpn)
            {
                return true;
            }

            if (HasSavedApiKey != saved.HasSavedApiKey)
                return true;

            if (!string.IsNullOrWhiteSpace(ApiKeyDraft))
                return true;

            return !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password);
        }
    }
}
