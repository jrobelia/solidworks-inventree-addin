using System.Collections.Generic;
using System.Linq;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubDocumentPropertyService : IDocumentPropertyService
    {
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        private readonly List<(string Name, string Value)> _writeLog =
            new List<(string Name, string Value)>();

        /// <summary>Every SetCustomProperty call, in order — name and value written.</summary>
        public IReadOnlyList<(string Name, string Value)> WriteLog => _writeLog;

        /// <summary>The property names written, in order — the name projection of <see cref="WriteLog"/>.</summary>
        public IReadOnlyList<string> WrittenNames => _writeLog.Select(w => w.Name).ToList();

        /// <summary>
        /// True when a write of <paramref name="name"/> was recorded — and of
        /// <paramref name="value"/> too when one is given.
        /// </summary>
        public bool DidWrite(string name, string? value = null) =>
            _writeLog.Any(w => w.Name == name && (value == null || w.Value == value));

        /// <summary>Set this to control what GetDocumentType() returns in tests. Defaults to Part.</summary>
        public DocumentType DocumentTypeToReturn { get; set; } = DocumentType.Part;

        /// <summary>
        /// Set this to control what GetActiveDocumentToken() returns in tests.
        /// Change it to model an active-document switch; defaults to "doc-1" so
        /// repeated loads of the same fixture classify as refreshes.
        /// </summary>
        public string? ActiveDocumentTokenToReturn { get; set; } = "doc-1";

        /// <summary>When true, GetCustomProperty returns StaleValue to simulate SW's stale read after a set on assemblies.</summary>
        public bool ReturnStaleReads { get; set; }

        /// <summary>The stale value GetCustomProperty returns when ReturnStaleReads is true.</summary>
        public string StaleValue { get; set; } = string.Empty;

        public DocumentType GetDocumentType() => DocumentTypeToReturn;

        public string? GetActiveDocumentToken() =>
            DocumentTypeToReturn == DocumentType.Unknown ? null : ActiveDocumentTokenToReturn;

        public void Seed(string name, string value) => _properties[name] = value;

        public string GetCustomProperty(string name) =>
            ReturnStaleReads ? StaleValue : _properties.TryGetValue(name, out var val) ? val : string.Empty;

        public void SetCustomProperty(string name, string value)
        {
            _properties[name] = value;
            _writeLog.Add((name, value));
        }

        public bool PropertyExists(string name) => _properties.ContainsKey(name);
    }
}
