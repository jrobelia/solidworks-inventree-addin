using System.Net.Http;
using System.Threading.Tasks;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public interface ISettingsApplyService
    {
        /// <summary>
        /// Resolves the API key, validates the server URL, and persists the settings.
        /// Throws <see cref="SettingsApplyException"/> when a config or credential step
        /// fails; the message begins with "Failed to save server settings".
        /// </summary>
        Task ApplyAsync(SettingsApplyInput input);

        /// <summary>
        /// Resolves the API key for the supplied <paramref name="input"/> and uses the
        /// provided <paramref name="client"/> to check whether the InvenTree server is
        /// reachable. Throws <see cref="System.InvalidOperationException"/> on failure.
        /// </summary>
        Task TestConnectionAsync(SettingsApplyInput input, HttpClient client);
    }
}
