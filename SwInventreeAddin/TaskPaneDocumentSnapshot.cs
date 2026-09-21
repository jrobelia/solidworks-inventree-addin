using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin
{
    /// <summary>
    /// One coherent, immutable capture of the active SolidWorks document's
    /// Task Pane state: identity (Document Type, IPN, stamped InvenTree Part
    /// PK) plus the mapped Document Property values that Part Sync and BOM
    /// readiness display. The host builds snapshots on the STA thread from
    /// <see cref="IDocumentPropertyService"/>; <see cref="TaskPaneState"/>
    /// never sees the service, the mapping provider, or a COM object.
    /// </summary>
    public sealed class TaskPaneDocumentSnapshot
    {
        public TaskPaneDocumentSnapshot(
            DocumentType documentType,
            string ipn,
            string pkText,
            string name,
            string notes,
            string revision,
            string description)
        {
            DocumentType = documentType;
            Ipn = ipn ?? string.Empty;
            PkText = pkText ?? string.Empty;
            Name = name ?? string.Empty;
            Notes = notes ?? string.Empty;
            Revision = revision ?? string.Empty;
            Description = description ?? string.Empty;
            StampedPartPk = int.TryParse(PkText, out int pk) && pk > 0 ? pk : 0;
        }

        /// <summary>The type of the captured document.</summary>
        public DocumentType DocumentType { get; }

        /// <summary>The document's IPN Document Property value.</summary>
        public string Ipn { get; }

        /// <summary>The raw stamped InvenTree Part PK Document Property value.</summary>
        public string PkText { get; }

        /// <summary>
        /// <see cref="PkText"/> parsed as a positive integer; 0 when the stamp is
        /// missing or not a valid InvenTree Part PK.
        /// </summary>
        public int StampedPartPk { get; }

        /// <summary>The document's Name Document Property value.</summary>
        public string Name { get; }

        /// <summary>The document's Notes Document Property value.</summary>
        public string Notes { get; }

        /// <summary>The document's Revision Document Property value.</summary>
        public string Revision { get; }

        /// <summary>The document's Description Document Property value.</summary>
        public string Description { get; }
    }
}
