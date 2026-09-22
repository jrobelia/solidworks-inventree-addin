using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin
{
    /// <summary>
    /// The per-fetch domain module: owns the fetched InvenTree part, its
    /// thumbnail, and the field-level Apply/Push behavior for one Part Sync
    /// session. Pure domain logic — no UI concerns.
    /// </summary>
    /// <remarks>
    /// Internal implementation detail of <see cref="PartSyncCoordinator"/> —
    /// only the coordinator creates, replaces, and clears the current session.
    /// The coordinator orchestrates the STA discipline a session cannot own:
    /// document writes go through a shared pending-write dictionary the
    /// coordinator clears on generation advance (registration precedes every
    /// <c>SetCustomProperty</c> — SolidWorks can raise the echo synchronously
    /// during the write), and Push is decomposed into STA capture → network
    /// send → STA commit so <see cref="Part"/> mutations land only inside a
    /// validated commit.
    /// </remarks>
    internal sealed class PartSyncSession
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly IInventreeClient _client;
        private readonly IDocumentPropertyService _propertyService;
        private readonly PropertyMappingConfig _mapping;
        private readonly IDictionary<string, string> _pendingWrites;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>The fetched InvenTree part. Updated by <see cref="CommitPushedValue"/> on successful Push calls.</summary>
        public InventreePart Part { get; }

        /// <summary>Shortcut to the fetched part's InvenTree PK.</summary>
        public int PartPk => Part.Pk;

        /// <summary>Thumbnail PNG bytes; null if not yet fetched or not available.</summary>
        public byte[]? ThumbnailBytes { get; private set; }

        /// <summary>The mapping captured at construction — sessions are rebuilt on mapping replacement.</summary>
        public PropertyMappingConfig Mapping => _mapping;

        // ── Constructor ───────────────────────────────────────────────────────

        public PartSyncSession(
            InventreePart part,
            IInventreeClient client,
            IDocumentPropertyService propertyService,
            PropertyMappingConfig mapping,
            IDictionary<string, string> pendingWrites,
            byte[]? thumbnailBytes = null)
        {
            Part = part;
            _client = client;
            _propertyService = propertyService;
            _mapping = mapping;
            _pendingWrites = pendingWrites;
            ThumbnailBytes = thumbnailBytes;
        }

        // ── Apply (InvenTree → SolidWorks) ────────────────────────────────────

        /// <summary>The mapped Document Property name for <paramref name="field"/>; null when unmapped.</summary>
        public string? ApplyPropertyName(ApplyField field) => field switch
        {
            ApplyField.Name => _mapping.NameProperty,
            ApplyField.Notes => _mapping.NotesProperty,
            ApplyField.Description => _mapping.DescriptionProperty,
            ApplyField.Pk => _mapping.PkProperty,
            _ => null,
        };

        /// <summary>
        /// Writes one field to the SolidWorks document on the STA thread and
        /// returns the written value. The pending-write registration precedes
        /// the write — SolidWorks can raise the echo synchronously during it.
        /// Unmapped fields write nothing but still return the part-side value.
        /// </summary>
        public string Apply(ApplyField field)
        {
            var propertyName = ApplyPropertyName(field);
            var value = ApplyValue(field);
            if (!string.IsNullOrEmpty(propertyName))
            {
                _pendingWrites[propertyName!] = value;
                _propertyService.SetCustomProperty(propertyName!, value);
            }
            return value;
        }

        /// <summary>Task-returning wrapper over the synchronous STA write.</summary>
        public Task<string> ApplyAsync(ApplyField field) => Task.FromResult(Apply(field));

        /// <summary>Writes every Apply field to the SolidWorks document.</summary>
        public Task ApplyAllAsync()
        {
            Apply(ApplyField.Name);
            Apply(ApplyField.Notes);
            Apply(ApplyField.Description);
            Apply(ApplyField.Pk);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns property names mapped to <paramref name="propertyName"/> that don't yet
        /// exist in the SolidWorks document. Returns an empty list when the property exists.
        /// </summary>
        public IReadOnlyList<string> GetMissingApplyProperties(string? propertyName)
        {
            var missing = new List<string>();
            if (!string.IsNullOrEmpty(propertyName) && !_propertyService.PropertyExists(propertyName!))
                missing.Add(propertyName!);
            return missing;
        }

        // ── Push (SolidWorks → InvenTree) — decomposed for STA discipline ─────

        /// <summary>
        /// STA capture: reads the mapped Document Property value for
        /// <paramref name="field"/>. Null when unmapped — nothing to push.
        /// </summary>
        public string? CapturePushValue(PushField field) =>
            GetPropertyIfMapped(PushPropertyName(field));

        /// <summary>Network send: pushes <paramref name="value"/> to the part on InvenTree.</summary>
        public Task PushValueAsync(PushField field, string value) => field switch
        {
            PushField.Name => _client.UpdatePartNameAsync(Part.Pk, value),
            PushField.Notes => _client.UpdatePartNotesAsync(Part.Pk, value),
            PushField.Description => _client.UpdatePartDescriptionAsync(Part.Pk, value),
            PushField.Revision => _client.UpdatePartRevisionAsync(Part.Pk, value),
            _ => Task.CompletedTask,
        };

        /// <summary>
        /// STA commit: applies a successfully pushed value to <see cref="Part"/>.
        /// Called by the coordinator inside its validated commit — never on a
        /// network continuation.
        /// </summary>
        public void CommitPushedValue(PushField field, string value)
        {
            switch (field)
            {
                case PushField.Name: Part.Name = value; break;
                case PushField.Notes: Part.Notes = value; break;
                case PushField.Description: Part.Description = value; break;
                case PushField.Revision: Part.Revision = value; break;
            }
        }

        /// <summary>
        /// Convenience whole-op Push — capture → send → commit in one await.
        /// Used by <see cref="PushAllAsync"/> and session-level tests; the
        /// coordinator uses the decomposed calls so the commit lands inside
        /// its token validation.
        /// </summary>
        public async Task PushAsync(PushField field)
        {
            var value = CapturePushValue(field);
            if (value == null) return;

            await PushValueAsync(field, value).ConfigureAwait(false);
            CommitPushedValue(field, value);
        }

        /// <summary>Pushes every field whose mapped property exists on the document.</summary>
        public async Task PushAllAsync()
        {
            await PushAsync(PushField.Name).ConfigureAwait(false);
            await PushAsync(PushField.Notes).ConfigureAwait(false);
            await PushAsync(PushField.Description).ConfigureAwait(false);
            await PushAsync(PushField.Revision).ConfigureAwait(false);
        }

        // ── Thumbnail ─────────────────────────────────────────────────────────

        /// <summary>Updates the thumbnail bytes after a successful Push Image.</summary>
        public void SetThumbnail(byte[] bytes) => ThumbnailBytes = bytes;

        // ── Helpers ───────────────────────────────────────────────────────────

        private string? PushPropertyName(PushField field) => field switch
        {
            PushField.Name => _mapping.NameProperty,
            PushField.Notes => _mapping.NotesProperty,
            PushField.Description => _mapping.DescriptionProperty,
            PushField.Revision => _mapping.RevisionProperty,
            _ => null,
        };

        private string ApplyValue(ApplyField field) => field switch
        {
            ApplyField.Name => Part.Name,
            ApplyField.Notes => Part.Notes,
            ApplyField.Description => Part.Description,
            ApplyField.Pk => Part.Pk.ToString(),
            _ => string.Empty,
        };

        private string? GetPropertyIfMapped(string? propertyName) =>
            string.IsNullOrEmpty(propertyName)
                ? null
                : _propertyService.GetCustomProperty(propertyName!);
    }
}
