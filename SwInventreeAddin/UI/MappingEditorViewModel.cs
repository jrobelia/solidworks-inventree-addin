using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

        private void SetDraftString(Action<string?> setter, Func<string?> getter, string value,
                                    [CallerMemberName] string? name = null)
        {
            var valueOrEmpty = value ?? string.Empty;
            if ((getter() ?? string.Empty) == valueOrEmpty) return;
            setter(valueOrEmpty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Dependencies and original state ────────────────────────────────────

        private readonly IPropertyMappingProvider _provider;
        private          PropertyMappingConfig    _original;
        private          PropertyMappingConfig    _draft;
        private readonly MappingResult            _result;
        private readonly bool                     _isReadOnly;
        private readonly MappingHealth            _health;

        private          bool                       _copyToLocalCompleted;

        private          string?                    _errorMessage;
        private          string?                    _copyToLocalInstruction;

        // ── Defaults ───────────────────────────────────────────────────────────

        private static PropertyMappingConfig DefaultConfig()
            => PropertyMappingConfig.WithDefaults();

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
            _original = _result.Config.Clone();
            _draft    = _original.Clone();
            _health   = _result.Health;
            _isReadOnly = !_result.CanEdit || _provider.IsReadOnly;
        }

        // ── Bindable properties ────────────────────────────────────────────────

        public string IpnProperty
        {
            get => _draft.IpnProperty         ?? string.Empty;
            set => SetDraftString(v => _draft.IpnProperty         = v, () => _draft.IpnProperty,         value);
        }

        public string NameProperty
        {
            get => _draft.NameProperty        ?? string.Empty;
            set => SetDraftString(v => _draft.NameProperty        = v, () => _draft.NameProperty,        value);
        }

        public string DescriptionProperty
        {
            get => _draft.DescriptionProperty ?? string.Empty;
            set => SetDraftString(v => _draft.DescriptionProperty = v, () => _draft.DescriptionProperty, value);
        }

        public string RevisionProperty
        {
            get => _draft.RevisionProperty    ?? string.Empty;
            set => SetDraftString(v => _draft.RevisionProperty    = v, () => _draft.RevisionProperty,    value);
        }

        public string NotesProperty
        {
            get => _draft.NotesProperty       ?? string.Empty;
            set => SetDraftString(v => _draft.NotesProperty       = v, () => _draft.NotesProperty,       value);
        }

        public string PkProperty
        {
            get => _draft.PkProperty          ?? string.Empty;
            set => SetDraftString(v => _draft.PkProperty          = v, () => _draft.PkProperty,          value);
        }

        public string BomColumnIpn
        {
            get => _draft.BomColumnIpn        ?? string.Empty;
            set => SetDraftString(v => _draft.BomColumnIpn        = v, () => _draft.BomColumnIpn,        value);
        }

        public string BomColumnQty
        {
            get => _draft.BomColumnQty        ?? string.Empty;
            set => SetDraftString(v => _draft.BomColumnQty        = v, () => _draft.BomColumnQty,        value);
        }

        public string BomColumnReference
        {
            get => _draft.BomColumnReference  ?? string.Empty;
            set => SetDraftString(v => _draft.BomColumnReference  = v, () => _draft.BomColumnReference,  value);
        }

        public string BomColumnNote
        {
            get => _draft.BomColumnNote       ?? string.Empty;
            set => SetDraftString(v => _draft.BomColumnNote       = v, () => _draft.BomColumnNote,       value);
        }

        public string IpnPlaceholder         => DefaultConfig().IpnProperty!;
        public string NamePlaceholder        => DefaultConfig().NameProperty!;
        public string DescriptionPlaceholder => DefaultConfig().DescriptionProperty!;
        public string RevisionPlaceholder    => DefaultConfig().RevisionProperty!;
        public string NotesPlaceholder       => DefaultConfig().NotesProperty!;
        public string PkPlaceholder          => DefaultConfig().PkProperty!;

        public string BomColumnIpnPlaceholder       => DefaultConfig().BomColumnIpn!;
        public string BomColumnQtyPlaceholder       => DefaultConfig().BomColumnQty!;
        public string BomColumnReferencePlaceholder => DefaultConfig().BomColumnReference!;
        public string BomColumnNotePlaceholder      => DefaultConfig().BomColumnNote!;

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

            _draft.SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion;
            var draft = _draft.Normalized();

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
                _original = draft.Clone();
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
            ErrorMessage = null;

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

        private string? Validate(PropertyMappingConfig draft)
        {
            var aliasError = ValidateBomAliases(draft);
            if (!string.IsNullOrEmpty(aliasError))
                return aliasError;

            var result = _provider.ValidateMapping(draft);
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

        private void RevertToOriginal()
        {
            _draft = _original.Clone();
            OnPropertyChanged(string.Empty);
        }
    }
}
