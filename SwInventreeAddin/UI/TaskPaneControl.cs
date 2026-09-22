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
    /// All business logic lives in <see cref="TaskPaneViewModel"/>; Part Sync
    /// session lifecycle and workflow live in <see cref="PartSyncCoordinator"/>.
    /// </summary>
    public class TaskPaneControl : UserControl
    {
        private readonly TaskPaneViewModel _vm;
        private readonly PartSyncCoordinator _coordinator;
        private IInventreeClient? _client;
        private readonly ICreatePartValidationErrorService _createPartValidator;
        private IPropertyMappingProvider? _mappingProvider;

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
            var dispatcher = new SynchronizationContextStaDispatcher();
            _coordinator = new PartSyncCoordinator(propertyService, dispatcher, client, mappingProvider);
            _vm = new TaskPaneViewModel(_coordinator, dispatcher, client, mappingProvider, configProvider, _createPartValidator);
            _vm.SettingsRequested += (s, e) => SettingsRequested?.Invoke(this, e);
            _vm.CompareBomRequested += OnCompareBomRequested;

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

            BomCompareReadiness readiness;
            try
            {
                readiness = await preFlightCheck.CheckAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ShowBomCompareError($"Could not load part from InvenTree:{System.Environment.NewLine}{ex.Message}");
                return;
            }

            var pushedRevision = false;
            while (true)
            {
                if (readiness.Outcome == BomCompareOutcome.Ready)
                    break;

                if (readiness.Outcome == BomCompareOutcome.FetchConfirmationRequired
                    && readiness.FetchResult != null)
                {
                    // The ensure-fetch parked on a typed confirmation (duplicate
                    // IPN, Link Mismatch): prompt here, resume the coordinator's
                    // pending operation, then re-run the check. A declined,
                    // cancelled, or stale resume stops the flow silently — the
                    // user already answered the prompt.
                    var resumed = await _vm
                        .ResumeFetchConfirmationAsync(readiness.FetchResult)
                        .ConfigureAwait(true);
                    if (resumed.Outcome != PartSyncOutcome.Success)
                        return;

                    try
                    {
                        readiness = await preFlightCheck.CheckAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        ShowBomCompareError($"Could not load part from InvenTree:{System.Environment.NewLine}{ex.Message}");
                        return;
                    }
                    continue;
                }

                if (readiness.Outcome == BomCompareOutcome.FetchFailed)
                {
                    // Preserve the typed ensure outcome: stale/cancelled are
                    // silent (a newer operation owns the pane / the user
                    // declined); everything else surfaces an honest error —
                    // never "create the part" for a server or lifecycle failure.
                    var r = readiness.FetchResult;
                    if (r?.Outcome == PartSyncOutcome.Stale
                        || r?.Outcome == PartSyncOutcome.Cancelled)
                        return;
                    ShowBomCompareError(DescribeBomFetchFailure(r));
                    return;
                }

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
                    try
                    {
                        readiness = await preFlightCheck.CheckAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        ShowBomCompareError($"Could not load part from InvenTree:{System.Environment.NewLine}{ex.Message}");
                        return;
                    }
                    continue;
                }

                // All remaining outcomes are terminal.
                switch (readiness.Outcome)
                {
                    case BomCompareOutcome.PkNotFound:
                        MessageDialog.ShowOK(
                            SolidWorksWindowHandle.Get(),
                            $"'{readiness.PartNumber}' was not found in InvenTree.\n\nCreate the part in InvenTree first, then try again.",
                            "BOM Compare",
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;

                    case BomCompareOutcome.PkNotStamped:
                        MessageDialog.ShowOK(
                            SolidWorksWindowHandle.Get(),
                            "No InvenTree Part PK is stored in this assembly\u2019s Document Properties.\n\n"
                            + "Apply the InvenTree PK to the document first, then try again.",
                            "BOM Compare \u2014 PK Missing",
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;

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
        /// Words a non-success ensure-populate outcome for the BOM Compare
        /// error dialog — mirroring the status wording the ViewModel uses for
        /// the same terminal fetch outcomes.
        /// </summary>
        private static string DescribeBomFetchFailure(PartSyncResult? result)
        {
            if (result == null)
                return "Could not load part from InvenTree.";

            switch (result.Outcome)
            {
                case PartSyncOutcome.DuplicateNoRevisionMatch:
                    return $"{result.Candidates?.Count ?? 0} parts share IPN \u2018{result.Ipn}\u2019 but none match " +
                           $"SW revision {RevisionLabel(result.SwRevision)}. Resolve in InvenTree.";
                case PartSyncOutcome.DuplicateAmbiguous:
                    return $"{result.Candidates?.Count ?? 0} parts share IPN \u2018{result.Ipn}\u2019 and revision " +
                           $"{RevisionLabel(result.SwRevision)}. Resolve duplicates in InvenTree.";
                case PartSyncOutcome.InvalidOperation:
                    return result.Diagnostic ?? "The part fetch is not available right now.";
                default:
                    return $"Could not load part from InvenTree:{System.Environment.NewLine}"
                         + (result.Diagnostic ?? result.Outcome.ToString());
            }
        }

        private static string RevisionLabel(string? revision) =>
            string.IsNullOrEmpty(revision) ? "(blank)" : revision;

        // -- Delegation to ViewModel -------------------------------------------

        /// <summary>
        /// The active SolidWorks document changed (opened, activated, loaded) —
        /// the ViewModel re-evaluates the pane against the new document.
        /// </summary>
        public void NotifyActiveDocumentChanged() => _vm.LoadPartNumber();

        /// <summary>The last document was closed — the ViewModel clears the pane.</summary>
        public void NotifyLastDocumentClosed() => _vm.ClearAll();

        public void RefreshProperties() => _vm.RefreshCurrentProperties();
        public void OnDocumentPropertyChanged(string name, string value) => _vm.OnDocumentPropertyChanged(name, value);

        public void UpdateClient(IInventreeClient? client)
        {
            _client = client;
            _vm.UpdateClient(client);
            _vm.UpdateCreatePartValidationService(_createPartValidator);
        }

        public void UpdateMapping(IPropertyMappingProvider provider)
        {
            _mappingProvider = provider;
            _vm.UpdateMapping(provider);
        }

        public void UpdateWaitForServerAssignedIpn(bool value)
        {
            _vm.WaitForServerAssignedIpn = value;
        }

        public void UpdateBomState(IAssemblyBomService bomService)
            => _vm.UpdateBomState(bomService);

        /// <summary>
        /// Teardown: disposes the coordinator first so any in-flight Part Sync
        /// operation is invalidated before the hosted view goes away.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _coordinator.Dispose();
            base.Dispose(disposing);
        }
    }
}
