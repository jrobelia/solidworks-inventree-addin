using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Thin WinForms wrapper that gives SolidWorks a native HWND while hosting
    /// the real UI in a WPF UserControl via <see cref="ElementHost"/>.
    /// Composition root and host adapter: builds the
    /// <see cref="PartSyncCoordinator"/> and <see cref="TaskPaneViewModel"/>,
    /// routes SolidWorks host callbacks into the coordinator's lifecycle
    /// methods, and hands the results to the ViewModel's projection entries.
    /// </summary>
    public class TaskPaneControl : UserControl
    {
        private readonly TaskPaneViewModel _vm;
        private readonly PartSyncCoordinator _coordinator;
        private readonly IHostStaDispatcher _dispatcher;
        private IInventreeClient? _client;
        private readonly ICreatePartValidationErrorService _createPartValidator;
        private IPropertyMappingProvider? _mappingProvider;
        private MappingChangedSubscription? _mappingChangedSubscription;

        public event EventHandler? SettingsRequested;

        // -- Constructors ------------------------------------------------------

        public TaskPaneControl(
            IInventreeClient? client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService,
            IPropertyMappingProvider? mappingProvider,
            IConfigProvider? configProvider,
            ICreatePartValidationErrorService createPartValidator)
        {
            _client = client;
            _createPartValidator = createPartValidator;
            _mappingProvider = mappingProvider;

            // The dispatcher captures the host STA SynchronizationContext at
            // construction — the coordinator marshals every async commit and
            // document mutation through it.
            _dispatcher = new SynchronizationContextStaDispatcher();
            _coordinator = new PartSyncCoordinator(propertyService, _dispatcher, client, mappingProvider);
            _vm = new TaskPaneViewModel(
                _coordinator,
                new CoordinatorBomReadinessContext(_coordinator, _dispatcher),
                _dispatcher,
                client,
                mappingProvider,
                configProvider,
                _createPartValidator);
            _vm.SettingsRequested += (s, e) => SettingsRequested?.Invoke(this, e);
            _vm.CompareBomRequested += OnCompareBomRequested;

            // Host-driven initial evaluation: capture the active document on
            // the coordinator, then project the pane.
            _coordinator.UpdateDocument();
            _vm.ProjectDocumentUpdate();
            AttachMappingProvider();

            // Host seam: viewport capture + crop dialog stay UI concerns; the
            // coordinator only receives the processed image and rectangle.
            if (viewportService != null)
            {
                _vm.CaptureImageForPush = () =>
                {
                    var image = viewportService.CaptureViewportImage();
                    if (image == null)
                        return null;

                    var cropWindow = new ImageCropWindow(image);
                    if (cropWindow.ShowDialog() != true)
                    {
                        image.Dispose();
                        return ((System.Drawing.Image, System.Drawing.Rectangle)?)null;
                    }

                    return (image, cropWindow.CropRectangle);
                };
            }
            _vm.ConfirmMissingProperties = missing =>
            {
                var bullet = string.Join(System.Environment.NewLine + "  \u2022 ", missing);
                var result = MessageDialog.ShowOKCancel(
                    SolidWorksWindowHandle.Get(),
                    "The following mapped property names don\u2019t exist in this document:"
                    + System.Environment.NewLine + "  \u2022 " + bullet
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "The property will be created. Write anyway?",
                    "Property Not Found",
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return result == MessageDialogResult.Ok;
            };

            _vm.ConfirmDuplicateIpn = (allParts, matched) =>
            {
                var nl = System.Environment.NewLine;
                var lines = string.Join(nl, System.Linq.Enumerable.Select(allParts, p =>
                {
                    var rev = string.IsNullOrEmpty(p.Revision) ? "(no revision)" : p.Revision;
                    var tag = p.Pk == matched.Pk ? "  \u2190 matches this file" : "";
                    return $"  PK {p.Pk,6}   Rev {rev}{tag}";
                }));
                var matchRev = string.IsNullOrEmpty(matched.Revision) ? "(no revision)" : matched.Revision;
                var answer = MessageDialog.ShowOKCancel(
                    SolidWorksWindowHandle.Get(),
                    $"IPN \u201c{matched.Ipn}\u201d has {allParts.Count} parts in InvenTree:{nl}{nl}"
                    + lines + nl + nl
                    + $"Loading PK {matched.Pk} (Rev {matchRev}). Proceed?",
                    "Duplicate IPN \u2014 Revision Matched",
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return answer == MessageDialogResult.Ok;
            };

            _vm.ConfirmLinkMismatch = (docIpn, docRev, part) =>
            {
                var nl = System.Environment.NewLine;
                var docIpnLabel = string.IsNullOrEmpty(docIpn) ? "(blank)" : docIpn;
                var docRevLabel = string.IsNullOrEmpty(docRev) ? "(blank)" : docRev;
                var partIpnLabel = string.IsNullOrEmpty(part.Ipn) ? "(blank)" : part.Ipn;
                var partRevLabel = string.IsNullOrEmpty(part.Revision) ? "(blank)" : part.Revision;
                var answer = MessageDialog.ShowOKCancel(
                    SolidWorksWindowHandle.Get(),
                    $"The InvenTree Part PK stamped on this document resolves to a part that disagrees with it:{nl}{nl}"
                    + $"  This document      IPN {docIpnLabel}   Rev {docRevLabel}{nl}"
                    + $"  InvenTree PK {part.Pk}   IPN {partIpnLabel}   Rev {partRevLabel}{nl}{nl}"
                    + "Load the PK-addressed part anyway?" + nl + nl
                    + "To fetch by IPN instead, clear the InvenTree Part PK Document Property and Fetch again.",
                    "Link Mismatch",
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return answer == MessageDialogResult.Ok;
            };

            var view = new TaskPaneView { DataContext = _vm };
            var host = new ElementHost { Dock = DockStyle.Fill, Child = view };
            Controls.Add(host);
            Dock = DockStyle.Fill;
        }

        // -- BOM event handler -------------------------------------------------

        private async void OnCompareBomRequested(object? sender, EventArgs e)
        {
            if (_client == null) return;

            var preFlightCheck = _vm.CreateBomCompareReadinessCheck();
            if (preFlightCheck == null) return;

            var mappingResult = _mappingProvider?.GetMappingResult()
                ?? new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());
            if (!mappingResult.CanUseForPartSync)
                return;

            var readiness = await RunReadinessCheckAsync(preFlightCheck).ConfigureAwait(true);
            if (readiness == null) return;

            var pushedRevision = false;
            while (true)
            {
                if (readiness.Outcome == BomCompareOutcome.Ready)
                    break;

                if (readiness.Outcome == BomCompareOutcome.BomColumnAliasesMissing)
                {
                    ShowBomColumnAliasesMissingDialog(mappingResult.Config);
                    break;
                }

                if (readiness.Outcome == BomCompareOutcome.SwIsNewer)
                {
                    if (pushedRevision)
                    {
                        ShowBomCompareError("The SolidWorks revision is still newer after the update. Close this file and pull the latest revision from InvenTree.");
                        return;
                    }

                    if (!await AskAndPushRevisionAsync(preFlightCheck, readiness).ConfigureAwait(true))
                        return;

                    pushedRevision = true;
                    readiness = await RunReadinessCheckAsync(preFlightCheck).ConfigureAwait(true);
                    if (readiness == null) return;
                    continue;
                }

                // All remaining outcomes are terminal.
                switch (readiness.Outcome)
                {
                    case BomCompareOutcome.SessionNotPopulated:
                        // The session dropped between button enablement and the
                        // check — stop silently, like a Stale/Cancelled resume.
                        return;

                    case BomCompareOutcome.PkNotStamped:
                        MessageDialog.ShowOK(
                            SolidWorksWindowHandle.Get(),
                            "No InvenTree Part PK is stored in this assembly\u2019s Document Properties.\n\n"
                            + "Apply the InvenTree PK to the document first, then try again.",
                            "BOM Compare \u2014 PK Missing",
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;

                    case BomCompareOutcome.PartNotAssembly:
                        {
                            var partLabel = string.IsNullOrEmpty(readiness.Ipn)
                                ? "The linked InvenTree part"
                                : $"InvenTree part \u201c{readiness.Ipn}\u201d";
                            MessageDialog.ShowOK(
                                SolidWorksWindowHandle.Get(),
                                $"{partLabel} is not flagged as Assembly and cannot hold a BOM.\n\n"
                                + "Set the Assembly flag on the part in InvenTree first, then Fetch again.",
                                "BOM Compare \u2014 Not an Assembly",
                                System.Windows.Forms.MessageBoxIcon.Warning);
                            return;
                        }

                    case BomCompareOutcome.ItIsNewer:
                        MessageDialog.ShowOK(
                            SolidWorksWindowHandle.Get(),
                            $"InvenTree is at revision \u201c{readiness.ItRevision}\u201d but this file is revision \u201c{readiness.SwRevision}\u201d.\n\n"
                            + "You have an older file open. Close it \u2014 do not push its BOM to InvenTree.",
                            "BOM Compare \u2014 Old Revision",
                            System.Windows.Forms.MessageBoxIcon.Stop);
                        return;

                    case BomCompareOutcome.Ambiguous:
                        {
                            var swLabel = string.IsNullOrEmpty(readiness.SwRevision) ? "(blank)" : readiness.SwRevision;
                            var itLabel = string.IsNullOrEmpty(readiness.ItRevision) ? "(blank)" : readiness.ItRevision;
                            MessageDialog.ShowOK(
                                SolidWorksWindowHandle.Get(),
                                $"Revision mismatch (SolidWorks: {swLabel} / InvenTree: {itLabel}).\n\n"
                                + "The order cannot be determined automatically. Resolve the revision manually before comparing the BOM.",
                                "BOM Compare \u2014 Revision Ambiguous",
                                System.Windows.Forms.MessageBoxIcon.Warning);
                            return;
                        }

                    case BomCompareOutcome.BomTableMissing:
                        {
                            new BomTableMissingDialog(_vm.BomKeyword, SolidWorksWindowHandle.Get()).ShowDialog();
                            return;
                        }
                }
            }

            int pk = _vm.CurrentInvenTreePk;
            var bomVm = _vm.CreateBomCompareViewModel(mappingResult.Config, pk);
            if (bomVm == null) return;

            var tableName = _vm.GetBomTableName() ?? string.Empty;
            var window = new BomCompareWindow(bomVm, _vm.PartNumber, _vm.NamePreview,
                                               tableName);
            try
            {
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowBomCompareError($"Failed to open BOM comparison:{System.Environment.NewLine}{ex.Message}");
            }
        }

        // -- Message helpers ---------------------------------------------------

        private static void ShowBomCompareError(string message)
        {
            MessageDialog.ShowOK(
                SolidWorksWindowHandle.Get(),
                message,
                "BOM Compare",
                System.Windows.Forms.MessageBoxIcon.Error);
        }

        private static void ShowBomColumnAliasesMissingDialog(PropertyMappingConfig mapping)
        {
            var missing = mapping.GetMissingBomCompareAliases();
            var aliasList = string.Join(" and ", missing);
            var valueList = string.Join(" or ", missing);
            var verb = missing.Count == 1 ? "is" : "are";
            var pronoun = missing.Count == 1 ? "it is" : "they are";
            var aliasWord = missing.Count == 1 ? "Alias" : "Aliases";

            var message = $"The {aliasList} BOM Column {aliasWord} {verb} blank.\n\n"
                        + $"BOM Compare will not find {valueList} values until {pronoun} set "
                        + "in Settings > Property Mappings.\n\n"
                        + "Click OK to open the comparison anyway.";

            MessageDialog.ShowOK(
                SolidWorksWindowHandle.Get(),
                message,
                "BOM Compare \u2014 Missing Alias",
                System.Windows.Forms.MessageBoxIcon.Warning);
        }

        private static async Task<bool> AskAndPushRevisionAsync(BomCompareReadinessCheck preFlightCheck, BomCompareReadiness readiness)
        {
            var swLabel = string.IsNullOrEmpty(readiness.SwRevision) ? "(blank)" : readiness.SwRevision;
            var itLabel = string.IsNullOrEmpty(readiness.ItRevision) ? "(blank)" : readiness.ItRevision;
            var answer = MessageDialog.ShowOKCancel(
                SolidWorksWindowHandle.Get(),
                $"Revision mismatch:\n  SolidWorks:  {swLabel}\n  InvenTree:   {itLabel}\n\n"
                + $"Update InvenTree to revision \u201c{swLabel}\u201d and proceed?",
                "BOM Compare \u2014 Revision Mismatch",
                System.Windows.Forms.MessageBoxIcon.Question);

            if (answer != MessageDialogResult.Ok) return false;

            PartSyncResult pushResult;
            try
            {
                pushResult = await preFlightCheck.PushRevisionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageDialog.ShowOK(
                    SolidWorksWindowHandle.Get(),
                    $"Failed to update revision in InvenTree:{System.Environment.NewLine}{ex.Message}",
                    "BOM Compare \u2014 Revision Update Failed",
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }

            // Surface the typed result: stale is silent (a newer operation
            // owns the pane); anything non-success reports its diagnostic.
            switch (pushResult.Outcome)
            {
                case PartSyncOutcome.Success:
                case PartSyncOutcome.SucceededWithWarning:
                    return true;
                case PartSyncOutcome.Stale:
                case PartSyncOutcome.Cancelled:
                    return false;
                default:
                    MessageDialog.ShowOK(
                        SolidWorksWindowHandle.Get(),
                        $"Failed to update revision in InvenTree:{System.Environment.NewLine}"
                        + (pushResult.Diagnostic ?? pushResult.Outcome.ToString()),
                        "BOM Compare \u2014 Revision Update Failed",
                        System.Windows.Forms.MessageBoxIcon.Error);
                    return false;
            }
        }

        /// <summary>
        /// Runs the readiness check and turns a thrown failure into the BOM
        /// Compare error dialog; null means the failure was already shown.
        /// The check itself never fetches — snapshot capture can still throw
        /// crossing the STA dispatcher.
        /// </summary>
        private static async Task<BomCompareReadiness?> RunReadinessCheckAsync(
            BomCompareReadinessCheck preFlightCheck)
        {
            try
            {
                return await preFlightCheck.CheckAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ShowBomCompareError($"Could not evaluate BOM Compare readiness:{System.Environment.NewLine}{ex.Message}");
                return null;
            }
        }

        // -- Host callback routing ---------------------------------------------
        // Each entry drives the coordinator lifecycle member first, then hands
        // the result to the ViewModel's projection entries — the ViewModel
        // never calls document-lifecycle members itself.

        /// <summary>
        /// The active SolidWorks document changed (opened, activated, loaded) —
        /// the coordinator re-captures and revalidates, then the pane projects.
        /// </summary>
        public void NotifyActiveDocumentChanged()
        {
            _coordinator.UpdateDocument();
            _vm.ProjectDocumentUpdate();
        }

        /// <summary>The last document was closed — the pane clears.</summary>
        public void NotifyLastDocumentClosed()
        {
            _coordinator.NotifyDocumentClosed();
            _vm.ProjectDocumentClosed();
        }

        /// <summary>
        /// A mapped SolidWorks Document Property changed: the coordinator
        /// consumes write echoes, re-captures, and classifies; a re-evaluation
        /// classification runs the full document update, then the pane maps
        /// the classification.
        /// </summary>
        public void OnDocumentPropertyChanged(string name, string value)
        {
            var change = _coordinator.NotifyDocumentPropertyChanged(name, value);
            if (change == PartSyncPropertyChange.Reevaluated)
                _coordinator.UpdateDocument();
            _vm.ProjectPropertyChange(change);
        }

        public void UpdateClient(IInventreeClient? client)
        {
            _client = client;
            _coordinator.UpdateClient(client);
            _coordinator.UpdateDocument();
            _vm.UpdateClient(client);
            _vm.UpdateCreatePartValidationService(_createPartValidator);
        }

        public void UpdateMapping(IPropertyMappingProvider provider)
        {
            _mappingProvider = provider;
            _coordinator.UpdateMapping(provider);
            _vm.UpdateMapping(provider);
            AttachMappingProvider();
        }

        /// <summary>
        /// The mapping file changed under the same provider: rebind the
        /// coordinator, then re-project — marshalled onto the host STA thread.
        /// </summary>
        private void OnMappingFileChanged() =>
            _dispatcher.Run(() =>
            {
                _coordinator.UpdateMapping(_mappingProvider);
                _vm.UpdateMapping(_mappingProvider);
            });

        private void AttachMappingProvider() =>
            MappingChangedSubscription.SubscribeTo(
                ref _mappingChangedSubscription, _mappingProvider, OnMappingFileChanged);

        public void UpdateWaitForServerAssignedIpn(bool value)
        {
            _vm.WaitForServerAssignedIpn = value;
        }

        public void UpdateBomState(IAssemblyBomService bomService)
            => _vm.UpdateBomState(bomService);

        /// <summary>
        /// Teardown: detach the ViewModel's coordinator subscription and the
        /// mapping-provider subscription first so nothing retains the pane,
        /// then dispose the coordinator so any in-flight Part Sync operation
        /// is invalidated before the hosted view goes away.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MappingChangedSubscription.UnsubscribeFrom(ref _mappingChangedSubscription);
                _vm.Dispose();
                _coordinator.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
