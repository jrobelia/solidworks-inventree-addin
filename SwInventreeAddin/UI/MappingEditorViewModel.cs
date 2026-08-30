using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Non-WPF state and validation for the Property Mapping editor.
    /// Holds a draft copy of the mapping, validates it on save, and reverts
    /// when the user cancels or the save fails.
    /// </summary>
    public class MappingEditorViewModel : INotifyPropertyChanged
    {
        // ── INotifyPropertyChanged ─────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Dependencies and original state ────────────────────────────────────

        private readonly IPropertyMappingProvider _provider;
        private          PropertyMappingConfig    _original;
        private readonly MappingResult            _result;
        private readonly bool                     _isReadOnly;
        private readonly MappingHealth            _health;

        private bool _copyToLocalCompleted;

        // ── Bindable fields ────────────────────────────────────────────────────

        private string _ipnProperty         = string.Empty;
        private string _nameProperty        = string.Empty;
        private string _descriptionProperty = string.Empty;
        private string _revisionProperty    = string.Empty;
        private string _notesProperty       = string.Empty;
        private string _pkProperty          = string.Empty;

        private string _bomColumnIpn        = string.Empty;
        private string _bomColumnQty        = string.Empty;
        private string _bomColumnReference  = string.Empty;
        private string _bomColumnNote       = string.Empty;

        private string? _errorMessage;
        private string? _copyToLocalInstruction;

        // ── Defaults ───────────────────────────────────────────────────────────

        private static readonly PropertyMappingConfig DefaultConfig
            = PropertyMappingConfig.WithDefaults();

        // ── Constructors ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new editor for the supplied <paramref name="provider"/>.
        /// Loads the current mapping into the draft and exposes placeholders
        /// from the current add-in defaults.
        /// </summary>
        public MappingEditorViewModel(IPropertyMappingProvider provider)
        {
            _provider = provider;
            _result   = _provider.GetMappingResult();
            _original = CloneConfig(_result.Config);
            _health   = _result.Health;
            _isReadOnly = !_result.CanEdit || _provider.IsReadOnly;

            RevertToOriginal();
        }

        // ── Bindable properties ────────────────────────────────────────────────

        public string IpnProperty
        {
            get => _ipnProperty;
            set => Set(ref _ipnProperty, value);
        }

        public string NameProperty
        {
            get => _nameProperty;
            set => Set(ref _nameProperty, value);
        }

        public string DescriptionProperty
        {
            get => _descriptionProperty;
            set => Set(ref _descriptionProperty, value);
        }

        public string RevisionProperty
        {
            get => _revisionProperty;
            set => Set(ref _revisionProperty, value);
        }

        public string NotesProperty
        {
            get => _notesProperty;
            set => Set(ref _notesProperty, value);
        }

        public string PkProperty
        {
            get => _pkProperty;
            set => Set(ref _pkProperty, value);
        }

        public string BomColumnIpn
        {
            get => _bomColumnIpn;
            set => Set(ref _bomColumnIpn, value);
        }

        public string BomColumnQty
        {
            get => _bomColumnQty;
            set => Set(ref _bomColumnQty, value);
        }

        public string BomColumnReference
        {
            get => _bomColumnReference;
            set => Set(ref _bomColumnReference, value);
        }

        public string BomColumnNote
        {
            get => _bomColumnNote;
            set => Set(ref _bomColumnNote, value);
        }

        public string IpnPlaceholder         => DefaultConfig.IpnProperty!;
        public string NamePlaceholder        => DefaultConfig.NameProperty!;
        public string DescriptionPlaceholder => DefaultConfig.DescriptionProperty!;
        public string RevisionPlaceholder    => DefaultConfig.RevisionProperty!;
        public string NotesPlaceholder       => DefaultConfig.NotesProperty!;
        public string PkPlaceholder          => DefaultConfig.PkProperty!;

        public string BomColumnIpnPlaceholder       => DefaultConfig.BomColumnIpn!;
        public string BomColumnQtyPlaceholder       => DefaultConfig.BomColumnQty!;
        public string BomColumnReferencePlaceholder => DefaultConfig.BomColumnReference!;
        public string BomColumnNotePlaceholder      => DefaultConfig.BomColumnNote!;

        public bool IsReadOnly => _isReadOnly;

        public bool CanCopyToLocal =>
            _isReadOnly &&
            _health == MappingHealth.NeedsUpgrade &&
            !_copyToLocalCompleted;

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set => Set(ref _errorMessage, value);
        }

        public string? CopyToLocalInstruction
        {
            get => _copyToLocalInstruction;
            private set => Set(ref _copyToLocalInstruction, value);
        }

        // ── Commands ───────────────────────────────────────────────────────────

        /// <summary>
        /// Validates the draft and, if it is valid, persists it through
        /// <see cref="IPropertyMappingProvider.SaveMapping"/>.
        /// Returns <c>true</c> when the save succeeds; otherwise reverts the
        /// draft and returns <c>false</c>.
        /// </summary>
        public bool Save()
        {
            ErrorMessage = null;

            var draft = BuildDraft();

            var validationError = Validate(draft);
            if (!string.IsNullOrEmpty(validationError))
            {
                ErrorMessage = validationError;
                RevertToOriginal();
                return false;
            }

            try
            {
                _provider.SaveMapping(draft);
                _original = CloneConfig(draft);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                RevertToOriginal();
                return false;
            }
        }

        /// <summary>
        /// Discards the draft and resets all editable fields to the on-disk
        /// values that were loaded when the editor opened.
        /// </summary>
        public void Cancel()
        {
            ErrorMessage = null;
            CopyToLocalInstruction = null;
            RevertToOriginal();
        }

        /// <summary>
        /// Copies the shared mapping file to the local path and shows the
        /// instruction to switch to Local in Settings.
        /// </summary>
        public void CopyToLocal()
        {
            try
            {
                _provider.CopyToLocal();
                _copyToLocalCompleted = true;
                CopyToLocalInstruction =
                    "A local copy has been saved. Close this editor and select Local in Settings to edit the mapping.";
                OnPropertyChanged(nameof(CanCopyToLocal));
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        // ── Validation ─────────────────────────────────────────────────────────

        private static string? Validate(PropertyMappingConfig draft)
        {
            var aliasError = ValidateBomAliases(draft);
            if (!string.IsNullOrEmpty(aliasError))
                return aliasError;

            var result = PropertyMappingProvider.Classify(draft, string.Empty);
            if (result.Health == MappingHealth.Invalid)
                return result.MessageOrDefault;

            return null;
        }

        internal static string? ValidateBomAliases(PropertyMappingConfig draft)
        {
            var aliases = new (string Role, string? Value)[]
            {
                ("IPN",       draft.BomColumnIpn),
                ("Qty",       draft.BomColumnQty),
                ("Reference", draft.BomColumnReference),
                ("Note",      draft.BomColumnNote),
            };

            var allSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (role, value) in aliases)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return $"BOM Column Alias for {role} cannot be blank.";

                var trimmed = value!.Trim();
                if (trimmed.Length != value!.Length)
                    return $"BOM Column Alias for {role} cannot start or end with a comma or space.";

                var tokens = trimmed
                    .Split(',')
                    .Select(t => t.Trim())
                    .ToList();

                if (tokens.Any(string.IsNullOrWhiteSpace))
                    return $"BOM Column Alias for {role} contains a blank entry.";

                var seenInColumn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in tokens)
                {
                    if (!seenInColumn.Add(token))
                        return $"BOM Column Alias for {role} contains duplicate alias '{token}'.";

                    if (!allSeen.Add(token))
                        return $"BOM Column Alias '{token}' is used for more than one field.";
                }
            }

            return null;
        }

        // ── Draft helpers ──────────────────────────────────────────────────────

        private PropertyMappingConfig BuildDraft()
        {
            return new PropertyMappingConfig
            {
                SchemaVersion       = PropertyMappingConfig.CurrentSchemaVersion,
                IpnProperty         = NullIfWhiteSpace(_ipnProperty),
                NameProperty        = NullIfWhiteSpace(_nameProperty),
                DescriptionProperty = NullIfWhiteSpace(_descriptionProperty),
                RevisionProperty    = NullIfWhiteSpace(_revisionProperty),
                NotesProperty       = NullIfWhiteSpace(_notesProperty),
                PkProperty          = NullIfWhiteSpace(_pkProperty),
                BomColumnIpn        = NullIfWhiteSpace(_bomColumnIpn),
                BomColumnQty        = NullIfWhiteSpace(_bomColumnQty),
                BomColumnReference  = NullIfWhiteSpace(_bomColumnReference),
                BomColumnNote       = NullIfWhiteSpace(_bomColumnNote),
                ExtensionData       = CloneExtensionData(_original.ExtensionData)
            };
        }

        private void RevertToOriginal()
        {
            _ipnProperty         = _original.IpnProperty         ?? string.Empty;
            _nameProperty        = _original.NameProperty        ?? string.Empty;
            _descriptionProperty = _original.DescriptionProperty ?? string.Empty;
            _revisionProperty    = _original.RevisionProperty    ?? string.Empty;
            _notesProperty       = _original.NotesProperty       ?? string.Empty;
            _pkProperty          = _original.PkProperty          ?? string.Empty;

            _bomColumnIpn        = _original.BomColumnIpn        ?? string.Empty;
            _bomColumnQty        = _original.BomColumnQty        ?? string.Empty;
            _bomColumnReference  = _original.BomColumnReference  ?? string.Empty;
            _bomColumnNote       = _original.BomColumnNote       ?? string.Empty;

            OnPropertyChanged(string.Empty);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static PropertyMappingConfig CloneConfig(PropertyMappingConfig source)
        {
            return new PropertyMappingConfig
            {
                SchemaVersion       = source.SchemaVersion,
                IpnProperty         = source.IpnProperty,
                NameProperty        = source.NameProperty,
                NotesProperty       = source.NotesProperty,
                RevisionProperty    = source.RevisionProperty,
                DescriptionProperty = source.DescriptionProperty,
                PkProperty          = source.PkProperty,
                BomColumnIpn        = source.BomColumnIpn,
                BomColumnQty        = source.BomColumnQty,
                BomColumnReference  = source.BomColumnReference,
                BomColumnNote       = source.BomColumnNote,
                ExtensionData       = CloneExtensionData(source.ExtensionData)
            };
        }

        private static Dictionary<string, JsonElement> CloneExtensionData(
            Dictionary<string, JsonElement> source)
        {
            return new Dictionary<string, JsonElement>(source, StringComparer.OrdinalIgnoreCase);
        }

        private static string? NullIfWhiteSpace(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
