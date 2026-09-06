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
                var result = System.Windows.Forms.MessageBox.Show(
                    WindowHandleOwner.FromSolidWorks(),
                    "The following mapped property names don\u2019t exist in this document:"
                    + System.Environment.NewLine + "  \u2022 " + bullet
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "The property will be created. Write anyway?",
                    "Property Not Found",
                    System.Windows.Forms.MessageBoxButtons.OKCancel,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return result == System.Windows.Forms.DialogResult.OK;
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
                var answer = System.Windows.Forms.MessageBox.Show(
                    WindowHandleOwner.FromSolidWorks(),
                    $"IPN \u201c{matched.Ipn}\u201d has {allParts.Count} parts in InvenTree:{nl}{nl}"
                    + lines + nl + nl
                    + $"Loading PK {matched.Pk} (Rev {matchRev}). Proceed?",
                    "Duplicate IPN \u2014 Revision Matched",
                    System.Windows.Forms.MessageBoxButtons.OKCancel,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return answer == System.Windows.Forms.DialogResult.OK;
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
                        System.Windows.Forms.MessageBox.Show(
                            WindowHandleOwner.FromSolidWorks(),
                            $"'{readiness.PartNumber}' was not found in InvenTree.\n\nCreate the part in InvenTree first, then try again.",
                            "BOM Compare",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;

                    case BomCompareOutcome.PkNotStamped:
                        System.Windows.Forms.MessageBox.Show(
                            WindowHandleOwner.FromSolidWorks(),
                            "No InvenTree PK is stored in this assembly\u2019s custom properties.\n\n"
                            + "Sync the part with InvenTree first to stamp the PK, then try again.",
                            "BOM Compare \u2014 PK Missing",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                        return;

                    case BomCompareOutcome.ItIsNewer:
                        System.Windows.Forms.MessageBox.Show(
                            WindowHandleOwner.FromSolidWorks(),
                            $"InvenTree is at revision \u201c{readiness.ItRevision}\u201d but this file is revision \u201c{readiness.SwRevision}\u201d.\n\n"
                            + "You have an older file open. Close it \u2014 do not push its BOM to InvenTree.",
                            "BOM Compare \u2014 Old Revision",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Stop);
                        return;

                    case BomCompareOutcome.Ambiguous:
                    {
                        var swLabel = string.IsNullOrEmpty(readiness.SwRevision) ? "(blank)" : readiness.SwRevision;
                        var itLabel = string.IsNullOrEmpty(readiness.ItRevision) ? "(blank)" : readiness.ItRevision;
                        System.Windows.Forms.MessageBox.Show(
                            WindowHandleOwner.FromSolidWorks(),
                            $"Revision mismatch (SolidWorks: {swLabel} / InvenTree: {itLabel}).\n\n"
                            + "The order cannot be determined automatically. Resolve the revision manually before comparing the BOM.",
                            "BOM Compare \u2014 Revision Ambiguous",
                            System.Windows.Forms.MessageBoxButtons.OK,
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

            int pk      = _vm.CurrentInvenTreePk;
            var bomVm   = _vm.CreateBomCompareViewModel(mappingResult.Config, pk);
            if (bomVm == null) return;

            var tableName = _vm.GetBomTableName() ?? string.Empty;
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
            System.Windows.Forms.MessageBox.Show(
                WindowHandleOwner.FromSolidWorks(),
                message,
                "BOM Compare",
                System.Windows.Forms.MessageBoxButtons.OK,
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

            System.Windows.Forms.MessageBox.Show(
                WindowHandleOwner.FromSolidWorks(),
                message,
                "BOM Compare \u2014 Missing Alias",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Warning);
        }

        private static async Task<bool> AskAndPushRevisionAsync(BomCompareReadinessCheck preFlightCheck, BomCompareReadiness readiness)
        {
            var swLabel = string.IsNullOrEmpty(readiness.SwRevision) ? "(blank)" : readiness.SwRevision;
            var itLabel = string.IsNullOrEmpty(readiness.ItRevision) ? "(blank)" : readiness.ItRevision;
            var answer = System.Windows.Forms.MessageBox.Show(
                WindowHandleOwner.FromSolidWorks(),
                $"Revision mismatch:\n  SolidWorks:  {swLabel}\n  InvenTree:   {itLabel}\n\n"
                + $"Update InvenTree to revision \u201c{swLabel}\u201d and proceed?",
                "BOM Compare \u2014 Revision Mismatch",
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (answer != System.Windows.Forms.DialogResult.OK) return false;

            try
            {
                await preFlightCheck.PushRevisionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    WindowHandleOwner.FromSolidWorks(),
                    $"Failed to update revision in InvenTree:{System.Environment.NewLine}{ex.Message}",
                    "BOM Compare \u2014 Revision Update Failed",
                    System.Windows.Forms.MessageBoxButtons.OK,
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
            => _vm.UpdateBomState(bomService, keyword);
    }

    /// <summary>Wraps an arbitrary Win32 window handle so it can be used as the owner of
    /// a WinForms message box, which parents the dialog for modality and z-order.</summary>
    internal sealed class WindowHandleOwner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }

        public WindowHandleOwner(IntPtr handle) => Handle = handle;

        /// <summary>Returns an owner parented to the SolidWorks main window.</summary>
        public static System.Windows.Forms.IWin32Window FromSolidWorks()
            => new WindowHandleOwner(SolidWorksWindowHandle.Get());
    }
}
