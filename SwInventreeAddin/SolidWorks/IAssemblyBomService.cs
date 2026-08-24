using System.Collections.Generic;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.SolidWorks
{
    public interface IAssemblyBomService
    {
        /// <summary>
        /// Returns true when a BOM table whose name contains the keyword
        /// (case-insensitive) exists in the currently active assembly.
        /// </summary>
        bool HasBomTable(string keyword);

        /// <summary>
        /// Returns the feature name of the matched BOM table.
        /// </summary>
        string GetBomInfo(string keyword);

        /// <summary>
        /// Scrapes rows from the first BOM table whose name contains the keyword.
        /// Throws InvalidOperationException if no matching table is found.
        /// </summary>
        IReadOnlyList<SwBomLine> GetBomLines(string keyword, PropertyMappingConfig mapping);
    }
}
