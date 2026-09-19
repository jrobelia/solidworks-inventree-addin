namespace SwInventreeAddin.Config
{
    /// <summary>
    /// How a probe of the InvenTree server ended. <see cref="Unreachable"/> means the
    /// request never got a response; <see cref="NotConfigured"/> and
    /// <see cref="NotProbed"/> mean no probe ran at all; the other values mean the
    /// server answered.
    /// </summary>
    public enum ConnectionProbeStatus
    {
        /// <summary>The server answered the probe with a success status (2xx).</summary>
        Connected,

        /// <summary>The request never got a response — the server could not be reached.</summary>
        Unreachable,

        /// <summary>The server answered 401/403 — the credential was rejected.</summary>
        CredentialRejected,

        /// <summary>The server answered with any other non-success status.</summary>
        ServerError,

        /// <summary>
        /// No server URL was saved, so nothing was probed — the add-in is unconfigured.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// The save deliberately skipped the probe — no connection-relevant field
        /// changed since the last save (#249). A no-verdict result: nothing was
        /// probed and the card must not treat it as a probe outcome.
        /// </summary>
        NotProbed,
    }
}
