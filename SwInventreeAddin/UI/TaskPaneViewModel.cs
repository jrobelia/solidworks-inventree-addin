using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// All business logic for the InvenTree task pane.
    /// Pure C# — no WinForms or WPF types so it is fully unit-testable
    /// without an STA thread or UI handle.
    /// </summary>
    public class TaskPaneViewModel : INotifyPropertyChanged, IBomReadinessSource
    {
        // ── INotifyPropertyChanged ─────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // ── Dependencies ──────────────────────────────────────────────────────

        private IInventreeClient?                 _client;
        private readonly IDocumentPropertyService _propertyService;
        private readonly IViewportCaptureService? _viewportService;
        private IPropertyMappingProvider?         _mappingProvider;
        private const string ExpectedMappingSchemaVersion = PropertyMappingConfig.CurrentSchemaVersion;

        /// <summary>Raised when the user triggers the Settings action.</summary>
        public event EventHandler? SettingsRequested;

        /// <summary>Raised when the user clicks Compare BOM.</summary>
        public event EventHandler? CompareBomRequested;

        /// <summary>
        /// Called before any write to SW when one or more mapped property names don't already
        /// exist in the document. Return true to proceed (property will be created), false to
        /// abort. Default always proceeds.
        /// </summary>
        public Func<IReadOnlyList<string>, bool> ConfirmMissingProperties { get; set; } = _ => true;

        /// <summary>
        /// Called when an IPN resolves to multiple parts and exactly one revision matches SW.
        /// Receives (allCandidates, matchedPart). Return true to load the matched part, false to cancel.
        /// Default always proceeds.
        /// </summary>
        public Func<IReadOnlyList<InventreePart>, InventreePart, bool> ConfirmDuplicateIpn { get; set; } = (_, __) => true;

        // ── Bindable properties ───────────────────────────────────────────────

        private string _partNumber              = string.Empty;
        private string _namePreview             = string.Empty;
        private string _notesPreview            = string.Empty;
        private string _revisionPreview         = string.Empty;
        private string _descriptionPreview      = string.Empty;
        private string _pkPreview               = string.Empty;
        private string _currentName             = string.Empty;
        private string _currentNotes            = string.Empty;
        private string _currentRevision         = string.Empty;
        private string _currentDescription      = string.Empty;
        private string _currentPk               = string.Empty;
        private string _statusText              = string.Empty;
        private bool   _applyEnabled;
        private bool   _applyNameEnabled;
        private bool   _applyNotesEnabled;
        private bool   _applyDescriptionEnabled;
        private bool   _applyPkEnabled;
        private bool   _pushNameEnabled;
        private bool   _pushNotesEnabled;
        private bool   _pushDescriptionEnabled;
        private bool   _pushRevisionVisible;
        private bool   _pushImageVisible;
        private bool   _fetchEnabled;
        private bool   _createPartEnabled;
        private bool   _isDocumentOpen;
        private bool   _propertiesSectionVisible;
        private byte[]? _thumbnailBytes;
        private string  _inStockDisplay    = string.Empty;
        private string  _orderingDisplay   = string.Empty;
        private string  _activeDisplay     = string.Empty;
        private StatusSeverity _statusSeverity  = StatusSeverity.None;
        private string _bomStatusText = "BOM: Not checked";

        /// <summary>
        /// The type of the currently active SolidWorks document.
        /// Set at the top of LoadPartNumber() on every document switch.
        /// All future per-type logic (property mapping, enable/disable switches) reads from here.
        /// </summary>
        private DocumentType _currentDocumentType = DocumentType.Unknown;

        /// <summary>User-editable IPN entry box.</summary>
        public string PartNumber
        {
            get => _partNumber;
            set
            {
                Set(ref _partNumber, value);
                CreatePartEnabled = CanCreatePart();
            }
        }

        /// <summary>Name fetched from InvenTree.</summary>
        public string NamePreview
        {
            get => _namePreview;
            set
            {
                Set(ref _namePreview, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
            }
        }

        /// <summary>Notes fetched from InvenTree.</summary>
        public string NotesPreview
        {
            get => _notesPreview;
            set
            {
                Set(ref _notesPreview, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
            }
        }

        /// <summary>Revision fetched from InvenTree (or pushed).</summary>
        public string RevisionPreview
        {
            get => _revisionPreview;
            set
            {
                Set(ref _revisionPreview, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
            }
        }

        /// <summary>Description fetched from InvenTree.</summary>
        public string DescriptionPreview
        {
            get => _descriptionPreview;
            set
            {
                Set(ref _descriptionPreview, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
            }
        }

        /// <summary>InvenTree PK as a display string.</summary>
        public string PkPreview
        {
            get => _pkPreview;
            set
            {
                Set(ref _pkPreview, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
            }
        }

        /// <summary>Current SolidWorks document Name / Description value.</summary>
        public string CurrentName
        {
            get => _currentName;
            set
            {
                Set(ref _currentName, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
            }
        }

        /// <summary>Current SolidWorks document Notes value.</summary>
        public string CurrentNotes
        {
            get => _currentNotes;
            set
            {
                Set(ref _currentNotes, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
            }
        }

        /// <summary>Current SolidWorks document Revision value.</summary>
        public string CurrentRevision
        {
            get => _currentRevision;
            set
            {
                Set(ref _currentRevision, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
            }
        }

        /// <summary>Current SolidWorks document Description Long value.</summary>
        public string CurrentDescription
        {
            get => _currentDescription;
            set
            {
                Set(ref _currentDescription, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
            }
        }

        /// <summary>Current SolidWorks InvenTree PK property value.</summary>
        public string CurrentPk
        {
            get => _currentPk;
            set
            {
                Set(ref _currentPk, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
            }
        }

        /// <summary>Status bar message.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        /// <summary>Colour signal for the status bar stripe.</summary>
        public StatusSeverity StatusSeverity
        {
            get => _statusSeverity;
            private set => Set(ref _statusSeverity, value);
        }

        /// <summary>True when a part has been fetched and Apply is meaningful.</summary>
        public bool ApplyEnabled
        {
            get => _applyEnabled;
            private set => Set(ref _applyEnabled, value);
        }

        /// <summary>True when individual Name apply is available.</summary>
        public bool ApplyNameEnabled
        {
            get => _applyNameEnabled;
            private set => Set(ref _applyNameEnabled, value);
        }

        /// <summary>True when individual Notes apply is available.</summary>
        public bool ApplyNotesEnabled
        {
            get => _applyNotesEnabled;
            private set => Set(ref _applyNotesEnabled, value);
        }

        /// <summary>Controls Push Revision button visibility.</summary>
        public bool PushRevisionVisible
        {
            get => _pushRevisionVisible;
            private set => Set(ref _pushRevisionVisible, value);
        }

        /// <summary>Controls Push Image button visibility.</summary>
        public bool PushImageVisible
        {
            get => _pushImageVisible;
            private set => Set(ref _pushImageVisible, value);
        }

        /// <summary>Raw PNG/JPEG bytes of the InvenTree part thumbnail. Null when none fetched.</summary>
        public byte[]? ThumbnailBytes
        {
            get => _thumbnailBytes;
            private set => Set(ref _thumbnailBytes, value);
        }

        /// <summary>In-stock quantity display string (e.g. "15.5").</summary>
        public string InStockDisplay
        {
            get => _inStockDisplay;
            private set => Set(ref _inStockDisplay, value);
        }

        /// <summary>On-order quantity display string (e.g. "100").</summary>
        public string OrderingDisplay
        {
            get => _orderingDisplay;
            private set => Set(ref _orderingDisplay, value);
        }

        /// <summary>"Active" or "Inactive".</summary>
        public string ActiveDisplay
        {
            get => _activeDisplay;
            private set => Set(ref _activeDisplay, value);
        }

        /// <summary>Controls Load button enabled state.</summary>
        public bool FetchEnabled
        {
            get => _fetchEnabled;
            private set => Set(ref _fetchEnabled, value);
        }

        /// <summary>Controls Create Part button enabled state.</summary>
        public bool CreatePartEnabled
        {
            get => _createPartEnabled;
            private set => Set(ref _createPartEnabled, value);
        }

        private bool CanCreatePart() =>
            _client != null && string.IsNullOrEmpty(_partNumber) && _isDocumentOpen;

        /// <summary>BOM status summary text shown in the task pane BOM section.</summary>
        public string BomStatusText
        {
            get => _bomStatusText;
            private set => Set(ref _bomStatusText, value);
        }

        /// <summary>True when an assembly is open — shows the BOM section.</summary>
        public bool BomSectionVisible =>
            _isDocumentOpen && _currentDocumentType == DocumentType.Assembly;

        /// <summary>True when BOM compare button should be enabled.</summary>
        public bool BomButtonEnabled =>
            _isDocumentOpen && _currentDocumentType == DocumentType.Assembly && _client != null;

        /// <summary>The InvenTree PK of the currently fetched part. Zero when none fetched.</summary>
        public int CurrentInvenTreePk => _lastFetchedPart?.Pk ?? 0;

        /// <summary>True once a document is open (shows the comparison grid).</summary>
        public bool PropertiesSectionVisible
        {
            get => _propertiesSectionVisible;
            private set
            {
                Set(ref _propertiesSectionVisible, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
            }
        }

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree values match. False = mismatch.
        /// </summary>
        public bool? NameMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentName?.Trim(), _namePreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree values match. False = mismatch.
        /// </summary>
        public bool? NotesMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentNotes?.Trim(), _notesPreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree revisions match. False = mismatch.
        /// </summary>
        public bool? RevisionMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentRevision?.Trim(), _revisionPreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree descriptions match. False = mismatch.
        /// </summary>
        public bool? DescriptionMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentDescription?.Trim(), _descriptionPreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree PK values match. False = mismatch.
        /// </summary>
        public bool? PkMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentPk?.Trim(), _pkPreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>True when a part has been fetched and pushing Name to InvenTree is meaningful.</summary>
        public bool PushNameEnabled
        {
            get => _pushNameEnabled;
            private set => Set(ref _pushNameEnabled, value);
        }

        /// <summary>True when a part has been fetched and pushing Notes to InvenTree is meaningful.</summary>
        public bool PushNotesEnabled
        {
            get => _pushNotesEnabled;
            private set => Set(ref _pushNotesEnabled, value);
        }

        /// <summary>True when individual Description apply is available.</summary>
        public bool ApplyDescriptionEnabled
        {
            get => _applyDescriptionEnabled;
            private set => Set(ref _applyDescriptionEnabled, value);
        }

        /// <summary>True when a part has been fetched and pushing Description to InvenTree is meaningful.</summary>
        public bool PushDescriptionEnabled
        {
            get => _pushDescriptionEnabled;
            private set => Set(ref _pushDescriptionEnabled, value);
        }

        /// <summary>True when a part has been fetched and applying PK to SW doc is meaningful.</summary>
        public bool ApplyPkEnabled
        {
            get => _applyPkEnabled;
            private set => Set(ref _applyPkEnabled, value);
        }

        // ── State ─────────────────────────────────────────────────────────────

        private InventreePart? _lastFetchedPart;
        private bool _schemaMismatchActive;

        /// <summary>
        /// UI-thread synchronisation context captured at construction.
        /// Null when constructed on a thread-pool thread (unit tests) — in
        /// that case RunOnUiThread executes actions inline.
        /// </summary>
        private readonly SynchronizationContext? _uiContext;

        // ── Constructors ──────────────────────────────────────────────────────

        /// <summary>Two-service constructor (no viewport capture — e.g. unit tests).</summary>
        public TaskPaneViewModel(IInventreeClient? client, IDocumentPropertyService propertyService)
            : this(client, propertyService, null) { }

        /// <summary>Three-service constructor (no mapping provider).</summary>
        public TaskPaneViewModel(
            IInventreeClient?        client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService)
            : this(client, propertyService, viewportService, null) { }

        /// <summary>Full constructor used by the production add-in.</summary>
        public TaskPaneViewModel(
            IInventreeClient?         client,
            IDocumentPropertyService  propertyService,
            IViewportCaptureService?  viewportService,
            IPropertyMappingProvider? mappingProvider = null)
        {
            _client          = client;
            _propertyService = propertyService;
            _viewportService = viewportService;
            _mappingProvider = mappingProvider;
            _uiContext       = SynchronizationContext.Current;

            LoadPartNumber();
            CheckMappingSchema();
        }

        // ── Commands (called by WPF bindings and forwarded by the shim) ───────

        /// <summary>Settings button — raises the SettingsRequested event.</summary>
        public void RequestSettings() =>
            SettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Compare BOM button — raises the CompareBomRequested event.</summary>
        public void RequestCompareBom() =>
            CompareBomRequested?.Invoke(this, EventArgs.Empty);

        // ── Behaviour ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the IPN from the open document and prepares
        /// the panel for the user to fetch from InvenTree.
        /// </summary>
        public void LoadPartNumber()
        {
            _currentDocumentType = _propertyService.GetDocumentType();

            if (_currentDocumentType == DocumentType.Drawing)
            {
                ClearAll();
                SetStatus("Drawings are not supported \u2014 open a part or assembly.",
                          StatusSeverity.Warning);
                return;
            }

            if (_currentDocumentType == DocumentType.Unknown)
            {
                // No document open — Create is not meaningful.
                ClearAll();
                return;
            }

            var mapping = GetMappingOrDefault();
            var partNo = _propertyService.GetCustomProperty(mapping.IpnProperty);

            if (string.IsNullOrEmpty(partNo))
            {
                ClearAll();
                _isDocumentOpen   = true;   // blank part: doc IS open, Create should be enabled
                CreatePartEnabled = CanCreatePart();
                NotifyBomVisibility();
                return;
            }

            _isDocumentOpen          = true;
            PartNumber               = partNo;
            PropertiesSectionVisible = true;
            RefreshCurrentProperties();

            // If we already have fetched data for this IPN (e.g. just created the part),
            // stay in POPULATED state — don't blow away the previews and button state.
            if (_lastFetchedPart == null || _lastFetchedPart.Ipn != partNo)
                ResetInvenTreeState();

            NotifyBomVisibility();
        }

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            _isDocumentOpen          = false;
            PartNumber               = string.Empty;
            CurrentName              = string.Empty;
            CurrentNotes             = string.Empty;
            CurrentRevision          = string.Empty;
            CurrentDescription       = string.Empty;
            CurrentPk                = string.Empty;
            PropertiesSectionVisible = false;

            ResetInvenTreeState();

            if (_client != null)
            {
                FetchEnabled      = false;
                // CreatePartEnabled is recomputed from CanCreatePart() via the
                // PartNumber setter above — no explicit set needed here.
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
            }

            NotifyBomVisibility();
        }

        /// <summary>
        /// Updates the client reference — called when settings change.
        /// </summary>
        public void UpdateClient(IInventreeClient? newClient)
        {
            _client = newClient;

            if (_client == null)
            {
                FetchEnabled      = false;
                CreatePartEnabled = false;
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled      = true;
                CreatePartEnabled = CanCreatePart();
                SetStatus(string.Empty, StatusSeverity.None);
            }

            NotifyBomVisibility();
        }

        /// <summary>
        /// Creates and opens the Create Part dialog.
        /// Called from the WPF code-behind on the UI thread.
        /// The <paramref name="showDialog"/> delegate is responsible for
        /// constructing and showing the window (keeps this ViewModel free of WPF types).
        /// </summary>
        public void OpenCreatePartWindow(Action<CreatePartViewModel> showDialog)
        {
            if (_client == null) return;

            var mapping = GetMappingOrDefault();
            var name    = _propertyService.GetCustomProperty(mapping.NameProperty);

            var vm = new CreatePartViewModel(_client, _propertyService, name, _mappingProvider);

            vm.PartCreated += (_, part) =>
            {
                PartNumber        = part.Ipn ?? string.Empty;
                FetchEnabled      = !string.IsNullOrEmpty(part.Ipn);
                CreatePartEnabled = CanCreatePart();
                ApplyFetchedPart(part);

                // Write PK to SW doc on create (write-on-create only).
                if (part.Pk > 0)
                {
                    var m = GetMappingOrDefault();
                    _propertyService.SetCustomProperty(m.PkProperty, part.Pk.ToString());
                    CurrentPk = part.Pk.ToString();
                }

                SetStatus("Part created in InvenTree.", StatusSeverity.Success);
            };

            showDialog(vm);
        }

        /// <summary>
        /// Fetches part data from InvenTree for the current IPN.
        /// </summary>
        public async Task FetchPartAsync()
        {
            RefreshCurrentProperties();

            var ipn = PartNumber;
            if (string.IsNullOrEmpty(ipn))
            {
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
                return;
            }

            SetStatus("Fetching from InvenTree\u2026", StatusSeverity.None);
            ApplyEnabled      = false;
            ApplyNameEnabled  = false;
            ApplyNotesEnabled = false;

            if (_client == null)
            {
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
                return;
            }

            IReadOnlyList<InventreePart>? parts      = null;
            byte[]?                        thumbBytes  = null;
            Exception?                     fetchError  = null;

            try   { parts = await _client.GetPartsByIpnAsync(ipn).ConfigureAwait(false); }
            catch (Exception ex) { fetchError = ex; }

            // Only pre-fetch thumbnail when there is exactly one unambiguous result.
            if (parts?.Count == 1 && !string.IsNullOrEmpty(parts[0].ThumbnailUrl))
            {
                try   { thumbBytes = await _client.DownloadImageAsync(parts[0].ThumbnailUrl!).ConfigureAwait(false); }
                catch { /* silent — placeholder will show */ }
            }

            RunOnUiThread(() =>
            {
                if (fetchError != null)
                {
                    SetStatus($"Error: {fetchError.Message}", StatusSeverity.Error);
                    return;
                }

                if (parts == null || parts.Count == 0)
                {
                    SetStatus($"No part found in InvenTree for: {ipn}", StatusSeverity.Warning);
                    return;
                }

                InventreePart resolvedPart;
                byte[]?       resolvedThumb = thumbBytes;

                if (parts.Count == 1)
                {
                    resolvedPart = parts[0];
                }
                else
                {
                    // Multiple parts share this IPN — resolve by revision.
                    var swRev   = _currentRevision?.Trim() ?? string.Empty;
                    var matches = new System.Collections.Generic.List<InventreePart>();
                    foreach (var p in parts)
                    {
                        if (RevisionComparer.Compare(swRev, p.Revision?.Trim() ?? string.Empty)
                            == RevisionOrder.Equal)
                            matches.Add(p);
                    }

                    if (matches.Count == 0)
                    {
                        var revLabel = string.IsNullOrEmpty(swRev) ? "(blank)" : swRev;
                        SetStatus(
                            $"{parts.Count} parts share IPN \u2018{ipn}\u2019 but none match "
                            + $"SW revision {revLabel}. Resolve in InvenTree.",
                            StatusSeverity.Error);
                        return;
                    }

                    if (matches.Count > 1)
                    {
                        var revLabel = string.IsNullOrEmpty(swRev) ? "(blank)" : swRev;
                        SetStatus(
                            $"{parts.Count} parts share IPN \u2018{ipn}\u2019 and revision {revLabel}. "
                            + "Resolve duplicates in InvenTree.",
                            StatusSeverity.Error);
                        return;
                    }

                    // Exactly one revision match — confirm with user.
                    if (!ConfirmDuplicateIpn(parts, matches[0])) return;
                    resolvedPart  = matches[0];
                    resolvedThumb = null; // thumbnail not pre-fetched on the duplicate path
                }

                ApplyFetchedPart(resolvedPart, resolvedThumb);
                SetStatus(string.Empty, StatusSeverity.None);
            });
        }

        /// <summary>
        /// Returns property names from <paramref name="names"/> that don't exist in the
        /// SolidWorks document. Empty list means all exist.
        /// </summary>
        internal List<string> FindMissingProperties(IEnumerable<string> names)
        {
            var missing = new List<string>();
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && !_propertyService.PropertyExists(n))
                    missing.Add(n);
            return missing;
        }

        /// <summary>Writes Name and Notes from InvenTree to the SolidWorks document.</summary>
        public void ApplyToDocument()
        {
            if (_lastFetchedPart == null) return;

            var mapping = GetMappingOrDefault();
            _propertyService.SetCustomProperty(mapping.NameProperty,        _lastFetchedPart.Name);
            _propertyService.SetCustomProperty(mapping.NotesProperty,       _lastFetchedPart.Notes);
            _propertyService.SetCustomProperty(mapping.DescriptionProperty, _lastFetchedPart.Description);

            CurrentName        = _lastFetchedPart.Name;
            CurrentNotes       = _lastFetchedPart.Notes;
            CurrentDescription = _lastFetchedPart.Description;

            SetStatus("Applied to document.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Name field to the SolidWorks document.</summary>
        public void ApplyNameToDocument() =>
            ApplySingleProperty(GetMappingOrDefault().NameProperty, NamePreview, v => CurrentName = v, "Name applied.");

        /// <summary>Writes only the Notes field to the SolidWorks document.</summary>
        public void ApplyNotesToDocument() =>
            ApplySingleProperty(GetMappingOrDefault().NotesProperty, NotesPreview, v => CurrentNotes = v, "Notes applied.");

        /// <summary>Writes only the Description field to the SolidWorks document.</summary>
        public void ApplyDescriptionToDocument() =>
            ApplySingleProperty(GetMappingOrDefault().DescriptionProperty, DescriptionPreview, v => CurrentDescription = v, "Description applied.");

        /// <summary>Writes the InvenTree PK property to the SolidWorks document.</summary>
        public void ApplyPkToDocument() =>
            ApplySingleProperty(GetMappingOrDefault().PkProperty, PkPreview, v => CurrentPk = v, "InvenTree PK applied.");

        private void ApplySingleProperty(string mappedPropertyName, string value, Action<string> updateCurrentField, string successMessage)
        {
            if (_lastFetchedPart == null) return;
            var missing = FindMissingProperties(new[] { mappedPropertyName });
            if (missing.Count > 0 && !ConfirmMissingProperties(missing)) return;
            _propertyService.SetCustomProperty(mappedPropertyName, value);
            updateCurrentField(value);
            SetStatus(successMessage, StatusSeverity.Success);
        }

        /// <summary>Pushes the current SolidWorks revision up to InvenTree.</summary>
        public async Task PushRevisionToInventreeAsync()
        {
            if (_lastFetchedPart == null) return;
            if (_lastFetchedPart.Pk == 0)
            {
                SetStatus("Error: cannot push revision \u2014 InvenTree part ID is missing.",
                          StatusSeverity.Error);
                return;
            }
            var mapping  = GetMappingOrDefault();
            var revision = _propertyService.GetCustomProperty(mapping.RevisionProperty);
            await PushToInventreeAsync(
                revision,
                (c, pk, v) => c.UpdatePartRevisionAsync(pk, v),
                v => { _lastFetchedPart!.Revision = v; RevisionPreview = v; },
                "Pushing revision to InvenTree\u2026",
                "Revision pushed to InvenTree.");
        }

        /// <summary>Pushes the current SolidWorks name/description up to InvenTree.</summary>
        public Task PushNameToInvenTreeAsync()
        {
            var mapping = GetMappingOrDefault();
            var name    = _propertyService.GetCustomProperty(mapping.NameProperty);
            return PushToInventreeAsync(
                name,
                (c, pk, v) => c.UpdatePartNameAsync(pk, v),
                v => { _lastFetchedPart!.Name = v; NamePreview = v; },
                "Pushing name to InvenTree\u2026",
                "Name pushed to InvenTree.");
        }

        /// <summary>Pushes the current SolidWorks notes up to InvenTree.</summary>
        public Task PushNotesToInvenTreeAsync()
        {
            var mapping = GetMappingOrDefault();
            var notes   = _propertyService.GetCustomProperty(mapping.NotesProperty);
            return PushToInventreeAsync(
                notes,
                (c, pk, v) => c.UpdatePartNotesAsync(pk, v),
                v => { _lastFetchedPart!.Notes = v; NotesPreview = v; },
                "Pushing notes to InvenTree\u2026",
                "Notes pushed to InvenTree.");
        }

        /// <summary>Pushes the current SolidWorks description up to InvenTree.</summary>
        public Task PushDescriptionToInvenTreeAsync()
        {
            var mapping     = GetMappingOrDefault();
            var description = _propertyService.GetCustomProperty(mapping.DescriptionProperty);
            return PushToInventreeAsync(
                description,
                (c, pk, v) => c.UpdatePartDescriptionAsync(pk, v),
                v => { _lastFetchedPart!.Description = v; DescriptionPreview = v; },
                "Pushing description to InvenTree\u2026",
                "Description pushed to InvenTree.");
        }

        private async Task PushToInventreeAsync(
            string value,
            Func<IInventreeClient, int, string, Task> clientCall,
            Action<string> onSuccess,
            string pushingMessage,
            string successMessage)
        {
            if (_lastFetchedPart == null || _lastFetchedPart.Pk == 0) return;
            if (_client == null) return;
            SetStatus(pushingMessage, StatusSeverity.None);
            try
            {
                await clientCall(_client, _lastFetchedPart.Pk, value).ConfigureAwait(false);
                RunOnUiThread(() => { onSuccess(value); SetStatus(successMessage, StatusSeverity.Success); });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>
        /// Runs the Viewport Capture workflow: capture, crop, upload, and refresh the
        /// thumbnail. Delegates to <see cref="PartThumbnailService"/>.
        /// Must be called on the UI thread.
        /// </summary>
        public async Task PushImageAsync(Image? imageOverride = null)
        {
            if (_lastFetchedPart == null || _lastFetchedPart.Pk == 0) return;
            if (_client == null) return;

            var service = new PartThumbnailService(_client, _viewportService);
            try
            {
                var thumb = await service.PushAsync(
                    _lastFetchedPart.Pk,
                    _lastFetchedPart.Ipn,
                    (text, severity) => SetStatus(text, severity),
                    imageOverride).ConfigureAwait(true);

                RunOnUiThread(() =>
                {
                    if (thumb != null) ThumbnailBytes = thumb;
                    SetStatus("Image pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public void RefreshCurrentProperties()
        {
            var mapping = GetMappingOrDefault();
            CurrentName        = _propertyService.GetCustomProperty(mapping.NameProperty);
            CurrentNotes       = _propertyService.GetCustomProperty(mapping.NotesProperty);
            CurrentRevision    = _propertyService.GetCustomProperty(mapping.RevisionProperty);
            CurrentDescription = _propertyService.GetCustomProperty(mapping.DescriptionProperty);
            CurrentPk          = _propertyService.GetCustomProperty(mapping.PkProperty);
        }

        /// <summary>
        /// Applies a fetched InvenTree part to the task pane, entering POPULATED state.
        /// Called from both FetchPartAsync (after a Load) and the PartCreated handler
        /// (after a create — the part was already fetched inside CreateAsync).
        /// </summary>
        private void ApplyFetchedPart(InventreePart part, byte[]? thumbBytes = null)
        {
            PropertiesSectionVisible = true;
            NamePreview              = part.Name        ?? string.Empty;
            NotesPreview             = part.Notes       ?? string.Empty;
            RevisionPreview          = part.Revision    ?? string.Empty;
            DescriptionPreview       = part.Description ?? string.Empty;
            PkPreview                = part.Pk > 0 ? part.Pk.ToString() : string.Empty;
            ThumbnailBytes           = thumbBytes;
            InStockDisplay           = part.InStock.ToString("G29");
            OrderingDisplay          = part.Ordering.ToString("G29");
            ActiveDisplay            = part.Active ? "Active" : "Inactive";
            ApplyEnabled             = true;
            ApplyNameEnabled         = true;
            ApplyNotesEnabled        = true;
            ApplyDescriptionEnabled  = true;
            ApplyPkEnabled           = true;
            PushNameEnabled          = true;
            PushNotesEnabled         = true;
            PushDescriptionEnabled   = true;
            PushRevisionVisible      = true;
            PushImageVisible         = true;
            _lastFetchedPart         = part;
            RefreshCurrentProperties();
        }

        private void ResetInvenTreeState()
        {
            NamePreview          = string.Empty;
            NotesPreview         = string.Empty;
            RevisionPreview      = string.Empty;
            DescriptionPreview   = string.Empty;
            PkPreview            = string.Empty;
            ThumbnailBytes       = null;
            InStockDisplay       = string.Empty;
            OrderingDisplay      = string.Empty;
            ActiveDisplay        = string.Empty;
            ApplyEnabled         = false;
            ApplyNameEnabled     = false;
            ApplyNotesEnabled    = false;
            ApplyDescriptionEnabled = false;
            ApplyPkEnabled       = false;
            PushNameEnabled      = false;
            PushNotesEnabled     = false;
            PushDescriptionEnabled = false;
            PushRevisionVisible  = false;
            PushImageVisible     = false;
            _lastFetchedPart     = null;

            if (_client == null)
            {
                FetchEnabled      = false;
                CreatePartEnabled = false;
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled      = true;
                CreatePartEnabled = CanCreatePart();
                SetStatus(string.Empty, StatusSeverity.None);
            }
        }

        /// <summary>
        /// Updates the mapping provider reference and re-checks the schema version.
        /// Called after settings are saved with a new MappingSourcePath.
        /// </summary>
        public void UpdateMapping(IPropertyMappingProvider provider)
        {
            _mappingProvider = provider;
            CheckMappingSchema();
            if (_propertiesSectionVisible)
                RefreshCurrentProperties();
        }

        private PropertyMappingConfig GetMappingOrDefault() =>
            _mappingProvider?.GetMapping() ?? new PropertyMappingConfig();

        private void CheckMappingSchema()
        {
            if (_mappingProvider == null) return;

            var mapping = _mappingProvider.GetMapping();
            if (mapping.SchemaVersion != ExpectedMappingSchemaVersion)
            {
                _schemaMismatchActive = true;
                SetStatus("Mapping schema mismatch \u2014 review Settings",
                          StatusSeverity.Warning);
            }
            else if (_schemaMismatchActive)
            {
                _schemaMismatchActive = false;
                SetStatus(string.Empty, StatusSeverity.None);
            }
        }

        private void SetStatus(string text, StatusSeverity severity)
        {
            StatusText     = text;
            StatusSeverity = severity;
        }

        /// <summary>
        /// Updates the BOM status text after a BOM sync.
        /// Called by the code-behind (via subscription to BomSynced).
        /// </summary>
        public void UpdateBomStatus(int diffCount)
        {
            BomStatusText = diffCount == 0 ? "BOM: In sync" : $"BOM: {diffCount} difference(s)";
        }

        /// <summary>Fires PropertyChanged for BOM visibility properties.</summary>
        private void NotifyBomVisibility()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomSectionVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomButtonEnabled)));
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread.
        /// Uses Send (synchronous) so callers see property updates immediately.
        /// Falls back to inline execution when no context was captured (unit tests).
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (_uiContext != null && SynchronizationContext.Current != _uiContext)
                _uiContext.Send(_ => action(), null);
            else
                action();
        }
    }

    /// <summary>Severity level for the status bar stripe colour.</summary>
    public enum StatusSeverity { None, Success, Warning, Error }
}
