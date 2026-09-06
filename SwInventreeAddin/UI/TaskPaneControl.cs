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
    /// All business logic lives in <see cref="TaskPaneViewModel"/>.
    /// </summary>
    public class TaskPaneControl : UserControl
    {
        private readonly TaskPaneViewModel             _vm;
        private IInventreeClient?                      _client;
        private ICreatePartValidationErrorService      _createPartValidator;
        private IPropertyMappingProvider?              _mappingProvider;
        private IAssemblyBomService?                   _assemblyBomService;
        private string                                 _bomKeyword = "inventree";

        public event EventHandler? SettingsRequested;

        // -- Constructors ------------------------------------------------------

        public TaskPaneControl(
            IInventreeClient?                   client,
            IDocumentPropertyService            propertyService,
            IViewportCaptureService?            viewportService,
            IPropertyMappingProvider?           mappingProvider,
            IConfigProvider?                    configProvider,
            ICreatePartValidationErrorService   createPartValidator)
        {
            _client              = client;
            _createPartValidator = createPartValidator;
            _mappingProvider     = mappingProvider;

            _vm = new TaskPaneViewModel(client, propertyService, viewportService, mappingProvider, configProvider, _createPartValidator);
            _vm.SettingsRequested   += (s, e) => SettingsRequested?.Invoke(this, e);
            _vm.CompareBomRequested += OnCompareBomRequested;
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
                var nl      = System.Environment.NewLine;
                var lines   = string.Join(nl, System.Linq.Enumerable.Select(allParts, p =>
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

            var view = new TaskPaneView { DataContext = _vm };
            var host = new ElementHost { Dock = DockStyle.Fill, Child = view };
            Controls.Add(host);
            Dock = DockStyle.Fill;
        }

        // -- BOM event handler -------------------------------------------------

        private async void OnCompareBomRequested(object? sender, EventArgs e)
        {
            if (_client == null || _assemblyBomService == null) return;

            var mappingResult = _mappingProvider?.GetMappingResult()
                ?? new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());
            if (!mappingResult.CanUseForPartSync)
                return;

            var preFlightCheck = new BomCompareReadinessCheck(_vm, _assemblyBomService, _bomKeyword);
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
                            "No InvenTree PK is stored in this assembly\u2019s custom properties.\n\n"
                            + "Sync the part with InvenTree first to stamp the PK, then try again.",
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
                        new BomTableMissingDialog(_bomKeyword, SolidWorksWindowHandle.Get()).ShowDialog();
                        return;
                    }
                }
            }

            int pk      = _vm.CurrentInvenTreePk;
            var bomVm   = new BomCompareViewModel(
                _client, _assemblyBomService, mappingResult.Config, pk, _bomKeyword);

            var tableName = _assemblyBomService.GetBomTableName(_bomKeyword);
            var window  = new BomCompareWindow(bomVm, _vm.PartNumber, _vm.NamePreview,
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
            var aliasList  = string.Join(" and ", missing);
            var valueList  = string.Join(" or ", missing);
            var verb       = missing.Count == 1 ? "is" : "are";
            var pronoun    = missing.Count == 1 ? "it is" : "they are";
            var aliasWord  = missing.Count == 1 ? "Alias" : "Aliases";

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

            try
            {
                await preFlightCheck.PushRevisionAsync().ConfigureAwait(true);
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

            return true;
        }

        // -- Delegation to ViewModel -------------------------------------------

        public void LoadPartNumber()                                    => _vm.LoadPartNumber();
        public void RefreshProperties()                                 => _vm.RefreshCurrentProperties();
        public void ClearAll()                                          => _vm.ClearAll();
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

        public void UpdateBomState(IAssemblyBomService bomService, string keyword)
        {
            _assemblyBomService = bomService;
            _bomKeyword         = keyword;
        }
    }
}
