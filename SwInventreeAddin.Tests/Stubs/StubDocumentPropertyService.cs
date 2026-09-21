using System.Collections.Generic;
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

        /// <summary>Set this to control what GetDocumentType() returns in tests. Defaults to Part.</summary>
        public DocumentType DocumentTypeToReturn { get; set; } = DocumentType.Part;

        /// <summary>When true, GetCustomProperty returns StaleValue to simulate SW's stale read after a set on assemblies.</summary>
        public bool ReturnStaleReads { get; set; }

        /// <summary>The stale value GetCustomProperty returns when ReturnStaleReads is true.</summary>
        public string StaleValue { get; set; } = string.Empty;

        public DocumentType GetDocumentType() => DocumentTypeToReturn;

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
