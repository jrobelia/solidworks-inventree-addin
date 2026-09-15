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
        /// Resolves the API key, validates the server URL, tests the connection with
        /// the supplied <paramref name="client"/>, and persists the settings only when
        /// the probe succeeds. Throws <see cref="ArgumentNullException"/> when
        /// <paramref name="client"/> is null. Throws <see cref="SettingsApplyException"/>
        /// when any step fails — validation, credential resolution, the probe, or the
        /// config write; the message begins with "Failed to save server settings" and
        /// nothing is persisted. The caller owns and disposes <paramref name="client"/>
        /// and must not rely on its BaseAddress or headers afterwards.
        /// </summary>
        Task ApplyAsync(SettingsApplyInput input, HttpClient client);

        /// <summary>
        /// Resolves the API key for the supplied <paramref name="input"/> and uses the
        /// provided <paramref name="client"/> to check whether the InvenTree server is
        /// reachable. Throws <see cref="System.InvalidOperationException"/> on failure.
        /// </summary>
        Task TestConnectionAsync(SettingsApplyInput input, HttpClient client);

        /// <summary>
        /// Deletes the saved server settings. Throws <see cref="SettingsApplyException"/>
        /// when deletion fails; the message begins with "Failed to remove server settings".
        /// Completes normally when nothing is saved.
        /// </summary>
        Task RemoveServerConfigAsync();
    }
}
