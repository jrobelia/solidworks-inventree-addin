using System;

namespace SwInventreeAddin.Logging
{
    /// <summary>
    /// The cross-cutting diagnostic seam (ADR-0020): a small interface behind
    /// which the file adapter hides path resolution, size-capped rollover,
    /// secret redaction, formatting, and thread safety. Created once by the
    /// composition root and passed down per the "accept dependencies, don't
    /// create them" rule.
    /// </summary>
    /// <remarks>
    /// Implementations are thread-safe and must never throw into the caller —
    /// a logging failure inside a SolidWorks COM callback or async continuation
    /// has no recovery path. Info-level messages must not carry COM or interop
    /// internals; error-level messages may include exception detail. Callers
    /// must never intentionally log secrets; the file adapter additionally
    /// redacts registered secret strings.
    /// </remarks>
    public interface IAddinLogger : IDisposable
    {
        /// <summary>Records an informational operational event (operation start, typed outcome).</summary>
        void LogInformation(string message);

        /// <summary>Records a failure, optionally with exception detail.</summary>
        void LogError(string message, Exception? exception = null);
    }
}
