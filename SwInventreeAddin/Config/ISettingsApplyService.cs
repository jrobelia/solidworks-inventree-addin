using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public interface ISettingsApplyService
    {
        /// <summary>
        /// Resolves the API key, validates the server URL, persists the settings, then
        /// probes the server with the persisted credential and reports the outcome.
        /// A normal return means the settings were persisted — a failed probe never
        /// throws and never rolls back the save. When the input resolves to no
        /// credential at all, the URL-only config is still persisted ("server only" is
        /// a valid saved state); the returned result reports
        /// <see cref="ConnectionProbeStatus.CredentialRejected"/> without a probe being
        /// made. Throws <see cref="ArgumentNullException"/>
        /// when <paramref name="client"/> is null. Throws <see cref="SettingsApplyException"/>
        /// when any pre-persistence step fails — validation, credential resolution, or
        /// the config write; the message begins with "Failed to save server settings" and
        /// nothing is persisted. The caller owns and disposes <paramref name="client"/>
        /// and must not rely on its BaseAddress or headers afterwards.
        /// </summary>
        Task<ConnectionProbeResult> ApplyAsync(SettingsApplyInput input, HttpClient client);

        /// <summary>
        /// Resolves the API key for the supplied <paramref name="input"/> and probes the
        /// server with it exactly as <see cref="ApplyAsync"/> does, but writes nothing.
        /// A normal return means the probe ran. Every probe is bounded to a few
        /// seconds internally — a timeout returns an Unreachable result rather than
        /// throwing. <paramref name="cancellationToken"/> is a lifecycle signal only:
        /// when it fires, <see cref="OperationCanceledException"/> propagates
        /// unclassified instead of a result. Callers without a lifecycle pass
        /// <see cref="CancellationToken.None"/> and still get the bound.
        /// Throws <see cref="ArgumentNullException"/>
        /// when <paramref name="client"/> is null and
        /// <see cref="System.InvalidOperationException"/> when the probe cannot be
        /// attempted — invalid URL, missing credential, or token-resolution failure.
        /// The caller owns and disposes <paramref name="client"/> and must not rely on
        /// its BaseAddress or headers afterwards.
        /// </summary>
        Task<ConnectionProbeResult> TestConnectionAsync(SettingsApplyInput input, HttpClient client,
            CancellationToken cancellationToken);

        /// <summary>
        /// Clears only the saved API key; the server URL, Property Mapping path, BOM
        /// keyword, and IPN flag survive. A no-op when nothing is saved. Throws
        /// <see cref="SettingsApplyException"/> when the provider fails; the message
        /// begins with "Failed to remove the API key".
        /// </summary>
        Task RemoveApiKeyAsync();
    }
}
