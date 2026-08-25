using System;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// View-model for the BOM-table-missing warning dialog.
    /// </summary>
    public sealed class BomTableMissingViewModel
    {
        // ── Properties ─────────────────────────────────────────────────────────

        /// <summary>
        /// The dialog title.
        /// </summary>
        public string Title { get; } = "BOM Compare";

        /// <summary>
        /// The warning message shown to the engineer.
        /// </summary>
        public string Message { get; }

        // ── Constructors ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the view-model for a missing BOM table warning.
        /// </summary>
        /// <param name="bomKeyword">The configured BOM keyword the table should have matched.</param>
        public BomTableMissingViewModel(string bomKeyword)
        {
            if (bomKeyword is null)
                throw new ArgumentNullException(nameof(bomKeyword));

            Message = $"No BOM table containing '{bomKeyword}' was found in the active assembly." +
                      $"{System.Environment.NewLine}{System.Environment.NewLine}" +
                      $"Change the BOM Table Keyword under Settings → BOM Sync.";
        }
    }
}
