using System;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// One coherent, immutable read of the state
    /// <see cref="BomCompareReadinessCheck"/> evaluates: the document's IPN
    /// and stamped InvenTree Part PK, the SolidWorks revision, the session's
    /// in-memory part PK, fetched revision, and fetched Assembly flag, plus
    /// the resolved mapping
    /// (a defensive copy — callers cannot mutate the coordinator's mapping).
    /// </summary>
    internal sealed class BomReadinessSnapshot
    {
        // The snapshot owns its mapping — defensive copy in, defensive copy
        // out — so the type is immutable regardless of what the caller hands
        // in or does with the value it reads back.
        private readonly PropertyMappingConfig _mapping;

        public BomReadinessSnapshot(
            string ipn,
            int inMemoryPartPk,
            string stampedPkText,
            string swRevision,
            string fetchedRevision,
            bool fetchedPartIsAssembly,
            PropertyMappingConfig mapping)
        {
            Ipn = ipn ?? string.Empty;
            InMemoryPartPk = inMemoryPartPk;
            StampedPkText = stampedPkText ?? string.Empty;
            SwRevision = swRevision ?? string.Empty;
            FetchedRevision = fetchedRevision ?? string.Empty;
            FetchedPartIsAssembly = fetchedPartIsAssembly;
            _mapping = (mapping ?? throw new ArgumentNullException(nameof(mapping))).Clone();
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

        /// <summary>
        /// The fetched part's Assembly flag — whether it can hold a BOM;
        /// false without a session. Same staleness profile as
        /// <see cref="FetchedRevision"/>: read from the same fetch, no extra
        /// server call.
        /// </summary>
        public bool FetchedPartIsAssembly { get; }

        /// <summary>The resolved mapping (defensive copy), including BOM column aliases.</summary>
        public PropertyMappingConfig Mapping => _mapping.Clone();
    }
}
