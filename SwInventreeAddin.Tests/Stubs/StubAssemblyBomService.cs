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

        public bool HasBomTable(string keyword) => HasBomTableResult;

        public (string TableName, bool NeedsRebuild) GetBomInfo(string keyword)
            => ("BOM1", false);

        public IReadOnlyList<SwBomLine> GetBomLines(string keyword, PropertyMappingConfig mapping)
        {
            if (ThrowOnGetBomLines)
                throw new InvalidOperationException("Stub: no BOM table found");
            return LinesToReturn;
        }
    }
}
