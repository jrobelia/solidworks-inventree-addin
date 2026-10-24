using System;
using System.Collections.Generic;
using System.IO;

namespace SwInventreeAddin.Logging
{
    /// <summary>
    /// Production <see cref="IAddinLogger"/> (ADR-0020): appends timestamped
    /// entries to <c>inventree-addin.log</c> under
    /// <c>%LOCALAPPDATA%\SwInventreeAddin\logs</c>, rolling at
    /// <see cref="MaxFileBytes"/> and retaining <see cref="RetainedFileCount"/>
    /// files total — the oldest is overwritten. Thread-safe; all file I/O is
    /// best-effort so a logging failure can never reach the SolidWorks host.
    /// Registered secrets are redacted from message and exception detail
    /// before the entry is written.
    /// </summary>
    public sealed class FileAddinLogger : IAddinLogger
    {
        /// <summary>The log file name inside the log directory.</summary>
        public const string LogFileName = "inventree-addin.log";

        /// <summary>The active file rolls to an archive once it reaches 1 MB.</summary>
        public const long MaxFileBytes = 1024L * 1024L;

        /// <summary>Total files kept: the active file plus its archives.</summary>
        public const int RetainedFileCount = 3;

        private const string Redacted = "***";

        private readonly string _logPath;
        private readonly object _gate = new object();
        private readonly List<string> _secrets = new List<string>();
        private bool _disposed;

        /// <summary>
        /// Creates the logger. <paramref name="secrets"/> are registered for
        /// redaction; <paramref name="logDirectory"/> overrides the default
        /// <c>%LOCALAPPDATA%\SwInventreeAddin\logs</c> directory (tests use a
        /// temporary directory). The constructor performs no file I/O — the
        /// directory is created on the first write.
        /// </summary>
        public FileAddinLogger(IEnumerable<string?>? secrets = null, string? logDirectory = null)
        {
            var directory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwInventreeAddin", "logs");
            _logPath = Path.Combine(directory, LogFileName);
            if (secrets != null)
                foreach (var secret in secrets)
                    RegisterSecret(secret);
        }

        /// <summary>The full path of the active log file.</summary>
        public string LogPath => _logPath;

        /// <summary>
        /// Registers a string that must never be written — every occurrence,
        /// anywhere in an entry, is replaced with <c>***</c>. Null or empty
        /// values are ignored. Registered secrets accumulate for the life of
        /// the logger: an old key staying redacted is harmless, an un-redacted
        /// key is a leak.
        /// </summary>
        public void RegisterSecret(string? secret)
        {
            if (string.IsNullOrEmpty(secret)) return;
            lock (_gate)
            {
                if (!_secrets.Contains(secret!))
                    _secrets.Add(secret!);
            }
        }

        public void LogInformation(string message) => Write("INFO", message, null);

        public void LogError(string message, Exception? exception = null) =>
            Write("ERROR", message, exception);

        private void Write(string level, string message, Exception? exception)
        {
            lock (_gate)
            {
                if (_disposed) return;

                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception != null)
                    entry += Environment.NewLine + exception;
                entry = Redact(entry) + Environment.NewLine;

                try
                {
                    RollIfNeeded();
                    var directory = Path.GetDirectoryName(_logPath)!;
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(_logPath, entry);
                }
                catch
                {
                    // Best-effort diagnostics: a full disk, a permissions
                    // problem, or a locked file must never reach the host.
                }
            }
        }

        /// <summary>
        /// When the active file reaches <see cref="MaxFileBytes"/>, shifts the
        /// archives down one slot — <c>.2.log</c> is overwritten by
        /// <c>.1.log</c>, <c>.1.log</c> by the active file — keeping exactly
        /// <see cref="RetainedFileCount"/> files.
        /// </summary>
        private void RollIfNeeded()
        {
            if (!File.Exists(_logPath) || new FileInfo(_logPath).Length < MaxFileBytes)
                return;

            for (var i = RetainedFileCount - 1; i >= 1; i--)
            {
                var target = ArchivePath(i);
                if (File.Exists(target))
                    File.Delete(target);
                var source = ArchivePath(i - 1);
                if (File.Exists(source))
                    File.Move(source, target);
            }
        }

        private string ArchivePath(int index) =>
            index == 0
                ? _logPath
                : Path.Combine(
                    Path.GetDirectoryName(_logPath)!,
                    $"inventree-addin.{index}.log");

        private string Redact(string text)
        {
            foreach (var secret in _secrets)
                text = text.Replace(secret, Redacted);
            return text;
        }

        public void Dispose()
        {
            lock (_gate)
                _disposed = true;
        }
    }
}
