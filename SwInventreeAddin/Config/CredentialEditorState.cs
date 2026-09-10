namespace SwInventreeAddin.Config
{
    /// <summary>Which credential form the Settings dialog is editing.</summary>
    public enum CredentialEntryMode
    {
        /// <summary>Sign in with an InvenTree username and password.</summary>
        Account,

        /// <summary>Paste an InvenTree API key directly.</summary>
        ApiKey,
    }

    /// <summary>
    /// The editable credential state behind the Settings dialog's mode switcher: which
    /// entry mode is selected, whether the API key is revealed, and the key itself. The
    /// key value lives here rather than in a control so the masked and revealed fields
    /// always agree (ADR-0022).
    /// </summary>
    public class CredentialEditorState
    {
        private string _apiKey;

        private CredentialEditorState(CredentialEntryMode mode, string apiKey)
        {
            Mode = mode;
            _apiKey = apiKey;
        }

        /// <summary>
        /// Seeds the editor from the saved server configuration. A saved API key preselects
        /// <see cref="CredentialEntryMode.ApiKey"/> and populates the field; anything else
        /// starts on <see cref="CredentialEntryMode.Account"/>. The key always starts masked.
        /// </summary>
        public static CredentialEditorState FromSavedConfig(ServerConfig? config)
        {
            string savedKey = (config?.ApiKey ?? string.Empty).Trim();

            return savedKey.Length == 0
                ? new CredentialEditorState(CredentialEntryMode.Account, string.Empty)
                : new CredentialEditorState(CredentialEntryMode.ApiKey, savedKey);
        }

        /// <summary>The credential form the engineer is currently editing.</summary>
        public CredentialEntryMode Mode { get; private set; }

        /// <summary>True while the API key is shown in plain text instead of masked.</summary>
        public bool IsApiKeyRevealed { get; private set; }

        /// <summary>The API key being edited. Never null.</summary>
        public string ApiKey
        {
            get => _apiKey;
            set => _apiKey = value ?? string.Empty;
        }

        /// <summary>
        /// Switches the visible credential form. Re-masks the API key so it is never
        /// left revealed by a mode change.
        /// </summary>
        public void SelectMode(CredentialEntryMode mode)
        {
            Mode = mode;
            IsApiKeyRevealed = false;
        }

        /// <summary>Flips the API key between masked and plain text.</summary>
        public void ToggleApiKeyReveal() => IsApiKeyRevealed = !IsApiKeyRevealed;
    }
}
