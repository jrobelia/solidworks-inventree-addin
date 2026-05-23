using System.Threading.Tasks;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// Exposes the Task Pane state that <see cref="BomCompareReadinessCheck"/> needs to
    /// evaluate whether a BOM Compare can proceed, and to push a revision when the caller
    /// has confirmed the operation.
    /// </summary>
    internal interface IBomReadinessSource
    {
        /// <summary>InvenTree PK held in memory from the last Part Sync. 0 if not yet fetched.</summary>
        int    CurrentInvenTreePk { get; }

        /// <summary>The IPN read from the active SolidWorks Document Property.</summary>
        string PartNumber         { get; }

        /// <summary>
        /// The InvenTree PK value stamped in this assembly's SolidWorks Document Properties.
        /// Empty if Part Sync has never been run on this document.
        /// </summary>
        string CurrentPk          { get; }

        /// <summary>The revision value read from the active SolidWorks Document Property.</summary>
        string CurrentRevision    { get; }

        /// <summary>The revision value fetched from InvenTree during the last Part Sync.</summary>
        string RevisionPreview    { get; }

        /// <summary>
        /// Fetches the InvenTree part for the current IPN and populates the in-memory state.
        /// </summary>
        Task FetchPartAsync();

        /// <summary>Re-reads SolidWorks Document Properties into the in-memory state.</summary>
        void RefreshCurrentProperties();

        /// <summary>Pushes the SolidWorks revision value to InvenTree.</summary>
        Task PushRevisionToInventreeAsync();
    }
}
