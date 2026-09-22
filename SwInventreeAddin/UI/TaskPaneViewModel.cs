using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Presentation layer for the InvenTree task pane.
    /// Pure C# — no WinForms or WPF types so it is fully unit-testable
    /// without an STA thread or UI handle.
    /// </summary>
    /// <remarks>
    /// Part Sync session lifecycle and workflow live in
    /// <see cref="IPartSyncCoordinator"/> — this ViewModel owns only WPF
    /// property notifications, status wording, confirmation prompts, and
    /// command routing. Every bindable document/session projection reads the
    /// coordinator's immutable surface; nothing here stores document state.
    /// </remarks>
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

        private readonly IPartSyncCoordinator _coordinator;
        private readonly IHostStaDispatcher _dispatcher;
        private IInventreeClient? _client;
        private IPropertyMappingProvider? _mappingProvider;
        private readonly IConfigProvider? _configProvider;
        private ICreatePartValidationErrorService? _validationService;
        private MappingChangedSubscription? _mappingChangedSubscription;

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
        /// Receives (allCandidates, matchedCandidate) as immutable projections.
        /// Return true to load the matched part, false to cancel. Default always proceeds.
        /// </summary>
        public Func<IReadOnlyList<PartSnapshot>, PartSnapshot, bool> ConfirmDuplicateIpn { get; set; } = (_, __) => true;

        /// <summary>
        /// Called on the PK fetch path when the fetched part's IPN or Revision disagrees
        /// with the document's stamped values — a Link Mismatch. Receives
        /// (documentIpn, documentRevision, fetchedPart). Return true to load the
        /// PK-addressed part, false to leave the document LINKED with no session.
        /// Default always proceeds.
        /// </summary>
        public Func<string, string, PartSnapshot, bool> ConfirmLinkMismatch { get; set; } = (_, __, ___) => true;

        /// <summary>
        /// Called when the InvenTree thumbnail is clicked and a part URL is available.
        /// Defaults to opening the URL in the system's default browser.
        /// </summary>
        public Action<Uri?> OpenBrowserUrl { get; set; } = url =>
        {
            if (url == null) return;
            using var _ = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        };

        /// <summary>
        /// Host-provided viewport capture + crop: returns the image to push and
        /// the crop rectangle, or null when the user cancels or capture is
        /// unavailable. Wired by <see cref="TaskPaneControl"/>; the returned
        /// image is disposed by <see cref="PushImageAsync"/> after the push.
        /// </summary>
        public Func<(Image Image, Rectangle CropRect)?>? CaptureImageForPush { get; set; }

        // ── Bindable properties ───────────────────────────────────────────────

        private string _partNumber = string.Empty;
        private string _statusText = string.Empty;
        private string? _statusToolTip;
        private bool _fetchEnabled;
        private bool _createPartEnabled;
        private bool _propertiesSectionVisible;
        private StatusSeverity _statusSeverity = StatusSeverity.None;

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

        // ── Preview properties (computed from the coordinator's session projection) ──

        private PartSnapshot? Fetched => _coordinator.FetchedPart;

        /// <summary>Name fetched from InvenTree.</summary>
        public string NamePreview => Fetched?.Name ?? string.Empty;

        /// <summary>Notes fetched from InvenTree.</summary>
        public string NotesPreview => Fetched?.Notes ?? string.Empty;

        /// <summary>Revision fetched from InvenTree (or pushed).</summary>
        public string RevisionPreview => Fetched?.Revision ?? string.Empty;

        /// <summary>Description fetched from InvenTree.</summary>
        public string DescriptionPreview => Fetched?.Description ?? string.Empty;

        /// <summary>InvenTree PK as a display string.</summary>
        public string PkPreview => Fetched?.Pk > 0 ? Fetched!.Pk.ToString() : string.Empty;

        /// <summary>Raw PNG/JPEG bytes of the InvenTree part thumbnail. Null when none fetched.</summary>
        public byte[]? ThumbnailBytes => _coordinator.ThumbnailBytes;

        /// <summary>In-stock quantity display string (e.g. "15.5").</summary>
        public string InStockDisplay => Fetched?.InStock.ToString("G29") ?? string.Empty;

        /// <summary>On-order quantity display string (e.g. "100").</summary>
        public string OrderingDisplay => Fetched?.Ordering.ToString("G29") ?? string.Empty;

        // ── Flag chips (computed from the session projection) ─────────────────

        /// <summary>"Active: ✓" / "Active: ✗" display text for the Active flag chip.</summary>
        public string ActiveDisplay => FormatFlag("Active", Fetched?.Active);
        public bool? ActiveValue => Fetched?.Active;
        public string AssemblyDisplay => FormatFlag("Assembly", Fetched?.Assembly);
        public bool? AssemblyValue => Fetched?.Assembly;
        public string ComponentDisplay => FormatFlag("Component", Fetched?.Component);
        public bool? ComponentValue => Fetched?.Component;
        public string PurchaseableDisplay => FormatFlag("Purchaseable", Fetched?.Purchaseable);
        public bool? PurchaseableValue => Fetched?.Purchaseable;
        public string SalableDisplay => FormatFlag("Salable", Fetched?.Salable);
        public bool? SalableValue => Fetched?.Salable;
        public string TrackableDisplay => FormatFlag("Trackable", Fetched?.Trackable);
        public bool? TrackableValue => Fetched?.Trackable;
        public string TestableDisplay => FormatFlag("Testable", Fetched?.Testable);
        public bool? TestableValue => Fetched?.Testable;

        private static string FormatFlag(string name, bool? value) =>
            value == null ? string.Empty : $"{name}: {(value.Value ? "✓" : "✗")}";

        // ── Enabled / visible flags ───────────────────────────────────────────

        private bool HasSessionAndHealthyMapping =>
            Fetched != null && _mappingResult?.CanUseForPartSync == true;

        /// <summary>True when a part has been fetched and Apply is meaningful.</summary>
        public bool ApplyEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when individual Name apply is available.</summary>
        public bool ApplyNameEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when individual Notes apply is available.</summary>
        public bool ApplyNotesEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when individual Description apply is available.</summary>
        public bool ApplyDescriptionEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when a part has been fetched and applying PK to SW doc is meaningful.</summary>
        public bool ApplyPkEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when a part has been fetched and pushing Name to InvenTree is meaningful.</summary>
        public bool PushNameEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when a part has been fetched and pushing Notes to InvenTree is meaningful.</summary>
        public bool PushNotesEnabled => HasSessionAndHealthyMapping;

        /// <summary>True when a part has been fetched and pushing Description to InvenTree is meaningful.</summary>
        public bool PushDescriptionEnabled => HasSessionAndHealthyMapping;

        /// <summary>Controls Push Revision button visibility.</summary>
        public bool PushRevisionVisible => HasSessionAndHealthyMapping;

        /// <summary>Controls Push Image button visibility.</summary>
        public bool PushImageVisible => HasSessionAndHealthyMapping;

        /// <summary>True when the no-image placeholder icon should be shown.</summary>
        public bool ThumbnailPlaceholderVisible =>
            Fetched != null && (ThumbnailBytes == null || ThumbnailBytes.Length == 0);

        /// <summary>True when the InvenTree thumbnail is clickable and links to the part page.</summary>
        public bool PartLinkEnabled => (Fetched?.Pk ?? 0) > 0;

        /// <summary>Current SolidWorks document Name value — projected from the coordinator's document snapshot.</summary>
        public string CurrentName => _coordinator.Document?.Name ?? string.Empty;

        /// <summary>Current SolidWorks document Notes value — projected from the coordinator's document snapshot.</summary>
        public string CurrentNotes => _coordinator.Document?.Notes ?? string.Empty;

        /// <summary>Current SolidWorks document Revision value — projected from the coordinator's document snapshot.</summary>
        public string CurrentRevision => _coordinator.Document?.Revision ?? string.Empty;

        /// <summary>Current SolidWorks document Description Long value — projected from the coordinator's document snapshot.</summary>
        public string CurrentDescription => _coordinator.Document?.Description ?? string.Empty;

        /// <summary>Current SolidWorks InvenTree PK property value — projected from the coordinator's document snapshot.</summary>
        public string CurrentPk => _coordinator.Document?.PkText ?? string.Empty;

        /// <summary>Status bar message.</summary>
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        /// <summary>Status bar tooltip, typically the detail behind a mapping-health message.</summary>
        public string? StatusToolTip
        {
            get => _statusToolTip;
            private set => Set(ref _statusToolTip, value);
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

        private DocumentType? ActiveDocumentType => _coordinator.Document?.DocumentType;

        /// <summary>True when the active document carries a positive stamped InvenTree Part PK.</summary>
        private bool DocumentHasStampedPk => (_coordinator.Document?.StampedPartPk ?? 0) > 0;

        private bool IsPartOrAssemblyDocument =>
            ActiveDocumentType == DocumentType.Part || ActiveDocumentType == DocumentType.Assembly;

        private bool CanCreatePart() =>
            _client != null
            && _validationService != null
            && string.IsNullOrEmpty(_partNumber)
            && IsPartOrAssemblyDocument
            && !DocumentHasStampedPk
            && _mappingResult?.CanUseForPartSync == true;

        private bool ShouldEnableFetch() =>
            _client != null
            && IsPartOrAssemblyDocument
            && _mappingResult?.CanFetch == true
            && (DocumentHasStampedPk || !string.IsNullOrEmpty(_partNumber));

        /// <summary>True when an assembly is open and the linked-data sections are showing — the Compare BOM button stays disabled until a Part Sync session exists.</summary>
        public bool BomSectionVisible =>
            ActiveDocumentType == DocumentType.Assembly && _propertiesSectionVisible;

        /// <summary>True when BOM compare button should be enabled.</summary>
        public bool BomButtonEnabled =>
            ActiveDocumentType == DocumentType.Assembly
            && _client != null && Fetched != null
            && _mappingResult?.CanUseForPartSync == true;

        /// <summary>The InvenTree PK of the currently fetched part. Zero when none fetched.</summary>
        public int CurrentInvenTreePk => Fetched?.Pk ?? 0;

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomSectionVisible)));
            }
        }

        // ── Match indicators ──────────────────────────────────────────────────

        private bool BothNonBlank(string? a, string? b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b);

        /// <summary>True when both sides have a Name and they are identical.</summary>
        public bool NameMatch => PropertiesSectionVisible && BothNonBlank(CurrentName, NamePreview) && CurrentName == NamePreview;

        /// <summary>True when both sides have Notes and they are identical.</summary>
        public bool NotesMatch => PropertiesSectionVisible && BothNonBlank(CurrentNotes, NotesPreview) && CurrentNotes == NotesPreview;

        /// <summary>True when both sides have a Revision and they are identical.</summary>
        public bool RevisionMatch => PropertiesSectionVisible && BothNonBlank(CurrentRevision, RevisionPreview) && CurrentRevision == RevisionPreview;

        /// <summary>True when both sides have a Description Long and they are identical.</summary>
        public bool DescriptionMatch => PropertiesSectionVisible && BothNonBlank(CurrentDescription, DescriptionPreview) && CurrentDescription == DescriptionPreview;

        /// <summary>True when both sides have a PK and they are identical.</summary>
        public bool PkMatch => PropertiesSectionVisible && BothNonBlank(CurrentPk, PkPreview) && CurrentPk == PkPreview;

        // ── BOM compare state ─────────────────────────────────────────────────

        private IAssemblyBomService? _assemblyBomService;

        /// <summary>
        /// Part Sync keyword to match against BOM-column names. Read live from
        /// the server config; corrupt settings fall back to the default.
        /// </summary>
        public string BomKeyword
        {
            get
            {
                // Corrupt settings surface in the Settings window; Compare falls back to the default.
                try { return _configProvider?.GetServerConfig()?.BomKeyword ?? "inventree"; }
                catch { return "inventree"; }
            }
        }

        /// <summary>Wires the SolidWorks BOM table service used by BOM Compare.</summary>
        public void UpdateBomState(IAssemblyBomService bomService)
            => _assemblyBomService = bomService;

        /// <summary>
        /// Builds the BOM Compare pre-flight check over the coordinator's narrow
        /// context seam — the check reaches SolidWorks only through the
        /// coordinator's STA dispatcher. Null when no BOM service is wired.
        /// </summary>
        internal BomCompareReadinessCheck? CreateBomCompareReadinessCheck()
            => _assemblyBomService == null
                ? null
                : new BomCompareReadinessCheck(
                    new CoordinatorBomReadinessContext(_coordinator, _dispatcher),
                    _assemblyBomService,
                    BomKeyword);

        /// <summary>Builds the BOM Compare ViewModel; null when the client or BOM service is missing.</summary>
        internal BomCompareViewModel? CreateBomCompareViewModel(PropertyMappingConfig mapping, int assemblyPk)
            => (_client == null || _assemblyBomService == null)
                ? null
                : new BomCompareViewModel(_client, _assemblyBomService, mapping, assemblyPk, BomKeyword);

        /// <summary>Returns the matched BOM table's feature name; null when no BOM service is wired.</summary>
        internal string? GetBomTableName()
            => _assemblyBomService?.GetBomTableName(BomKeyword);

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the task pane ViewModel over an existing coordinator. The
        /// coordinator owns the document model and all Part Sync workflow; the
        /// ViewModel only projects it into bindable properties.
        /// </summary>
        /// <param name="coordinator">Part Sync coordinator owning state and workflow.</param>
        /// <param name="dispatcher">STA dispatcher for marshalling UI-thread work.</param>
        /// <param name="client">Active InvenTree client, or null when no server is configured.</param>
        /// <param name="mappingProvider">Current property mapping provider.</param>
        /// <param name="configProvider">Config persistence (BOM keyword), or null.</param>
        /// <param name="createPartValidator">Create Part validation service, or null to hide the button.</param>
        public TaskPaneViewModel(
            IPartSyncCoordinator coordinator,
            IHostStaDispatcher dispatcher,
            IInventreeClient? client,
            IPropertyMappingProvider? mappingProvider = null,
            IConfigProvider? configProvider = null,
            ICreatePartValidationErrorService? createPartValidator = null)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _client = client;
            _mappingProvider = mappingProvider;
            _configProvider = configProvider;
            _validationService = createPartValidator;

            _coordinator.Changed += OnCoordinatorChanged;

            LoadPartNumber();
            AttachMappingProvider();
        }

        // ── Document lifecycle → presentation ─────────────────────────────────

        /// <summary>
        /// Runs a full document evaluation on the coordinator, then projects
        /// the result into bindable state. Equivalent to the legacy
        /// "load document → set presentation" step; called on document
        /// activation, mapping changes, and construction.
        /// </summary>
        public void LoadPartNumber()
        {
            RefreshMappingResult();
            _coordinator.UpdateDocument();

            if (_coordinator.Kind == TaskPaneStateKind.Empty)
            {
                ResetDocumentPanel();
                NotifyBomVisibility();
                RefreshStatus();
                return;
            }

            var document = _coordinator.Document!;

            if (_coordinator.Kind == TaskPaneStateKind.Unsupported)
            {
                ResetDocumentPanel();
                SetStatus("Drawings are not supported — open a part or assembly.", StatusSeverity.Warning);
                RefreshStatus();
                return;
            }

            if (_coordinator.Kind == TaskPaneStateKind.Unlinked)
            {
                ResetDocumentPanel();
                CreatePartEnabled = CanCreatePart();
                if (_client == null)
                    SetStatus("No server configured — click ⚙ Settings to get started", StatusSeverity.Warning);
                NotifyBomVisibility();
                RefreshStatus();
                return;
            }

            // LINKED or POPULATED: a session that survived document revalidation
            // is projected by the coordinator Changed event; presentation only
            // needs enable/disable state and status wording.
            var sessionKept = _coordinator.FetchedPart != null;

            if (string.IsNullOrEmpty(document.Ipn))
            {
                // LINKED-by-PK — no IPN property to show.
                PartNumber = string.Empty;
                FetchEnabled = ShouldEnableFetch();
                CreatePartEnabled = false;
                PropertiesSectionVisible = true;
                if (_client == null)
                    SetStatus("No server configured — click ⚙ Settings to get started", StatusSeverity.Warning);
                else if (!sessionKept)
                    SetStatus(string.Empty, StatusSeverity.None);
                NotifyBomVisibility();
                RefreshStatus();
                return;
            }

            // LINKED-by-IPN (or POPULATED on an IPN-bearing document).
            PartNumber = document.Ipn;
            PropertiesSectionVisible = true;
            if (_client == null)
            {
                FetchEnabled = false;
                CreatePartEnabled = false;
                SetStatus("No server configured — click ⚙ Settings to get started", StatusSeverity.Warning);
            }
            else
            {
                FetchEnabled = ShouldEnableFetch();
                CreatePartEnabled = CanCreatePart();
                SetStatus(string.Empty, StatusSeverity.None);
            }
            NotifyBomVisibility();
            RefreshStatus();
        }

        /// <summary>
        /// Handles a SolidWorks document-property-changed notification: the
        /// coordinator consumes write echoes, re-captures, and classifies; the
        /// ViewModel maps the classification onto presentation state.
        /// Entered on the STA/UI thread.
        /// </summary>
        public void OnDocumentPropertyChanged(string name, string newValue)
        {
            RefreshMappingResult();
            var change = _coordinator.NotifyDocumentPropertyChanged(name, newValue);
            switch (change)
            {
                case PartSyncPropertyChange.EchoConsumed:
                case PartSyncPropertyChange.Ignored:
                    return;
                case PartSyncPropertyChange.Reevaluated:
                    LoadPartNumber();
                    return;
                case PartSyncPropertyChange.RefreshedDivergent:
                    SetStatus(string.Empty, StatusSeverity.None);
                    return;
                case PartSyncPropertyChange.Refreshed:
                    return;
            }
        }

        /// <summary>Clears all state when the active document is closed.</summary>
        public void ClearAll()
        {
            _coordinator.NotifyDocumentClosed();
            ResetDocumentPanel();
            NotifyBomVisibility();
        }

        /// <summary>Updates the InvenTree client (called when server config changes).</summary>
        public void UpdateClient(IInventreeClient? client)
        {
            _client = client;
            _coordinator.UpdateClient(client);
            LoadPartNumber();
        }

        /// <summary>Updates the create-part validation service.</summary>
        public void UpdateCreatePartValidationService(ICreatePartValidationErrorService? service)
        {
            _validationService = service;
        }

        /// <summary>
        /// Updates the property-mapping provider and refreshes the document model.
        /// Called when the settings window reports a mapping change.
        /// </summary>
        /// <param name="mappingProvider">The new provider, or null to clear.</param>
        public void UpdateMapping(IPropertyMappingProvider? mappingProvider)
        {
            DetachMappingProvider();
            _mappingProvider = mappingProvider;
            _coordinator.UpdateMapping(mappingProvider);
            if (!TryLoadPartNumberWhenNoSession())
                RefreshPreservingSession();
            AttachMappingProvider();
        }

        private bool TryLoadPartNumberWhenNoSession()
        {
            if (_coordinator.FetchedPart != null)
                return false;
            LoadPartNumber();
            return true;
        }

        /// <summary>
        /// Mapping changed while a session is installed: re-evaluate the
        /// document snapshot under the new mapping and refresh command state
        /// without tearing down presentation.
        /// </summary>
        private void RefreshPreservingSession()
        {
            RefreshMappingResult();
            if (_propertiesSectionVisible)
                _coordinator.RefreshDocument();
            RefreshStatus();
            RefreshCommandStates();
        }

        // ── Create Part ───────────────────────────────────────────────────────

        /// <summary>
        /// Whether the Create Part dialog blocks until InvenTree assigns a
        /// server-generated IPN. Persisted via the config provider.
        /// </summary>
        public bool WaitForServerAssignedIpn { get; set; }

        /// <summary>
        /// Creates the Create Part dialog ViewModel and hands it to the
        /// caller-supplied show callback. The coordinator mints the session-family
        /// token up front; when the dialog produces a part, the coordinator
        /// performs the stale-validated commit (document writes + session
        /// install) so a document switch mid-dialog can never write to the
        /// wrong document.
        /// </summary>
        /// <param name="showDialog">Host callback that shows the dialog modally.</param>
        public void OpenCreatePartWindow(Action<CreatePartViewModel> showDialog)
        {
            if (_client == null || !IsPartOrAssemblyDocument || !CanCreatePart() || _validationService == null)
                return;

            var token = _coordinator.BeginCreatePart();
            var documentName = _coordinator.Document?.Name ?? string.Empty;

            var vm = new CreatePartViewModel(
                _client,
                _validationService,
                documentName,
                _mappingProvider,
                waitForServerAssignedIpn: WaitForServerAssignedIpn,
                documentType: _coordinator.Document!.DocumentType);

            vm.PartCreated += (_, part) =>
            {
                var result = _coordinator.CompleteCreatePart(token, part);
                if (result.Outcome != PartSyncOutcome.Success)
                    return;   // Stale or invalid — the coordinator already dropped the pending operation.

                PartNumber = result.Ipn ?? part.Ipn ?? string.Empty;
                FetchEnabled = ShouldEnableFetch();
                CreatePartEnabled = CanCreatePart();
                PropertiesSectionVisible = true;

                var ipnNotice = vm.IpnMismatchNotice;
                SetStatus(
                    ipnNotice ?? "Part created in InvenTree.",
                    ipnNotice != null ? StatusSeverity.Warning : StatusSeverity.Success);
            };

            showDialog(vm);

            // Remember the choice for the next Create Part dialog in this SolidWorks session.
            WaitForServerAssignedIpn = vm.WaitForServerAssignedIpn;

            if (_configProvider != null)
            {
                try
                {
                    var config = _configProvider.GetServerConfig();
                    if (config != null)
                    {
                        config.WaitForServerAssignedIpn = vm.WaitForServerAssignedIpn;
                        _configProvider.SaveServerConfig(config);
                    }
                }
                catch
                {
                    // Non-fatal: the preference lives in memory for this session.
                }
            }
        }

        // ── Fetch ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches part data from InvenTree. The coordinator decides between the
        /// stamped-PK path and the IPN path, clears any installed session before
        /// the first await, and guards the completion against staleness; this
        /// method only maps the typed outcome onto status text and prompts.
        /// </summary>
        public async Task FetchPartAsync()
        {
            RefreshMappingResult();
            if (_mappingResult?.CanFetch != true)
                return;

            _coordinator.RefreshDocument();
            var ipn = PartNumber;

            if (!DocumentHasStampedPk && string.IsNullOrEmpty(ipn))
            {
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
                return;
            }

            SetStatus("Fetching from InvenTree…", StatusSeverity.None);

            if (_client == null)
            {
                SetStatus("No server configured — click ⚙ Settings to get started", StatusSeverity.Warning);
                return;
            }

            var result = await _coordinator.FetchAsync(ipn).ConfigureAwait(false);
            await ApplyFetchOutcomeAsync(result).ConfigureAwait(false);
        }

        /// <summary>
        /// Maps a fetch outcome to presentation. Confirmation outcomes prompt on
        /// the UI thread, then resume through the coordinator and re-enter the
        /// loop so a resume outcome flows through the same terminal mapping.
        /// </summary>
        private async Task ApplyFetchOutcomeAsync(PartSyncResult result)
        {
            while (true)
            {
                var shouldResume = false;
                var approved = false;
                var linkMismatchDeclined = false;

                RunOnUiThread(() =>
                {
                    switch (result.Outcome)
                    {
                        case PartSyncOutcome.DuplicateIpnConfirmation:
                            shouldResume = result.Confirmation != null;
                            approved = result.MatchedCandidate != null
                                && ConfirmDuplicateIpn(
                                    result.Candidates ?? Array.Empty<PartSnapshot>(),
                                    result.MatchedCandidate);
                            break;
                        case PartSyncOutcome.LinkMismatchConfirmation:
                            shouldResume = result.Confirmation != null;
                            approved = result.FetchedPart != null
                                && ConfirmLinkMismatch(
                                    result.DocumentIpn ?? string.Empty,
                                    result.DocumentRevision ?? string.Empty,
                                    result.FetchedPart);
                            linkMismatchDeclined = shouldResume && !approved;
                            if (linkMismatchDeclined)
                                SetStatus("Fetch cancelled — Link Mismatch.", StatusSeverity.Warning);
                            break;
                        default:
                            MapTerminalOutcome(result);
                            break;
                    }
                });

                if (!shouldResume || result.Confirmation == null)
                    return;

                result = await _coordinator
                    .ResumeConfirmationAsync(result.Confirmation, approved)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Maps a terminal (non-confirmation) outcome to status text. Stale and
        /// Cancelled intentionally do nothing — a stale completion must never
        /// touch a newer operation's status, and cancellation leaves whatever
        /// status the decline path already set.
        /// </summary>
        private void MapTerminalOutcome(PartSyncResult result)
        {
            switch (result.Outcome)
            {
                case PartSyncOutcome.Success:
                    if (!string.IsNullOrEmpty(result.Ipn))
                        PartNumber = result.Ipn!;
                    PropertiesSectionVisible = true;
                    SetStatus(string.Empty, StatusSeverity.None);
                    break;
                case PartSyncOutcome.PartNotFound:
                    SetStatus(
                        result.PartPk > 0
                            ? $"No part found in InvenTree for PK: {result.PartPk}"
                            : $"No part found in InvenTree for: {result.Ipn}",
                        StatusSeverity.Warning);
                    break;
                case PartSyncOutcome.DuplicateNoRevisionMatch:
                    SetStatus(
                        $"{result.Candidates?.Count ?? 0} parts share IPN ‘{result.Ipn}’ but none match " +
                        $"SW revision {RevisionLabel(result.SwRevision)}. Resolve in InvenTree.",
                        StatusSeverity.Error);
                    break;
                case PartSyncOutcome.DuplicateAmbiguous:
                    SetStatus(
                        $"{result.Candidates?.Count ?? 0} parts share IPN ‘{result.Ipn}’ and revision " +
                        $"{RevisionLabel(result.SwRevision)}. Resolve duplicates in InvenTree.",
                        StatusSeverity.Error);
                    break;
                case PartSyncOutcome.SucceededWithWarning:
                    SetStatus(result.Diagnostic ?? string.Empty, StatusSeverity.Warning);
                    break;
                case PartSyncOutcome.InvalidOperation:
                    if (!string.IsNullOrEmpty(result.Diagnostic))
                        SetStatus(result.Diagnostic!, StatusSeverity.Warning);
                    break;
                case PartSyncOutcome.Failed:
                    SetStatus($"Error: {result.Diagnostic}", StatusSeverity.Error);
                    break;
                case PartSyncOutcome.Stale:
                case PartSyncOutcome.Cancelled:
                case PartSyncOutcome.DuplicateIpnConfirmation:
                case PartSyncOutcome.LinkMismatchConfirmation:
                case PartSyncOutcome.MissingPropertyConfirmation:
                    break;
            }
        }

        private static string RevisionLabel(string? revision) =>
            string.IsNullOrEmpty(revision) ? "(blank)" : revision;

        /// <summary>
        /// Runs the confirmation prompt for a fetch/BOM outcome and resumes the
        /// pending coordinator operation. Used by the BOM readiness path where
        /// the typed outcome travels through <see cref="BomReadinessResult"/>.
        /// </summary>
        internal async Task<PartSyncResult> ResumeFetchConfirmationAsync(PartSyncResult fetchResult)
        {
            switch (fetchResult.Outcome)
            {
                case PartSyncOutcome.DuplicateIpnConfirmation when fetchResult.Confirmation != null:
                    {
                        var approved = fetchResult.MatchedCandidate != null
                            && ConfirmDuplicateIpn(
                                fetchResult.Candidates ?? Array.Empty<PartSnapshot>(),
                                fetchResult.MatchedCandidate);
                        return await _coordinator
                            .ResumeConfirmationAsync(fetchResult.Confirmation, approved)
                            .ConfigureAwait(false);
                    }
                case PartSyncOutcome.LinkMismatchConfirmation when fetchResult.Confirmation != null:
                    {
                        var approved = fetchResult.FetchedPart != null
                            && ConfirmLinkMismatch(
                                fetchResult.DocumentIpn ?? string.Empty,
                                fetchResult.DocumentRevision ?? string.Empty,
                                fetchResult.FetchedPart);
                        return await _coordinator
                            .ResumeConfirmationAsync(fetchResult.Confirmation, approved)
                            .ConfigureAwait(false);
                    }
                case PartSyncOutcome.MissingPropertyConfirmation when fetchResult.Confirmation != null:
                    {
                        var approved = ConfirmMissingProperties(
                            fetchResult.MissingProperties ?? Array.Empty<string>());
                        return await _coordinator
                            .ResumeConfirmationAsync(fetchResult.Confirmation, approved)
                            .ConfigureAwait(false);
                    }
                default:
                    return fetchResult;
            }
        }

        // ── Apply (InvenTree → SolidWorks) ────────────────────────────────────

        /// <summary>Applies the fetched Name to the SolidWorks document.</summary>
        public Task ApplyNameToDocument() => ApplyFieldAsync(ApplyField.Name, "Name applied.");

        /// <summary>Applies the fetched Notes to the SolidWorks document.</summary>
        public Task ApplyNotesToDocument() => ApplyFieldAsync(ApplyField.Notes, "Notes applied.");

        /// <summary>Applies the fetched Description to the SolidWorks document.</summary>
        public Task ApplyDescriptionToDocument() => ApplyFieldAsync(ApplyField.Description, "Description applied.");

        /// <summary>Applies the fetched PK to the SolidWorks document.</summary>
        public Task ApplyPkToDocument() => ApplyFieldAsync(ApplyField.Pk, "PK applied.");

        /// <summary>
        /// Applies one field. The coordinator registers write echoes before
        /// every document write; a missing mapped property produces a
        /// confirmation outcome that prompts here and resumes through the
        /// coordinator.
        /// </summary>
        private async Task ApplyFieldAsync(ApplyField field, string successText)
        {
            if (Fetched == null || _mappingResult?.CanUseForPartSync != true)
                return;

            var result = _coordinator.Apply(field);

            if (result.Outcome == PartSyncOutcome.MissingPropertyConfirmation && result.Confirmation != null)
            {
                var approved = ConfirmMissingProperties(
                    result.MissingProperties ?? Array.Empty<string>());
                result = await _coordinator
                    .ResumeConfirmationAsync(result.Confirmation, approved)
                    .ConfigureAwait(false);
                if (!approved)
                    return;
            }

            RunOnUiThread(() =>
            {
                switch (result.Outcome)
                {
                    case PartSyncOutcome.Success:
                        SetStatus(successText, StatusSeverity.Success);
                        break;
                    case PartSyncOutcome.Failed:
                        SetStatus($"Error: {result.Diagnostic}", StatusSeverity.Error);
                        break;
                    case PartSyncOutcome.InvalidOperation:
                        if (!string.IsNullOrEmpty(result.Diagnostic))
                            SetStatus($"Error: {result.Diagnostic}", StatusSeverity.Error);
                        break;
                }
            });
        }

        // ── Push (SolidWorks → InvenTree) ─────────────────────────────────────

        /// <summary>Pushes the SolidWorks document Revision to the InvenTree part.</summary>
        public Task PushRevisionToInventreeAsync() =>
            PushFieldAsync(PushField.Revision, "Pushing revision to InvenTree…", "Revision pushed to InvenTree.");

        /// <summary>Pushes the SolidWorks document Name to the InvenTree part.</summary>
        public Task PushNameToInvenTreeAsync() =>
            PushFieldAsync(PushField.Name, "Pushing name to InvenTree…", "Name pushed to InvenTree.");

        /// <summary>Pushes the SolidWorks document Notes to the InvenTree part.</summary>
        public Task PushNotesToInvenTreeAsync() =>
            PushFieldAsync(PushField.Notes, "Pushing notes to InvenTree…", "Notes pushed to InvenTree.");

        /// <summary>Pushes the SolidWorks document Description to the InvenTree part.</summary>
        public Task PushDescriptionToInvenTreeAsync() =>
            PushFieldAsync(PushField.Description, "Pushing description to InvenTree…", "Description pushed to InvenTree.");

        /// <summary>
        /// Pushes one field. The coordinator captures the mapped document value
        /// on the STA thread, updates off-thread, and commits the session
        /// mutation through a validated dispatcher callback.
        /// </summary>
        private async Task PushFieldAsync(PushField field, string busyText, string successText)
        {
            if (!HasSessionAndHealthyMapping || _client == null)
                return;

            if (field == PushField.Revision && (Fetched?.Pk ?? 0) == 0)
            {
                SetStatus("Error: cannot push revision — InvenTree part ID is missing.", StatusSeverity.Error);
                return;
            }

            SetStatus(busyText, StatusSeverity.None);

            var result = await _coordinator.PushAsync(field).ConfigureAwait(false);

            RunOnUiThread(() =>
            {
                switch (result.Outcome)
                {
                    case PartSyncOutcome.Success:
                        SetStatus(successText, StatusSeverity.Success);
                        break;
                    case PartSyncOutcome.Failed:
                    case PartSyncOutcome.InvalidOperation:
                        if (!string.IsNullOrEmpty(result.Diagnostic))
                            SetStatus($"Error: {result.Diagnostic}", StatusSeverity.Error);
                        break;
                }
            });
        }

        /// <summary>
        /// Pushes a SolidWorks viewport screenshot to the InvenTree part's
        /// image field. When <paramref name="imageOverride"/> is null the host's
        /// <see cref="CaptureImageForPush"/> provides the capture (including the
        /// crop rectangle); the coordinator then uploads off-thread, refreshes
        /// the thumbnail, and commits it through a validated callback.
        /// </summary>
        /// <param name="imageOverride">
        /// Direct image override (tests). When supplied the image is used as-is,
        /// uncropped, and is NOT disposed by this method.
        /// </param>
        public async Task PushImageAsync(Image? imageOverride = null)
        {
            if (Fetched == null || _client == null || _mappingResult?.CanUseForPartSync != true)
                return;

            Image? image = imageOverride;
            var cropRect = Rectangle.Empty;
            var ownImage = false;

            if (image == null)
            {
                var captured = CaptureImageForPush?.Invoke();
                if (captured == null)
                    return;
                image = captured.Value.Image;
                cropRect = captured.Value.CropRect;
                ownImage = true;
            }

            try
            {
                SetStatus("Pushing image to InvenTree…", StatusSeverity.None);

                var result = await _coordinator.PushImageAsync(image, cropRect).ConfigureAwait(false);

                RunOnUiThread(() =>
                {
                    switch (result.Outcome)
                    {
                        case PartSyncOutcome.Success:
                            SetStatus("Image pushed to InvenTree.", StatusSeverity.Success);
                            break;
                        case PartSyncOutcome.SucceededWithWarning:
                            SetStatus(result.Diagnostic ?? "Image pushed.", StatusSeverity.Warning);
                            break;
                        case PartSyncOutcome.Failed:
                        case PartSyncOutcome.InvalidOperation:
                            if (!string.IsNullOrEmpty(result.Diagnostic))
                                SetStatus($"Error: {result.Diagnostic}", StatusSeverity.Error);
                            break;
                    }
                });
            }
            finally
            {
                if (ownImage)
                    image!.Dispose();
            }
        }

        // ── Property-change notifications ─────────────────────────────────────

        /// <summary>
        /// Re-raises PropertyChanged for all document-value projections.
        /// Called when the coordinator reports a document change (refresh,
        /// property change, session install/drop).
        /// </summary>
        private void NotifyDocumentProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentNotes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDescription)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPk)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyPkEnabled)));
        }

        /// <summary>
        /// Re-raises PropertyChanged for all session-projection properties.
        /// Called whenever the coordinator's <see cref="IPartSyncCoordinator.Changed"/>
        /// event fires — session install, drop, apply, push, thumbnail update.
        /// </summary>
        private void NotifySessionProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NamePreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailBytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InStockDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderingDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NotesMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RevisionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PkMatch)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailPlaceholderVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PartLinkEnabled)));
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

        private void NotifyBomVisibility()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomSectionVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BomButtonEnabled)));
        }

        private void OnCoordinatorChanged(object? sender, EventArgs e) =>
            RunOnUiThread(() =>
            {
                NotifyDocumentProperties();
                NotifySessionProperties();
            });

        private void ResetDocumentPanel()
        {
            PartNumber = string.Empty;
            FetchEnabled = false;
            CreatePartEnabled = false;
            PropertiesSectionVisible = false;

            if (_client == null)
                SetStatus("No server configured — click ⚙ Settings to get started", StatusSeverity.Warning);
            else
                SetStatus("Open a part or assembly in SolidWorks to get started.", StatusSeverity.None);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Raises <see cref="SettingsRequested"/>.</summary>
        public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Raises <see cref="CompareBomRequested"/>.</summary>
        public void RequestCompareBom() => CompareBomRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Opens the current part's InvenTree page in the default browser.</summary>
        public void OpenPartInBrowser()
        {
            var url = _coordinator.GetPartWebUrl();
            if (url == null) return;
            OpenBrowserUrl(url);
        }

        /// <summary>
        /// Re-checks the current document properties and notifies listeners.
        /// Kept for compatibility; coordinator projections are live so this is a no-op notify.
        /// </summary>
        public void RefreshCurrentProperties()
        {
            _coordinator.RefreshDocument();
            NotifyDocumentProperties();
        }

        // ── Mapping / status / threading internals ────────────────────────────

        private MappingResult? _mappingResult;
        private bool _mappingHealthWarningActive;

        private MappingResult GetMappingResultOrDefault() =>
            _mappingProvider?.GetMappingResult()
            ?? new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

        private void RefreshMappingResult() =>
            _mappingResult = GetMappingResultOrDefault();

        /// <summary>
        /// Recomputes all command enable/disable flags from the current document
        /// state and mapping health, then raises the PropertyChanged events that
        /// keep the Task Pane buttons in sync.
        /// </summary>
        private void RefreshCommandStates()
        {
            FetchEnabled = ShouldEnableFetch();
            CreatePartEnabled = CanCreatePart();

            NotifySessionProperties();
            NotifyBomVisibility();
        }

        // Mapping-health warnings take precedence over document/client status messages,
        // so any state change that could hide a schema mismatch must re-evaluate here.
        private void RefreshStatus()
        {
            if (_mappingProvider == null) return;

            RefreshMappingResult();
            var status = _mappingResult!.FullStatusMessage;
            switch (_mappingResult!.Health)
            {
                case MappingHealth.Invalid:
                    _mappingHealthWarningActive = true;
                    SetStatus(status, StatusSeverity.Error, status);
                    break;
                case MappingHealth.NeedsUpgrade:
                    _mappingHealthWarningActive = true;
                    SetStatus(status, StatusSeverity.Warning, status);
                    break;
                case MappingHealth.NewerSchema:
                    _mappingHealthWarningActive = true;
                    SetStatus(status, StatusSeverity.Warning, status);
                    break;
                default:
                    if (_mappingHealthWarningActive)
                    {
                        _mappingHealthWarningActive = false;
                        SetStatus(string.Empty, StatusSeverity.None);
                    }
                    break;
            }
        }

        private void SetStatus(string text, StatusSeverity severity, string? toolTip = null)
        {
            StatusText = text;
            StatusToolTip = toolTip;
            StatusSeverity = severity;
        }

        private void AttachMappingProvider() =>
            MappingChangedSubscription.SubscribeTo(ref _mappingChangedSubscription, _mappingProvider, OnMappingChanged);

        private void DetachMappingProvider() =>
            MappingChangedSubscription.UnsubscribeFrom(ref _mappingChangedSubscription);

        /// <summary>
        /// The mapping file changed under the same provider: the coordinator
        /// rebinds (revision advance + session rebuild against the new mapping),
        /// then the ViewModel re-runs the full evaluation when no session is
        /// installed, or the lighter session-preserving refresh otherwise.
        /// </summary>
        private void OnMappingChanged()
        {
            RunOnUiThread(() =>
            {
                _coordinator.UpdateMapping(_mappingProvider);
                if (TryLoadPartNumberWhenNoSession())
                    return;
                RefreshPreservingSession();
            });
        }

        /// <summary>Marshals an action onto the STA/UI thread via the dispatcher.</summary>
        private void RunOnUiThread(Action action) => _dispatcher.Run(action);
    }

    /// <summary>Visual severity of the status-bar message.</summary>
    public enum StatusSeverity
    {
        /// <summary>No status / neutral.</summary>
        None,
        /// <summary>Successful operation.</summary>
        Success,
        /// <summary>Non-fatal warning.</summary>
        Warning,
        /// <summary>Operation failed.</summary>
        Error,
    }
}
