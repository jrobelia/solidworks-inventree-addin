using System.Threading.Tasks;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public interface ISettingsApplyService
    {
        /// <summary>
        /// Resolves the API key and validates the server URL.
        /// Throws <see cref="System.InvalidOperationException"/> for credential or URL errors.
        /// </summary>
        Task<string> ResolveApiKeyAsync(SettingsApplyInput input);

        /// <summary>
        /// Resolves the API key, validates the server URL, and persists the settings.
        /// Throws <see cref="SettingsApplyException"/> with <see cref="SettingsApplyErrorKind.Config"/>
        /// when a config or credential step fails; the message begins with
        /// "Failed to save server settings".
        /// </summary>
        Task ApplyAsync(SettingsApplyInput input);
    }
}
