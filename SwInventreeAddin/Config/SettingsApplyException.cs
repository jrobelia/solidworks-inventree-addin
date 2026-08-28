using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Thrown by <see cref="ISettingsApplyService"/> to report a failure the
    /// <see cref="UI.SettingsWindow"/> should surface as a status message.
    /// </summary>
    public class SettingsApplyException : InvalidOperationException
    {
        /// <summary>Classifies the failure for status-message selection.</summary>
        public SettingsApplyErrorKind ErrorKind { get; }

        /// <summary>
        /// Creates a new exception with the given human-readable message and error kind.
        /// </summary>
        public SettingsApplyException(string message, SettingsApplyErrorKind kind)
            : base(message)
        {
            ErrorKind = kind;
        }

        /// <summary>
        /// Creates a new exception with the given message, error kind, and inner exception.
        /// </summary>
        public SettingsApplyException(string message, SettingsApplyErrorKind kind, Exception inner)
            : base(message, inner)
        {
            ErrorKind = kind;
        }
    }
}
