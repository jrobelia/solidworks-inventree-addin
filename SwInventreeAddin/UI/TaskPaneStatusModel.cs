using System.Collections.Generic;
using System.Linq;

namespace SwInventreeAddin.UI
{
    public sealed class TaskPaneStatusModel
    {
        private readonly List<StatusEntry> _entries = new List<StatusEntry>();
        private StatusEntry? _reigningTransient;

        public StatusEntry? Current => _reigningTransient
            ?? _entries.OrderByDescending(entry => entry.Severity).FirstOrDefault();

        public string? ToolTip
        {
            get
            {
                var current = Current;
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(current?.Detail))
                    lines.Add(current!.Detail!);
                lines.AddRange(_entries.Where(entry => entry != current)
                    .OrderByDescending(entry => entry.Severity).Select(entry => entry.Text));
                return lines.Count == 0 ? null : string.Join(System.Environment.NewLine, lines);
            }
        }

        public void Resolve(string domain)
        {
            _entries.RemoveAll(entry => BelongsToDomain(entry, domain));
            if (_reigningTransient != null && BelongsToDomain(_reigningTransient, domain))
                _reigningTransient = null;
        }

        private static bool BelongsToDomain(StatusEntry entry, string domain) =>
            entry.Domain == domain || entry.Domain.StartsWith(domain + ".", System.StringComparison.Ordinal);

        public void InvalidateTransients()
        {
            _reigningTransient = null;
            _entries.RemoveAll(entry => entry.Persistence == StatusPersistence.Transient
                && entry.Severity != StatusSeverity.Error);
        }

        public void Publish(StatusEntry entry)
        {
            if (entry.Persistence == StatusPersistence.Transient)
            {
                InvalidateTransients();
                if (entry.Severity == StatusSeverity.Success)
                    Resolve(entry.Domain);
                _reigningTransient = entry;
            }
            _entries.RemoveAll(existing => existing.Domain == entry.Domain
                && existing.Persistence == entry.Persistence
                && (existing.Severity != StatusSeverity.Error || entry.Severity == StatusSeverity.Error));
            _entries.Insert(0, entry);
        }
    }
}
