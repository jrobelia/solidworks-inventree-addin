using System;

namespace SwInventreeAddin.Logging
{
    /// <summary>
    /// No-op <see cref="IAddinLogger"/> — the default for constructors that do
    /// not need logging assertions, so the seam is never null-checked at the
    /// call site.
    /// </summary>
    public sealed class NullAddinLogger : IAddinLogger
    {
        public void LogInformation(string message) { }

        public void LogError(string message, Exception? exception = null) { }

        public void Dispose() { }
    }
}
