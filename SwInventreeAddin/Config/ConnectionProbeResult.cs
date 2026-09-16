namespace SwInventreeAddin.Config
{
    /// <summary>
    /// The outcome of probing the InvenTree server with a credential, returned by
    /// <see cref="ISettingsApplyService.ApplyAsync"/> and
    /// <see cref="ISettingsApplyService.TestConnectionAsync"/>. <see cref="Message"/>
    /// is user-facing and never contains the API key (ADR-0022).
    /// </summary>
    public class ConnectionProbeResult
    {
        /// <summary>Creates a result with the given status and user-facing detail.</summary>
        public ConnectionProbeResult(ConnectionProbeStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        /// <summary>How the probe ended.</summary>
        public ConnectionProbeStatus Status { get; }

        /// <summary>User-facing detail suitable for a status bar. Never contains the API key.</summary>
        public string Message { get; }

        /// <summary>True only when the probe connected.</summary>
        public bool Succeeded => Status == ConnectionProbeStatus.Connected;
    }
}
