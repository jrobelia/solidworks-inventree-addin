namespace SwInventreeAddin.Config
{
    /// <summary>
    /// How a probe of the InvenTree server ended. <see cref="Unreachable"/> means the
    /// request never got a response; the other values mean the server answered.
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
    }
}
