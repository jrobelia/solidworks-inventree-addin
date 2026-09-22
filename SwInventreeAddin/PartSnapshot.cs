using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin
{
    /// <summary>
    /// An immutable, coherent projection of an InvenTree part as the Part Sync
    /// coordinator saw it. Exposed on the coordinator's read surface and in
    /// <see cref="PartSyncResult"/> payloads instead of the mutable
    /// <see cref="InventreePart"/>, so consumers can never mutate session state
    /// behind the coordinator's back.
    /// </summary>
    public sealed class PartSnapshot
    {
        public PartSnapshot(
            int pk,
            string ipn,
            string name,
            string notes,
            string revision,
            string description,
            string? thumbnailUrl,
            decimal inStock,
            decimal ordering,
            bool active,
            bool assembly,
            bool component,
            bool purchaseable,
            bool salable,
            bool trackable,
            bool testable)
        {
            Pk = pk;
            Ipn = ipn ?? string.Empty;
            Name = name ?? string.Empty;
            Notes = notes ?? string.Empty;
            Revision = revision ?? string.Empty;
            Description = description ?? string.Empty;
            ThumbnailUrl = thumbnailUrl;
            InStock = inStock;
            Ordering = ordering;
            Active = active;
            Assembly = assembly;
            Component = component;
            Purchaseable = purchaseable;
            Salable = salable;
            Trackable = trackable;
            Testable = testable;
        }

        public int Pk { get; }
        public string Ipn { get; }
        public string Name { get; }
        public string Notes { get; }
        public string Revision { get; }
        public string Description { get; }
        public string? ThumbnailUrl { get; }
        public decimal InStock { get; }
        public decimal Ordering { get; }
        public bool Active { get; }
        public bool Assembly { get; }
        public bool Component { get; }
        public bool Purchaseable { get; }
        public bool Salable { get; }
        public bool Trackable { get; }
        public bool Testable { get; }

        /// <summary>Projects a mutable client/dialog part into an immutable snapshot.</summary>
        internal static PartSnapshot FromPart(InventreePart part) =>
            new PartSnapshot(
                pk: part.Pk,
                ipn: part.Ipn,
                name: part.Name,
                notes: part.Notes,
                revision: part.Revision,
                description: part.Description,
                thumbnailUrl: part.ThumbnailUrl,
                inStock: part.InStock,
                ordering: part.Ordering,
                active: part.Active,
                assembly: part.Assembly,
                component: part.Component,
                purchaseable: part.Purchaseable,
                salable: part.Salable,
                trackable: part.Trackable,
                testable: part.Testable);

        /// <summary>
        /// Re-materializes a mutable <see cref="InventreePart"/> from this
        /// snapshot — the copy-on-install direction the coordinator uses when
        /// a confirmation candidate is installed as a session part.
        /// </summary>
        internal InventreePart ToPart() =>
            new InventreePart
            {
                Pk = Pk,
                Ipn = Ipn,
                Name = Name,
                Notes = Notes,
                Revision = Revision,
                Description = Description,
                ThumbnailUrl = ThumbnailUrl,
                InStock = InStock,
                Ordering = Ordering,
                Active = Active,
                Assembly = Assembly,
                Component = Component,
                Purchaseable = Purchaseable,
                Salable = Salable,
                Trackable = Trackable,
                Testable = Testable,
            };
    }
}
