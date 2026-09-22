using SwInventreeAddin.Config;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// One coherent, immutable read of the state
    /// <see cref="BomCompareReadinessCheck"/> evaluates: the document's IPN
    /// and stamped InvenTree Part PK, the SolidWorks revision, the session's
    /// in-memory part PK and fetched revision, and the resolved mapping
    /// (a defensive copy — callers cannot mutate the coordinator's mapping).
    /// </summary>
    internal sealed class BomReadinessSnapshot
    {
        public BomReadinessSnapshot(
            string ipn,
            int inMemoryPartPk,
            string stampedPkText,
            string swRevision,
            string fetchedRevision,
            PropertyMappingConfig mapping)
        {
            Ipn = ipn ?? string.Empty;
            InMemoryPartPk = inMemoryPartPk;
            StampedPkText = stampedPkText ?? string.Empty;
            SwRevision = swRevision ?? string.Empty;
            FetchedRevision = fetchedRevision ?? string.Empty;
            Mapping = mapping;
        }

        /// <summary>The document's IPN Document Property value.</summary>
        public string Ipn { get; }

        /// <summary>The current session's InvenTree part PK; 0 when no session is populated.</summary>
        public int InMemoryPartPk { get; }

        /// <summary>The raw InvenTree Part PK stamped in the document's properties.</summary>
        public string StampedPkText { get; }

        /// <summary>The document's Revision Document Property value.</summary>
        public string SwRevision { get; }

        /// <summary>The fetched part's revision; empty without a session.</summary>
        public string FetchedRevision { get; }

        /// <summary>The resolved mapping (defensive copy), including BOM column aliases.</summary>
        public PropertyMappingConfig Mapping { get; }
    }
}
