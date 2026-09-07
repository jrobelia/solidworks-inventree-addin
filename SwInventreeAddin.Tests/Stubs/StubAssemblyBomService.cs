using System;
using System.Collections.Generic;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubAssemblyBomService : IAssemblyBomService
    {
        public bool HasBomTableResult        { get; set; } = true;
        public List<SwBomLine> LinesToReturn { get; set; } = new List<SwBomLine>();
        public bool ThrowOnGetBomLines       { get; set; }
        public PropertyMappingConfig? ReceivedMapping { get; private set; }
        public string? LastKeywordUsed { get; private set; }

        public bool HasBomTable(string keyword)
        {
            LastKeywordUsed = keyword;
            return HasBomTableResult;
        }

        public string GetBomTableName(string keyword)
        {
            LastKeywordUsed = keyword;
            return "BOM1";
        }

        public IReadOnlyList<SwBomLine> GetBomLines(string keyword, PropertyMappingConfig mapping)
        {
            LastKeywordUsed = keyword;
            if (ThrowOnGetBomLines)
                throw new InvalidOperationException("Stub: no BOM table found");
            ReceivedMapping = mapping;
            return LinesToReturn;
        }
    }
}
