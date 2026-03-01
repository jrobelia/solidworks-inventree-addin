using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
        private bool   _pushRevisionVisible;
        private bool   _pushImageVisible;
        private bool   _fetchEnabled;
        private bool   _propertiesSectionVisible;
        private StatusSeverity _statusSeverity  = StatusSeverity.None;

        /// <summary>User-editable IPN entry box.</summary>
        public string PartNumber
        {
            get => _partNumber;
            set => Set(ref _partNumber, value);
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
            set => Set(ref _revisionPreview, value);
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
            set => Set(ref _currentRevision, value);
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

        /// <summary>Controls Load button enabled state.</summary>
        public bool FetchEnabled
        {
            get => _fetchEnabled;
            private set => Set(ref _fetchEnabled, value);
        }

        /// <summary>True once a document is open (shows the comparison grid).</summary>
        public bool PropertiesSectionVisible
        {
            get => _propertiesSectionVisible;
            private set
            {
                Set(ref _propertiesSectionVisible, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
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

        // ── State ─────────────────────────────────────────────────────────────

        private InventreePart? _lastFetchedPart;

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

        /// <summary>Full constructor used by the production add-in.</summary>
        public TaskPaneViewModel(
            IInventreeClient?        client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService)
        {
            _client          = client;
            _propertyService = propertyService;
            _viewportService = viewportService;
            _uiContext       = SynchronizationContext.Current;

            LoadPartNumber();
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
            var partNo = _propertyService.GetCustomProperty("PartNo");

            if (string.IsNullOrEmpty(partNo))
            {
                ClearAll();
                return;
            }

            PartNumber                = partNo;
            PropertiesSectionVisible  = true;
            RefreshCurrentProperties();
            ResetInvenTreeState();
        }

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            PartNumber               = string.Empty;
            CurrentName              = string.Empty;
            CurrentNotes             = string.Empty;
            CurrentRevision          = string.Empty;
            PropertiesSectionVisible = false;

            ResetInvenTreeState();

            if (_client != null)
            {
                FetchEnabled = false;
                SetStatus("Open a part in SolidWorks to get started.", StatusSeverity.None);
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
                FetchEnabled = false;
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled = true;
                SetStatus(string.Empty, StatusSeverity.None);
            }
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
                SetStatus("Open a part in SolidWorks to get started.", StatusSeverity.None);
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

            InventreePart? part      = null;
            Exception?     fetchError = null;

            try   { part = await _client.GetPartByIpnAsync(ipn).ConfigureAwait(false); }
            catch (Exception ex) { fetchError = ex; }

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
                ApplyEnabled     = true;
                ApplyNameEnabled = true;
                ApplyNotesEnabled = true;
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

            _propertyService.SetCustomProperty("Description", _lastFetchedPart.Name);
            _propertyService.SetCustomProperty("Notes",       _lastFetchedPart.Notes);

            CurrentName  = _lastFetchedPart.Name;
            CurrentNotes = _lastFetchedPart.Notes;

            SetStatus("\u2713  Applied to document.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Name field to the SolidWorks document.</summary>
        public void ApplyNameToDocument()
        {
            if (_lastFetchedPart == null) return;

            var value = NamePreview;
            _propertyService.SetCustomProperty("Description", value);
            CurrentName = value;
            SetStatus("\u2713  Name applied.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Notes field to the SolidWorks document.</summary>
        public void ApplyNotesToDocument()
        {
            if (_lastFetchedPart == null) return;

            var value = NotesPreview;
            _propertyService.SetCustomProperty("Notes", value);
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

            var revision = _propertyService.GetCustomProperty("Revision");
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

                RunOnUiThread(() =>
                    SetStatus("\u2713  Image pushed to InvenTree.", StatusSeverity.Success));
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
            CurrentName     = _propertyService.GetCustomProperty("Description");
            CurrentNotes    = _propertyService.GetCustomProperty("Notes");
            CurrentRevision = _propertyService.GetCustomProperty("Revision");
        }

        private void ResetInvenTreeState()
        {
            NamePreview      = string.Empty;
            NotesPreview     = string.Empty;
            RevisionPreview  = string.Empty;
            ApplyEnabled     = false;
            ApplyNameEnabled = false;
            ApplyNotesEnabled = false;
            PushRevisionVisible = false;
            PushImageVisible    = false;
            _lastFetchedPart    = null;

            if (_client == null)
            {
                FetchEnabled = false;
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled = true;
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
