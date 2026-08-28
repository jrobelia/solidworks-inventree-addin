using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Thrown by <see cref="ISettingsApplyService"/> to report a failure the
    /// <see cref="UI.SettingsWindow"/> should surface as a status message.
    /// </summary>
    public class SettingsApplyException : InvalidOperationException
    {
        /// <summary>
        /// Creates a new exception with the given human-readable message.
        /// </summary>
        public SettingsApplyException(string message)
            : base(message) { }

        /// <summary>
        /// Creates a new exception with the given message and inner exception.
        /// </summary>
        public SettingsApplyException(string message, Exception inner)
            : base(message, inner) { }
    }
}
