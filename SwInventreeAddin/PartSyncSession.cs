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
        public string? ApplyPropertyName(ApplyField field) =>
            PartSyncFields.ForApply(field)?.PropertyName(_mapping);

        /// <summary>
        /// Writes one field to the SolidWorks document on the STA thread and
        /// returns the written value. The pending-write registration precedes
        /// the write — SolidWorks can raise the echo synchronously during it.
        /// Unmapped fields write nothing but still return the part-side value.
        /// </summary>
        public string Apply(ApplyField field)
        {
            var propertyName = ApplyPropertyName(field);
            var value = PartSyncFields.ForApply(field)?.Value(Part) ?? string.Empty;
            if (!string.IsNullOrEmpty(propertyName))
            {
                _pendingWrites[propertyName!] = value;
                _propertyService.SetCustomProperty(propertyName!, value);
            }
            return value;
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
            GetPropertyIfMapped(PartSyncFields.ForPush(field)?.PropertyName(_mapping));

        /// <summary>Network send: pushes <paramref name="value"/> to the part on InvenTree.</summary>
        public Task PushValueAsync(PushField field, string value) =>
            PartSyncFields.ForPush(field)?.Push(_client, Part.Pk, value) ?? Task.CompletedTask;

        /// <summary>
        /// STA commit: applies a successfully pushed value to <see cref="Part"/>.
        /// Called by the coordinator inside its validated commit — never on a
        /// network continuation.
        /// </summary>
        public void CommitPushedValue(PushField field, string value) =>
            PartSyncFields.ForPush(field)?.Commit(Part, value);

        // ── Thumbnail ─────────────────────────────────────────────────────────

        /// <summary>Updates the thumbnail bytes after a successful Push Image.</summary>
        public void SetThumbnail(byte[] bytes) => ThumbnailBytes = bytes;

        // ── Helpers ───────────────────────────────────────────────────────────

        private string? GetPropertyIfMapped(string? propertyName) =>
            string.IsNullOrEmpty(propertyName)
                ? null
                : _propertyService.GetCustomProperty(propertyName!);
    }

    /// <summary>
    /// The per-field behavior of an <see cref="ApplyField"/> (InvenTree →
    /// SolidWorks): which mapped Document Property it writes, which part-side
    /// value it carries, and which document-snapshot slot a committed write
    /// lands in. One row per field — session, coordinator, and the substitute
    /// refresh all consult this table instead of re-switching on the enum.
    /// </summary>
    internal sealed class ApplyFieldInfo
    {
        public ApplyFieldInfo(
            Func<PropertyMappingConfig, string?> propertyName,
            Func<InventreePart, string> value,
            Func<TaskPaneDocumentSnapshot, string, TaskPaneDocumentSnapshot> substitute)
        {
            PropertyName = propertyName;
            Value = value;
            Substitute = substitute;
        }

        /// <summary>The mapped Document Property name under a mapping.</summary>
        public Func<PropertyMappingConfig, string?> PropertyName { get; }

        /// <summary>The InvenTree-side value written for the field.</summary>
        public Func<InventreePart, string> Value { get; }

        /// <summary>Rebuilds a document snapshot with the written value substituted.</summary>
        public Func<TaskPaneDocumentSnapshot, string, TaskPaneDocumentSnapshot> Substitute { get; }
    }

    /// <summary>
    /// The per-field behavior of a <see cref="PushField"/> (SolidWorks →
    /// InvenTree): which mapped Document Property it reads, which client call
    /// sends it, and which <see cref="InventreePart"/> field a committed push
    /// updates. One row per field — the same table backs STA capture, network
    /// send, and STA commit.
    /// </summary>
    internal sealed class PushFieldInfo
    {
        public PushFieldInfo(
            Func<PropertyMappingConfig, string?> propertyName,
            Func<IInventreeClient, int, string, Task> push,
            Action<InventreePart, string> commit)
        {
            PropertyName = propertyName;
            Push = push;
            Commit = commit;
        }

        /// <summary>The mapped Document Property name under a mapping.</summary>
        public Func<PropertyMappingConfig, string?> PropertyName { get; }

        /// <summary>The client call that sends the value to InvenTree.</summary>
        public Func<IInventreeClient, int, string, Task> Push { get; }

        /// <summary>Applies a successfully pushed value to the session part.</summary>
        public Action<InventreePart, string> Commit { get; }
    }

    /// <summary>
    /// The single lookup every Apply/Push field behavior goes through.
    /// Out-of-range enum values return null — the former switch-default no-op.
    /// </summary>
    internal static class PartSyncFields
    {
        private static readonly IReadOnlyDictionary<ApplyField, ApplyFieldInfo> ApplyTable =
            new Dictionary<ApplyField, ApplyFieldInfo>
            {
                [ApplyField.Name] = new ApplyFieldInfo(
                    m => m.NameProperty,
                    p => p.Name,
                    (doc, v) => new TaskPaneDocumentSnapshot(
                        doc.DocumentType, doc.Ipn, doc.PkText, v, doc.Notes, doc.Revision, doc.Description)),
                [ApplyField.Notes] = new ApplyFieldInfo(
                    m => m.NotesProperty,
                    p => p.Notes,
                    (doc, v) => new TaskPaneDocumentSnapshot(
                        doc.DocumentType, doc.Ipn, doc.PkText, doc.Name, v, doc.Revision, doc.Description)),
                [ApplyField.Description] = new ApplyFieldInfo(
                    m => m.DescriptionProperty,
                    p => p.Description,
                    (doc, v) => new TaskPaneDocumentSnapshot(
                        doc.DocumentType, doc.Ipn, doc.PkText, doc.Name, doc.Notes, doc.Revision, v)),
                [ApplyField.Pk] = new ApplyFieldInfo(
                    m => m.PkProperty,
                    p => p.Pk.ToString(),
                    (doc, v) => new TaskPaneDocumentSnapshot(
                        doc.DocumentType, doc.Ipn, v, doc.Name, doc.Notes, doc.Revision, doc.Description)),
            };

        private static readonly IReadOnlyDictionary<PushField, PushFieldInfo> PushTable =
            new Dictionary<PushField, PushFieldInfo>
            {
                [PushField.Name] = new PushFieldInfo(
                    m => m.NameProperty,
                    (c, pk, v) => c.UpdatePartNameAsync(pk, v),
                    (p, v) => p.Name = v),
                [PushField.Notes] = new PushFieldInfo(
                    m => m.NotesProperty,
                    (c, pk, v) => c.UpdatePartNotesAsync(pk, v),
                    (p, v) => p.Notes = v),
                [PushField.Description] = new PushFieldInfo(
                    m => m.DescriptionProperty,
                    (c, pk, v) => c.UpdatePartDescriptionAsync(pk, v),
                    (p, v) => p.Description = v),
                [PushField.Revision] = new PushFieldInfo(
                    m => m.RevisionProperty,
                    (c, pk, v) => c.UpdatePartRevisionAsync(pk, v),
                    (p, v) => p.Revision = v),
            };

        /// <summary>The row for <paramref name="field"/>; null when the enum value has no row.</summary>
        public static ApplyFieldInfo? ForApply(ApplyField field) =>
            ApplyTable.TryGetValue(field, out var info) ? info : null;

        /// <summary>The row for <paramref name="field"/>; null when the enum value has no row.</summary>
        public static PushFieldInfo? ForPush(PushField field) =>
            PushTable.TryGetValue(field, out var info) ? info : null;
    }
}
