using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
        private readonly IConfigProvider?         _configProvider;
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

        /// <summary>
        /// Called when the InvenTree thumbnail is clicked and a part URL is available.
        /// Defaults to opening the URL in the system's default browser.
        /// </summary>
        public Action<Uri?> OpenBrowserUrl { get; set; } = url =>
        {
            if (url == null) return;
            using var _ = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        };

        // ── Bindable properties ───────────────────────────────────────────────

        private string _partNumber         = string.Empty;
        private string _currentName        = string.Empty;
        private string _currentNotes       = string.Empty;
        private string _currentRevision    = string.Empty;
        private string _currentDescription = string.Empty;
        private string _currentPk          = string.Empty;
        private string _statusText         = string.Empty;
        private bool   _fetchEnabled;
        private bool   _createPartEnabled;
        private bool   _isDocumentOpen;
        private bool   _documentPkPresent;
        private int    _documentPk;
        private bool   _propertiesSectionVisible;
        private StatusSeverity _statusSeverity = StatusSeverity.None;

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

        // ── Preview properties (computed from session) ────────────────────────

        /// <summary>Name fetched from InvenTree.</summary>
        public string NamePreview        => _session?.Part.Name        ?? string.Empty;

        /// <summary>Notes fetched from InvenTree.</summary>
        public string NotesPreview       => _session?.Part.Notes       ?? string.Empty;

        /// <summary>Revision fetched from InvenTree (or pushed).</summary>
        public string RevisionPreview    => _session?.Part.Revision    ?? string.Empty;

        /// <summary>Description fetched from InvenTree.</summary>
        public string DescriptionPreview => _session?.Part.Description ?? string.Empty;

        /// <summary>InvenTree PK as a display string.</summary>
        public string PkPreview          => _session?.Part.Pk > 0 ? _session!.Part.Pk.ToString() : string.Empty;

        /// <summary>Raw PNG/JPEG bytes of the InvenTree part thumbnail. Null when none fetched.</summary>
        public byte[]? ThumbnailBytes    => _session?.ThumbnailBytes;

        /// <summary>In-stock quantity display string (e.g. "15.5").</summary>
        public string InStockDisplay     => _session?.Part.InStock.ToString("G29") ?? string.Empty;

        /// <summary>On-order quantity display string (e.g. "100").</summary>
        public string OrderingDisplay    => _session?.Part.Ordering.ToString("G29") ?? string.Empty;

        // ── Flag chips (computed from session) ──────────────────────────────────

        /// <summary>"Active: ✓" / "Active: ✗" display text for the Active flag chip.</summary>
        public string ActiveDisplay       => FormatFlag("Active",       _session?.Part.Active);
        public bool?  ActiveValue         => _session?.Part.Active;
        public string AssemblyDisplay     => FormatFlag("Assembly",     _session?.Part.Assembly);
        public bool?  AssemblyValue       => _session?.Part.Assembly;
        public string ComponentDisplay    => FormatFlag("Component",    _session?.Part.Component);
        public bool?  ComponentValue      => _session?.Part.Component;
        public string PurchaseableDisplay => FormatFlag("Purchaseable", _session?.Part.Purchaseable);
        public bool?  PurchaseableValue   => _session?.Part.Purchaseable;
        public string SalableDisplay      => FormatFlag("Salable",      _session?.Part.Salable);
        public bool?  SalableValue        => _session?.Part.Salable;
        public string TrackableDisplay    => FormatFlag("Trackable",    _session?.Part.Trackable);
        public bool?  TrackableValue      => _session?.Part.Trackable;
        public string TestableDisplay     => FormatFlag("Testable",     _session?.Part.Testable);
        public bool?  TestableValue       => _session?.Part.Testable;

        private static string FormatFlag(string name, bool? value) =>
            value == null ? string.Empty : $"{name}: {(value.Value ? "\u2713" : "\u2717")}";

        // ── Enabled / visible flags (computed from session) ───────────────────

        /// <summary>True when a part has been fetched and Apply is meaningful.</summary>
        public bool ApplyEnabled            => _session != null;

        /// <summary>True when individual Name apply is available.</summary>
        public bool ApplyNameEnabled        => _session != null;

        /// <summary>True when individual Notes apply is available.</summary>
        public bool ApplyNotesEnabled       => _session != null;

        /// <summary>True when individual Description apply is available.</summary>
        public bool ApplyDescriptionEnabled => _session != null;

        /// <summary>True when a part has been fetched and applying PK to SW doc is meaningful.</summary>
        public bool ApplyPkEnabled          => _session != null;

        /// <summary>True when a part has been fetched and pushing Name to InvenTree is meaningful.</summary>
        public bool PushNameEnabled         => _session != null;

        /// <summary>True when a part has been fetched and pushing Notes to InvenTree is meaningful.</summary>
        public bool PushNotesEnabled        => _session != null;

        /// <summary>True when a part has been fetched and pushing Description to InvenTree is meaningful.</summary>
        public bool PushDescriptionEnabled  => _session != null;

        /// <summary>Controls Push Revision button visibility.</summary>
        public bool PushRevisionVisible     => _session != null;

        /// <summary>Controls Push Image button visibility.</summary>
        public bool PushImageVisible        => _session != null;

        /// <summary>True when the no-image placeholder icon should be shown.</summary>
        public bool ThumbnailPlaceholderVisible => _session != null && (_session.ThumbnailBytes == null || _session.ThumbnailBytes.Length == 0);

        /// <summary>True when the InvenTree thumbnail is clickable and links to the part page.</summary>
        public bool PartLinkEnabled         => _session != null && _session.PartPk > 0;

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
            _client != null
            && string.IsNullOrEmpty(_partNumber)
            && _isDocumentOpen
            && !_documentPkPresent
            && (_currentDocumentType == DocumentType.Part || _currentDocumentType == DocumentType.Assembly);

        /// <summary>True when an assembly is open — shows the BOM section.</summary>
        public bool BomSectionVisible =>
            _isDocumentOpen && _currentDocumentType == DocumentType.Assembly;

        /// <summary>True when BOM compare button should be enabled.</summary>
        public bool BomButtonEnabled =>
            _isDocumentOpen && _currentDocumentType == DocumentType.Assembly
            && _client != null && _session != null;

        /// <summary>The InvenTree PK of the currently fetched part. Zero when none fetched.</summary>
        public int CurrentInvenTreePk => _session?.Part.Pk ?? 0;

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
                ? string.Equals(_currentName?.Trim(), _session?.Part.Name?.Trim() ?? string.Empty,
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree values match. False = mismatch.
        /// </summary>
        public bool? NotesMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentNotes?.Trim(), _session?.Part.Notes?.Trim() ?? string.Empty,
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree revisions match. False = mismatch.
        /// </summary>
        public bool? RevisionMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentRevision?.Trim(), _session?.Part.Revision?.Trim() ?? string.Empty,
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree descriptions match. False = mismatch.
        /// </summary>
        public bool? DescriptionMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentDescription?.Trim(), _session?.Part.Description?.Trim() ?? string.Empty,
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        /// <summary>
        /// Null = not yet fetched. True = SW and InvenTree PK values match. False = mismatch.
        /// </summary>
        public bool? PkMatch =>
            _propertiesSectionVisible
                ? string.Equals(_currentPk?.Trim(), PkPreview?.Trim(),
                      StringComparison.OrdinalIgnoreCase)
                : (bool?)null;

        // ── State ─────────────────────────────────────────────────────────────

        private PartSyncSession? _session;
        private bool _schemaMismatchActive;

        /// <summary>
        /// UI-thread synchronisation context captured at construction.
        /// Null when constructed on a thread-pool thread (unit tests) — in
        /// that case RunOnUiThread executes actions inline.
        /// </summary>
        private readonly SynchronizationContext? _uiContext;

        /// <summary>
        /// When true, the Create Part flow polls InvenTree for a server-assigned IPN.
        /// When false (default), the poll is skipped and the dialog closes immediately.
        /// Set from <see cref="ServerConfig.WaitForAutoPartNumber"/> after config loads.
        /// </summary>
        public bool WaitForAutoPartNumber { get; set; }

        // ── Constructors ──────────────────────────────────────────────────────

        /// <summary>Two-service constructor (no viewport capture — e.g. unit tests).</summary>
        public TaskPaneViewModel(IInventreeClient? client, IDocumentPropertyService propertyService)
            : this(client, propertyService, null) { }

        /// <summary>Three-service constructor (no mapping provider).</summary>
        public TaskPaneViewModel(
            IInventreeClient?        client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService)
            : this(client, propertyService, viewportService, null, null) { }

        /// <summary>Full constructor used by the production add-in.</summary>
        public TaskPaneViewModel(
            IInventreeClient?         client,
            IDocumentPropertyService  propertyService,
            IViewportCaptureService?  viewportService,
            IPropertyMappingProvider? mappingProvider = null,
            IConfigProvider?          configProvider  = null)
        {
            _client          = client;
            _propertyService = propertyService;
            _viewportService = viewportService;
            _mappingProvider = mappingProvider;
            _configProvider  = configProvider;
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

        /// <summary>
        /// Opens the current InvenTree part in the default system browser.
        /// Does nothing when no part has been fetched.
        /// </summary>
        public void OpenPartInBrowser()
        {
            if (!PartLinkEnabled) return;

            var url = _client?.GetPartWebUrl(_session!.PartPk);
            if (url == null) return;

            OpenBrowserUrl(url);
        }

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

            var mapping    = GetMappingOrDefault();
            var partNo     = _propertyService.GetCustomProperty(mapping.IpnProperty);
            var pkRaw      = _propertyService.GetCustomProperty(mapping.PkProperty);
            bool pkPresent = int.TryParse(pkRaw, out int pkVal) && pkVal > 0;

            // A document switch can leave stale LINKED-by-PK state from the previous part.
            // Re-sync from the current document before deciding which fetch path to use
            // and whether the cached session still belongs here.
            _documentPkPresent = pkPresent;
            _documentPk        = pkPresent ? pkVal : 0;

            if (string.IsNullOrEmpty(partNo))
            {
                if (!pkPresent)
                {
                    // UNLINKED: no IPN and no PK — reset the panel.
                    ClearAll();
                    _isDocumentOpen    = true;
                    CreatePartEnabled = CanCreatePart();
                    if (_client == null)
                        SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                                  StatusSeverity.Warning);
                    NotifyBomVisibility();
                    return;
                }

                // LINKED-by-PK: blank IPN but a PK is stored.
                // If a matching session is already loaded, keep it POPULATED instead
                // of wiping it when SolidWorks fires LoadPartNumber right after a
                // poll-skipped Create Part.
                bool sessionMatches = _session != null && _session.Part.Pk == pkVal;
                if (!sessionMatches)
                {
                    ClearAll();
                }
                else
                {
                    RefreshCurrentProperties();
                    NotifySessionProperties();
                }

                _isDocumentOpen    = true;
                _documentPkPresent = true;
                _documentPk        = pkVal;
                PartNumber         = string.Empty;
                FetchEnabled       = _client != null;
                CreatePartEnabled  = false;

                if (_session != null)
                    PropertiesSectionVisible = true;

                if (_client == null)
                    SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                              StatusSeverity.Warning);
                else if (!sessionMatches)
                    SetStatus(string.Empty, StatusSeverity.None);

                NotifyBomVisibility();
                return;
            }

            // Drop the session if it no longer describes this document.
            if (_session != null &&
                (_session.Part.Ipn != partNo || _session.Part.Pk != _documentPk))
                ClearSession();

            _isDocumentOpen          = true;
            PartNumber               = partNo;
            PropertiesSectionVisible = true;
            RefreshCurrentProperties();

            // Restore FetchEnabled / CreatePartEnabled / status after ClearSession.
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

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            _isDocumentOpen          = false;
            _documentPkPresent       = false;
            _documentPk              = 0;
            PartNumber               = string.Empty;
            CurrentName              = string.Empty;
            CurrentNotes             = string.Empty;
            CurrentRevision          = string.Empty;
            CurrentDescription       = string.Empty;
            CurrentPk                = string.Empty;
            PropertiesSectionVisible = false;

            ClearSession();

            if (_client == null)
            {
                FetchEnabled      = false;
                CreatePartEnabled = false;
                SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                          StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled      = false;
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
            }

            NotifyBomVisibility();
        }

        /// <summary>
        /// Updates the client reference — called when settings change.
        /// Re-evaluates the panel against the current document so button states
        /// (especially FetchEnabled) stay correct after the server is configured.
        /// </summary>
        public void UpdateClient(IInventreeClient? newClient)
        {
            _client = newClient;
            ClearSession();
            LoadPartNumber();
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
            if (!_isDocumentOpen) return;
            if (_currentDocumentType != DocumentType.Part && _currentDocumentType != DocumentType.Assembly)
                return;

            var mapping = GetMappingOrDefault();
            var name    = _propertyService.GetCustomProperty(mapping.NameProperty);

            var vm = new CreatePartViewModel(_client, _propertyService, name, _mappingProvider,
                                             waitForServerAssignedIpn: WaitForAutoPartNumber,
                                             documentType: _currentDocumentType);

            vm.PartCreated += (_, part) =>
            {
                // A successful create always links the document by PK. Update the PK
                // cache before re-evaluating button states so LINKED-by-PK is respected
                // even when the server has not (yet) assigned an IPN.
                if (part.Pk > 0)
                {
                    _documentPkPresent = true;
                    _documentPk        = part.Pk;

                    var m = GetMappingOrDefault();
                    _propertyService.SetCustomProperty(m.PkProperty, part.Pk.ToString());
                }

                PartNumber        = part.Ipn ?? string.Empty;
                FetchEnabled      = _client != null && (_documentPkPresent || !string.IsNullOrEmpty(_partNumber));
                CreatePartEnabled = CanCreatePart();

                _session = new PartSyncSession(part, _client!, _propertyService, GetMappingOrDefault());
                PropertiesSectionVisible = true;
                RefreshCurrentProperties();
                NotifySessionProperties();

                SetStatus("Part created in InvenTree.", StatusSeverity.Success);
            };

            showDialog(vm);

            // Remember the choice for the next Create Part dialog in this SolidWorks session.
            WaitForAutoPartNumber = vm.WaitForServerAssignedIpn;

            if (_configProvider != null)
            {
                try
                {
                    var config = _configProvider.GetServerConfig();
                    if (config != null)
                    {
                        config.WaitForAutoPartNumber = vm.WaitForServerAssignedIpn;
                        _configProvider.SaveServerConfig(config);
                    }
                }
                catch
                {
                    // Non-fatal: the preference lives in memory for this session.
                }
            }
        }

        /// <summary>
        /// Fetches part data from InvenTree for the current IPN.
        /// </summary>
        public async Task FetchPartAsync()
        {
            try
            {
                RefreshCurrentProperties();
            }
            catch (InvalidOperationException ex) when (IsMappingFileError(ex))
            {
                RunOnUiThread(() => SetStatus(ex.Message, StatusSeverity.Error));
                return;
            }

            // ── LINKED-by-PK path ─────────────────────────────────────────────
            if (_documentPkPresent && _documentPk > 0)
            {
                SetStatus("Fetching from InvenTree\u2026", StatusSeverity.None);
                ClearSession();

                if (_client == null)
                {
                    SetStatus("No server configured \u2014 click \u2699 Settings to get started",
                              StatusSeverity.Warning);
                    return;
                }

                InventreePart? pkPart  = null;
                byte[]?        pkThumb = null;
                Exception?     pkError = null;

                try
                {
                    pkPart = await _client.GetPartByPkAsync(_documentPk).ConfigureAwait(false);

                    if (pkPart != null && !string.IsNullOrEmpty(pkPart.ThumbnailUrl))
                    {
                        try   { pkThumb = await _client.DownloadImageAsync(pkPart.ThumbnailUrl!).ConfigureAwait(false); }
                        catch { /* silent — placeholder will show */ }
                    }
                }
                catch (Exception ex) { pkError = ex; }

                RunOnUiThread(() =>
                {
                    if (pkError != null)
                    {
                        SetStatus($"Error: {pkError.Message}", StatusSeverity.Error);
                        return;
                    }

                    if (pkPart == null)
                    {
                        SetStatus($"No part found in InvenTree for PK: {_documentPk}", StatusSeverity.Warning);
                        return;
                    }

                    // Write IPN to SW document when the server has one and the document IPN is blank
                    // so the document is linked by IPN going forward without an explicit Apply.
                    var m      = GetMappingOrDefault();
                    var docIpn = _propertyService.GetCustomProperty(m.IpnProperty);
                    if (!string.IsNullOrEmpty(pkPart.Ipn) && string.IsNullOrEmpty(docIpn))
                    {
                        _propertyService.SetCustomProperty(m.IpnProperty, pkPart.Ipn);
                        PartNumber = pkPart.Ipn;
                    }

                    _session = new PartSyncSession(pkPart, _client!, _propertyService, m, pkThumb);
                    PropertiesSectionVisible = true;
                    RefreshCurrentProperties();
                    NotifySessionProperties();
                    SetStatus(string.Empty, StatusSeverity.None);
                });
                return;
            }

            // ── LINKED-by-IPN path (existing behaviour) ────────────────────────
            var ipn = PartNumber;
            if (string.IsNullOrEmpty(ipn))
            {
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
                return;
            }

            SetStatus("Fetching from InvenTree\u2026", StatusSeverity.None);
            ClearSession();

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

                _session = new PartSyncSession(resolvedPart, _client!, _propertyService, GetMappingOrDefault(), resolvedThumb);
                PropertiesSectionVisible = true;
                RefreshCurrentProperties();
                NotifySessionProperties();
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

        /// <summary>Writes only the Name field to the SolidWorks document and refreshes the preview from the written value.</summary>
        public void ApplyNameToDocument()
        {
            if (_session == null) return;
            var missing = FindMissingProperties(new[] { GetMappingOrDefault().NameProperty });
            if (missing.Count > 0 && !ConfirmMissingProperties(missing)) return;
            CurrentName = _session.ApplyName();
            SetStatus("Name applied.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Notes field to the SolidWorks document and refreshes the preview from the written value.</summary>
        public void ApplyNotesToDocument()
        {
            if (_session == null) return;
            var missing = FindMissingProperties(new[] { GetMappingOrDefault().NotesProperty });
            if (missing.Count > 0 && !ConfirmMissingProperties(missing)) return;
            CurrentNotes = _session.ApplyNotes();
            SetStatus("Notes applied.", StatusSeverity.Success);
        }

        /// <summary>Writes only the Description field to the SolidWorks document and refreshes the preview from the written value.</summary>
        public void ApplyDescriptionToDocument()
        {
            if (_session == null) return;
            var missing = FindMissingProperties(new[] { GetMappingOrDefault().DescriptionProperty });
            if (missing.Count > 0 && !ConfirmMissingProperties(missing)) return;
            CurrentDescription = _session.ApplyDescription();
            SetStatus("Description applied.", StatusSeverity.Success);
        }

        /// <summary>Writes the InvenTree PK property to the SolidWorks document and refreshes the preview from the written value.</summary>
        public void ApplyPkToDocument()
        {
            if (_session == null) return;
            var missing = FindMissingProperties(new[] { GetMappingOrDefault().PkProperty });
            if (missing.Count > 0 && !ConfirmMissingProperties(missing)) return;
            CurrentPk = _session.ApplyPk();
            SetStatus("InvenTree PK applied.", StatusSeverity.Success);
        }

        /// <summary>Pushes the current SolidWorks revision up to InvenTree.</summary>
        public async Task PushRevisionToInventreeAsync()
        {
            if (_session == null) return;
            if (_session.Part.Pk == 0)
            {
                SetStatus("Error: cannot push revision \u2014 InvenTree part ID is missing.",
                          StatusSeverity.Error);
                return;
            }
            SetStatus("Pushing revision to InvenTree\u2026", StatusSeverity.None);
            try
            {
                await _session.PushRevisionAsync().ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionPreview)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
                    SetStatus("Revision pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>Pushes the current SolidWorks name/description up to InvenTree.</summary>
        public async Task PushNameToInvenTreeAsync()
        {
            if (_session == null || _client == null) return;
            SetStatus("Pushing name to InvenTree\u2026", StatusSeverity.None);
            try
            {
                await _session.PushNameAsync().ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NamePreview)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
                    SetStatus("Name pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>Pushes the current SolidWorks notes up to InvenTree.</summary>
        public async Task PushNotesToInvenTreeAsync()
        {
            if (_session == null || _client == null) return;
            SetStatus("Pushing notes to InvenTree\u2026", StatusSeverity.None);
            try
            {
                await _session.PushNotesAsync().ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesPreview)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
                    SetStatus("Notes pushed to InvenTree.", StatusSeverity.Success);
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => SetStatus($"Error: {ex.Message}", StatusSeverity.Error));
            }
        }

        /// <summary>Pushes the current SolidWorks description up to InvenTree.</summary>
        public async Task PushDescriptionToInvenTreeAsync()
        {
            if (_session == null || _client == null) return;
            SetStatus("Pushing description to InvenTree\u2026", StatusSeverity.None);
            try
            {
                await _session.PushDescriptionAsync().ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionPreview)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
                    SetStatus("Description pushed to InvenTree.", StatusSeverity.Success);
                });
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
            if (_session == null || _client == null) return;

            var service = new PartThumbnailService(_client, _viewportService);
            try
            {
                var thumb = await service.PushAsync(
                    _session.Part.Pk,
                    (text, severity) => RunOnUiThread(() => SetStatus(text, severity)),
                    imageOverride).ConfigureAwait(false);

                RunOnUiThread(() =>
                {
                    if (thumb != null)
                    {
                        _session.SetThumbnail(thumb);
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailBytes)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailPlaceholderVisible)));
                        SetStatus("Image pushed to InvenTree.", StatusSeverity.Success);
                    }
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

        private void ClearSession()
        {
            _session = null;
            NotifySessionProperties();
        }

        private void NotifySessionProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NamePreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailBytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailPlaceholderVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InStockDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderingDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyNameEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyNotesEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyDescriptionEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyPkEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushNameEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushNotesEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushDescriptionEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushRevisionVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushImageVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PartLinkEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentInvenTreePk)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomButtonEnabled)));
            NotifyFlagDisplays();
        }

        private void NotifyFlagDisplays()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssemblyDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssemblyValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ComponentDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ComponentValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PurchaseableDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PurchaseableValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalableDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalableValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackableDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrackableValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestableDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TestableValue)));
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

        private static bool IsMappingFileError(InvalidOperationException ex) =>
            ex.Message.IndexOf("mapping file", StringComparison.OrdinalIgnoreCase) >= 0;

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
