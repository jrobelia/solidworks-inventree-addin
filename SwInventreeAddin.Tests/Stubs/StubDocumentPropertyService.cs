using System.Collections.Generic;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubDocumentPropertyService : IDocumentPropertyService
    {
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();

        public List<string> SetCallLog { get; } = new List<string>();

        public void Seed(string name, string value) => _properties[name] = value;

        public string GetCustomProperty(string name) =>
            _properties.TryGetValue(name, out var val) ? val : string.Empty;

        public void SetCustomProperty(string name, string value)
        {
            _properties[name] = value;
            SetCallLog.Add(name);
        }
    }
}
