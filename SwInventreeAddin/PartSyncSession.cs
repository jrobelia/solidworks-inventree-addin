using System.Collections.Generic;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin
{
    /// <summary>
    /// Owns the fetched InvenTree part and all field-level Apply and Push operations
    /// for a Part Sync session. Pure domain logic — no UI concerns.
    /// </summary>
    public class PartSyncSession
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly IInventreeClient          _client;
        private readonly IDocumentPropertyService  _propertyService;
        private readonly PropertyMappingConfig     _mapping;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>The fetched InvenTree part. Updated in-place on successful Push calls.</summary>
        public InventreePart Part { get; }

        /// <summary>Thumbnail PNG bytes; null if not yet fetched or not available.</summary>
        public byte[]? ThumbnailBytes { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────

        public PartSyncSession(
            InventreePart             part,
            IInventreeClient          client,
            IDocumentPropertyService  propertyService,
            PropertyMappingConfig     mapping,
            byte[]?                   thumbnailBytes = null)
        {
            Part             = part;
            _client          = client;
            _propertyService = propertyService;
            _mapping         = mapping;
            ThumbnailBytes   = thumbnailBytes;
        }

        // ── Apply (InvenTree → SolidWorks) ────────────────────────────────────

        /// <summary>Writes Name, Notes, and Description to the SolidWorks document.</summary>
        public void Apply()
        {
            _propertyService.SetCustomProperty(_mapping.NameProperty,        Part.Name);
            _propertyService.SetCustomProperty(_mapping.NotesProperty,       Part.Notes);
            _propertyService.SetCustomProperty(_mapping.DescriptionProperty, Part.Description);
        }

        /// <summary>Writes only the Name field to the SolidWorks document.</summary>
        public void ApplyName() =>
            _propertyService.SetCustomProperty(_mapping.NameProperty, Part.Name);

        /// <summary>Writes only the Notes field to the SolidWorks document.</summary>
        public void ApplyNotes() =>
            _propertyService.SetCustomProperty(_mapping.NotesProperty, Part.Notes);

        /// <summary>Writes only the Description field to the SolidWorks document.</summary>
        public void ApplyDescription() =>
            _propertyService.SetCustomProperty(_mapping.DescriptionProperty, Part.Description);

        /// <summary>Writes the InvenTree PK to the SolidWorks document.</summary>
        public void ApplyPk() =>
            _propertyService.SetCustomProperty(_mapping.PkProperty, Part.Pk.ToString());

        /// <summary>
        /// Returns property names mapped to <paramref name="propertyName"/> that don't yet
        /// exist in the SolidWorks document. Returns an empty list when the property exists.
        /// </summary>
        public IReadOnlyList<string> GetMissingApplyProperties(string propertyName)
        {
            var missing = new List<string>();
            if (!string.IsNullOrEmpty(propertyName) && !_propertyService.PropertyExists(propertyName))
                missing.Add(propertyName);
            return missing;
        }

        // ── Push (SolidWorks → InvenTree) ─────────────────────────────────────

        /// <summary>
        /// Reads the current SW Name property and pushes it to InvenTree.
        /// Updates <see cref="Part"/>.Name on success. Propagates exceptions on failure.
        /// </summary>
        public async Task PushNameAsync()
        {
            var value = _propertyService.GetCustomProperty(_mapping.NameProperty);
            await _client.UpdatePartNameAsync(Part.Pk, value).ConfigureAwait(false);
            Part.Name = value;
        }

        /// <summary>
        /// Reads the current SW Notes property and pushes it to InvenTree.
        /// Updates <see cref="Part"/>.Notes on success. Propagates exceptions on failure.
        /// </summary>
        public async Task PushNotesAsync()
        {
            var value = _propertyService.GetCustomProperty(_mapping.NotesProperty);
            await _client.UpdatePartNotesAsync(Part.Pk, value).ConfigureAwait(false);
            Part.Notes = value;
        }

        /// <summary>
        /// Reads the current SW Description property and pushes it to InvenTree.
        /// Updates <see cref="Part"/>.Description on success. Propagates exceptions on failure.
        /// </summary>
        public async Task PushDescriptionAsync()
        {
            var value = _propertyService.GetCustomProperty(_mapping.DescriptionProperty);
            await _client.UpdatePartDescriptionAsync(Part.Pk, value).ConfigureAwait(false);
            Part.Description = value;
        }

        /// <summary>
        /// Reads the current SW Revision property and pushes it to InvenTree.
        /// Updates <see cref="Part"/>.Revision on success. Propagates exceptions on failure.
        /// </summary>
        public async Task PushRevisionAsync()
        {
            var value = _propertyService.GetCustomProperty(_mapping.RevisionProperty);
            await _client.UpdatePartRevisionAsync(Part.Pk, value).ConfigureAwait(false);
            Part.Revision = value;
        }

        // ── Thumbnail ─────────────────────────────────────────────────────────

        /// <summary>Updates the thumbnail bytes after a successful Push Image.</summary>
        public void SetThumbnail(byte[] bytes) => ThumbnailBytes = bytes;
    }
}
