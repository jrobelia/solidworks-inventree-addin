namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The credential fields' draft state in Settings. The saved key is write-once
    /// (ADR-0022): it is held only so <see cref="ApplyCredentialTo"/> can reuse it —
    /// it is never seeded into <see cref="ApiKey"/>, never a placeholder value, and
    /// never readable back. On Apply the window calls <see cref="ApplyCredentialTo"/>
    /// and clears the draft, so a typed key cannot linger where it might persist twice.
    /// </summary>
    public class CredentialEditorState
    {
        private string _savedApiKey;
        private string _apiKeyDraft = string.Empty;

        private CredentialEditorState(string savedApiKey, string apiKeyDraft)
        {
            _savedApiKey = savedApiKey;
            HasSavedApiKey = savedApiKey.Length > 0;
            ApiKey = apiKeyDraft;
        }

        /// <summary>A saved key exists. The window uses this for the dots placeholder and dirty-check — never to read the key.</summary>
        public bool HasSavedApiKey { get; private set; }

        /// <summary>Typed API-key draft only — never contains the saved key; null normalises to empty.</summary>
        public string ApiKey
        {
            get => _apiKeyDraft;
            set => _apiKeyDraft = value ?? string.Empty;
        }

        /// <summary>Builds state from what is persisted; <see cref="ApiKey"/> always starts empty.</summary>
        public static CredentialEditorState FromSavedConfig(ServerConfig? config)
        {
            string saved = (config?.ApiKey ?? string.Empty).Trim();
            return new CredentialEditorState(saved, string.Empty);
        }

        /// <summary>
        /// Writes the effective credential into <paramref name="input"/> following the
        /// seam's precedence: a non-blank typed key draft wins and clears the pair;
        /// a complete non-blank username/password pair is next (it overrides the saved
        /// key so account auth is one Apply, no Remove-first detour); otherwise the
        /// saved key is reused so a URL-only edit still probes with it. Half-typed
        /// pairs are ignored so a stray field cannot wipe a saved credential.
        /// </summary>
        public void ApplyCredentialTo(SettingsApplyInput input, string username, string password)
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                input.RawApiKey = ApiKey.Trim();
                input.Username = string.Empty;
                input.Password = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                input.RawApiKey = string.Empty;
                input.Username = username.Trim();
                input.Password = password;
            }
            else if (HasSavedApiKey)
            {
                input.RawApiKey = _savedApiKey;
                input.Username = string.Empty;
                input.Password = string.Empty;
            }
            else
            {
                input.RawApiKey = string.Empty;
                input.Username = string.Empty;
                input.Password = string.Empty;
            }
        }

        /// <summary>
        /// Drops the saved key and any draft — called after
        /// <see cref="ISettingsApplyService.RemoveApiKeyAsync"/> and the config re-read,
        /// so the state lands on Authentication required.
        /// </summary>
        public void Clear()
        {
            _savedApiKey = string.Empty;
            HasSavedApiKey = false;
            ApiKey = string.Empty;
        }
    }
}
