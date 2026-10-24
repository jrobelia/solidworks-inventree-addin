using System;
using System.Collections.Generic;
using SwInventreeAddin.Logging;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// Test <see cref="IAddinLogger"/>: records every entry so tests can assert
    /// that operations and failures are logged through the seam.
    /// </summary>
    public sealed class StubAddinLogger : IAddinLogger
    {
        public sealed class Entry
        {
            public Entry(string level, string message, Exception? exception)
            {
                Level = level;
                Message = message;
                Exception = exception;
            }

            /// <summary>"Information" or "Error".</summary>
            public string Level { get; }
            public string Message { get; }
            public Exception? Exception { get; }
        }

        /// <summary>Every recorded entry, in call order.</summary>
        public List<Entry> Entries { get; } = new List<Entry>();

        public void LogInformation(string message) =>
            Entries.Add(new Entry("Information", message, null));

        public void LogError(string message, Exception? exception = null) =>
            Entries.Add(new Entry("Error", message, exception));

        public void Dispose() { }
    }
}
