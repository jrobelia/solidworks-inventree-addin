using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
    public class TaskPaneViewModel : INotifyPropertyChanged
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

        // ── Bindable properties ───────────────────────────────────────────────

        private string _partNumber              = string.Empty;
        private string _namePreview             = string.Empty;
        private string _notesPreview            = string.Empty;
        private string _revisionPreview         = string.Empty;
        private string _currentName             = string.Empty;
        private string _currentNotes            = string.Empty;
        private string _currentRevision         = string.Empty;
        private string _statusText              = string.Empty;
        private bool   _applyEnabled;
        private bool   _applyNameEnabled;
        private bool   _applyNotesEnabled;
        private bool   _pushNameEnabled;
        private bool   _pushNotesEnabled;
        private bool   _pushRevisionVisible;
        private bool   _pushImageVisible;
        private bool   _fetchEnabled;
        private bool   _createPartEnabled;
        private bool   _isDocumentOpen;
        private bool   _propertiesSectionVisible;
        private byte[]? _thumbnailBytes;
        private StatusSeverity _statusSeverity  = StatusSeverity.None;

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

        // ── Behaviour ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the OA Part Number from the open document and prepares
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
                return;
            }

            _isDocumentOpen          = true;
            PartNumber               = partNo;
            PropertiesSectionVisible = true;
            RefreshCurrentProperties();
            ResetInvenTreeState();
        }

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            _isDocumentOpen          = false;
            PartNumber               = string.Empty;
            CurrentName              = string.Empty;
            CurrentNotes             = string.Empty;
            CurrentRevision          = string.Empty;
            PropertiesSectionVisible = false;

            ResetInvenTreeState();

            if (_client != null)
            {
                FetchEnabled      = false;
                // CreatePartEnabled is recomputed from CanCreatePart() via the
                // PartNumber setter above — no explicit set needed here.
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
            }
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
                RunOnUiThread(() =>
                {
                    PartNumber               = part.Ipn ?? string.Empty;
                    _isDocumentOpen          = true;
                    NamePreview              = part.Name ?? string.Empty;
                    NotesPreview             = part.Notes ?? string.Empty;
                    RevisionPreview          = part.Revision ?? string.Empty;
                    ThumbnailBytes           = null;
                    PropertiesSectionVisible = true;
                    ApplyEnabled             = true;
                    ApplyNameEnabled         = true;
                    ApplyNotesEnabled        = true;
                    PushNameEnabled          = true;
                    PushNotesEnabled         = true;
                    PushRevisionVisible      = true;
                    PushImageVisible         = true;
                    _lastFetchedPart         = part;
                    RefreshCurrentProperties();
                    CreatePartEnabled        = CanCreatePart();
                    SetStatus("\u2713  Part created in InvenTree.", StatusSeverity.Success);
                });
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

            InventreePart? part       = null;
            byte[]?        thumbBytes  = null;
            Exception?     fetchError  = null;

            try   { part = await _client.GetPartByIpnAsync(ipn).ConfigureAwait(false); }
            catch (Exception ex) { fetchError = ex; }

            if (part != null && !string.IsNullOrEmpty(part.ThumbnailUrl))
            {
                try   { thumbBytes = await _client.DownloadImageAsync(part.ThumbnailUrl!).ConfigureAwait(false); }
                catch { /* silent — placeholder will show */ }
            }

            RunOnUiThread(() =>
            {
                if (fetchError != null)
                {
                    SetStatus($"Error: {fetchError.Message}", StatusSeverity.Error);
                    return;
                }

                if (part == null)
                {
                    SetStatus($"No part found in InvenTree for: {ipn}", StatusSeverity.Warning);
                    return;
                }

                NamePreview      = part.Name;
                NotesPreview     = part.Notes;
                RevisionPreview  = part.Revision;
                ThumbnailBytes    = thumbBytes;
                ApplyEnabled      = true;
                ApplyNameEnabled  = true;
                ApplyNotesEnabled = true;
                PushNameEnabled   = true;
                PushNotesEnabled  = true;
                PushRevisionVisible = true;
                PushImageVisible    = true;
                _lastFetchedPart    = part;
                SetStatus(string.Empty, StatusSeverity.None);
            });
        }

        /// <summary>Writes Name and Notes from InvenTree to the SolidWorks document.</summary>
        public void ApplyToDocument()
        {
            if (_lastFetchedPart == null) return;

            var mapping = GetMappingOrDefault();
            _propertyService.SetCustomProperty(mapping.NameProperty,  _lastFetchedPart.Name);
            _propertyService.SetCustomProperty(mapping.NotesProperty, _lastFetchedPart.Notes);

            CurrentName  = _lastFetchedPart.Name;
            CurrentNotes = _lastFetchedPart.Notes;

            SetStatus("\u2713  Applied to document.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Name field to the SolidWorks document.</summary>
        public void ApplyNameToDocument()
        {
            if (_lastFetchedPart == null) return;

            var value = NamePreview;
            _propertyService.SetCustomProperty(GetMappingOrDefault().NameProperty, value);
            CurrentName = value;
            SetStatus("\u2713  Name applied.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Notes field to the SolidWorks document.</summary>
        public void ApplyNotesToDocument()
        {
            if (_lastFetchedPart == null) return;

            var value = NotesPreview;
            _propertyService.SetCustomProperty(GetMappingOrDefault().NotesProperty, value);
            CurrentNotes = value;
            SetStatus("\u2713  Notes applied.", StatusSeverity.Success);
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
            SetStatus("Pushing revision to InvenTree\u2026", StatusSeverity.None);

            if (_client == null)
            {
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
                return;
            }

            try
            {
                await _client.UpdatePartRevisionAsync(_lastFetchedPart.Pk, revision)
                              .ConfigureAwait(false);

                RunOnUiThread(() =>
                {
                    _lastFetchedPart.Revision = revision;
                    RevisionPreview           = revision;
                    SetStatus("\u2713  Revision pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>Pushes the current SolidWorks name/description up to InvenTree.</summary>
        public async Task PushNameToInvenTreeAsync()
        {
            if (_lastFetchedPart == null || _lastFetchedPart.Pk == 0) return;
            if (_client == null) return;

            var mapping = GetMappingOrDefault();
            var name    = _propertyService.GetCustomProperty(mapping.NameProperty);
            SetStatus("Pushing name to InvenTree\u2026", StatusSeverity.None);

            try
            {
                await _client.UpdatePartNameAsync(_lastFetchedPart.Pk, name)
                              .ConfigureAwait(false);

                RunOnUiThread(() =>
                {
                    _lastFetchedPart.Name = name;
                    NamePreview           = name;
                    SetStatus("\u2713  Name pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>Pushes the current SolidWorks notes up to InvenTree.</summary>
        public async Task PushNotesToInvenTreeAsync()
        {
            if (_lastFetchedPart == null || _lastFetchedPart.Pk == 0) return;
            if (_client == null) return;

            var mapping = GetMappingOrDefault();
            var notes   = _propertyService.GetCustomProperty(mapping.NotesProperty);
            SetStatus("Pushing notes to InvenTree\u2026", StatusSeverity.None);

            try
            {
                await _client.UpdatePartNotesAsync(_lastFetchedPart.Pk, notes)
                              .ConfigureAwait(false);

                RunOnUiThread(() =>
                {
                    _lastFetchedPart.Notes = notes;
                    NotesPreview           = notes;
                    SetStatus("\u2713  Notes pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>
        /// Captures the viewport (or uses <paramref name="imageOverride"/> for tests),
        /// runs it through <see cref="ImagePipeline"/>, and uploads the PNG to InvenTree.
        /// </summary>
        public async Task PushImageAsync(Image? imageOverride = null)
        {
            if (_lastFetchedPart == null || _lastFetchedPart.Pk == 0) return;
            if (_client == null) return;

            Image?    image     = null;
            bool      ownImage  = false;
            Rectangle cropRect  = Rectangle.Empty;

            try
            {
                if (imageOverride != null)
                {
                    image = imageOverride;
                }
                else if (_viewportService != null)
                {
                    image    = _viewportService.CaptureViewportImage();
                    ownImage = true;

                    var cropWindow = new ImageCropWindow(image);
                    if (cropWindow.ShowDialog() != true)
                        return;
                    cropRect = cropWindow.CropRectangle;
                }
                else
                {
                    return;
                }

                byte[] pngData = ImagePipeline.Process(image, cropRect);
                SetStatus("Uploading image to InvenTree\u2026", StatusSeverity.None);

                await _client.UploadPartImageAsync(_lastFetchedPart.Pk, pngData)
                              .ConfigureAwait(false);

                // Re-fetch the part to get the updated thumbnail URL (the old
                // URL may be null if the part previously had no image).
                byte[]? newThumb = null;
                try
                {
                    var refreshed = await _client.GetPartByIpnAsync(_lastFetchedPart.Ipn)
                                                  .ConfigureAwait(false);
                    if (refreshed != null && !string.IsNullOrEmpty(refreshed.ThumbnailUrl))
                    {
                        _lastFetchedPart.ThumbnailUrl = refreshed.ThumbnailUrl;
                        newThumb = await _client.DownloadImageAsync(refreshed.ThumbnailUrl!)
                                                 .ConfigureAwait(false);
                    }
                }
                catch { /* silent — stale thumbnail stays until next fetch */ }

                RunOnUiThread(() =>
                {
                    if (newThumb != null) ThumbnailBytes = newThumb;
                    SetStatus("\u2713  Image pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                    SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
            finally
            {
                if (ownImage) image?.Dispose();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshCurrentProperties()
        {
            var mapping = GetMappingOrDefault();
            CurrentName     = _propertyService.GetCustomProperty(mapping.NameProperty);
            CurrentNotes    = _propertyService.GetCustomProperty(mapping.NotesProperty);
            CurrentRevision = _propertyService.GetCustomProperty(mapping.RevisionProperty);
        }

        private void ResetInvenTreeState()
        {
            NamePreview      = string.Empty;
            NotesPreview     = string.Empty;
            RevisionPreview  = string.Empty;
            ThumbnailBytes    = null;
            ApplyEnabled      = false;
            ApplyNameEnabled  = false;
            ApplyNotesEnabled = false;
            PushNameEnabled   = false;
            PushNotesEnabled  = false;
            PushRevisionVisible = false;
            PushImageVisible    = false;
            _lastFetchedPart    = null;

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
