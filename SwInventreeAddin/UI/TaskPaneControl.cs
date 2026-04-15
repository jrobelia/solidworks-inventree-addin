using System;
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
        private readonly TaskPaneViewModel    _vm;
        private IInventreeClient?             _client;
        private IPropertyMappingProvider?     _mappingProvider;
        private IAssemblyBomService?          _assemblyBomService;
        private string                        _bomKeyword = "inventree";

        public event EventHandler? SettingsRequested;

        // -- Constructors ------------------------------------------------------

        public TaskPaneControl(IInventreeClient? client, IDocumentPropertyService propertyService)
            : this(client, propertyService, null) { }

        public TaskPaneControl(
            IInventreeClient?        client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService)
            : this(client, propertyService, viewportService, null) { }

        public TaskPaneControl(
            IInventreeClient?         client,
            IDocumentPropertyService  propertyService,
            IViewportCaptureService?  viewportService,
            IPropertyMappingProvider? mappingProvider = null)
        {
            _client          = client;
            _mappingProvider = mappingProvider;

            _vm = new TaskPaneViewModel(client, propertyService, viewportService, mappingProvider);
            _vm.SettingsRequested   += (s, e) => SettingsRequested?.Invoke(this, e);
            _vm.CompareBomRequested += OnCompareBomRequested;
            _vm.ConfirmMissingProperties = missing =>
            {
                var bullet = string.Join(System.Environment.NewLine + "  \u2022 ", missing);
                var result = System.Windows.MessageBox.Show(
                    "The following mapped property names don\u2019t exist in this document:"
                    + System.Environment.NewLine + "  \u2022 " + bullet
                    + System.Environment.NewLine + System.Environment.NewLine
                    + "The property will be created. Write anyway?",
                    "Property Not Found",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning);
                return result == System.Windows.MessageBoxResult.OK;
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

            // Auto-fetch from InvenTree if we don't already have the PK in memory.
            if (_vm.CurrentInvenTreePk == 0)
            {
                try
                {
                    await _vm.FetchPartAsync();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Could not load part from InvenTree:{System.Environment.NewLine}{ex.Message}",
                        "BOM Compare",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }
            }

            int pk = _vm.CurrentInvenTreePk;
            if (pk == 0)
            {
                System.Windows.MessageBox.Show(
                    $"'{_vm.PartNumber}' was not found in InvenTree.\n\nCreate the part in InvenTree first, then try again.",
                    "BOM Compare",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Gate 1: PK must be stamped in this document's custom properties.
            // Without it we can't confirm this file has ever been explicitly linked to its InvenTree record.
            _vm.RefreshCurrentProperties();
            if (string.IsNullOrWhiteSpace(_vm.CurrentPk))
            {
                System.Windows.MessageBox.Show(
                    "No InvenTree PK is stored in this assembly\u2019s custom properties.\n\n"
                    + "Sync the part with InvenTree first to stamp the PK, then try again.",
                    "BOM Compare \u2014 PK Missing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Gate 2: Revision must be resolvable and SW must not be behind InvenTree.
            var swRev    = _vm.CurrentRevision?.Trim()  ?? string.Empty;
            var itRev    = _vm.RevisionPreview?.Trim()  ?? string.Empty;
            var revOrder = RevisionComparer.Compare(swRev, itRev);

            if (revOrder == RevisionOrder.ItIsNewer)
            {
                // SW is behind InvenTree — user has an old file open. Hard block.
                System.Windows.MessageBox.Show(
                    $"InvenTree is at revision \u201c{itRev}\u201d but this file is revision \u201c{swRev}\u201d.\n\n"
                    + "You have an older file open. Close it — do not push its BOM to InvenTree.",
                    "BOM Compare \u2014 Old Revision",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Stop);
                return;
            }

            if (revOrder == RevisionOrder.Ambiguous)
            {
                var swLabel = string.IsNullOrEmpty(swRev) ? "(blank)" : swRev;
                var itLabel = string.IsNullOrEmpty(itRev) ? "(blank)" : itRev;
                System.Windows.MessageBox.Show(
                    $"Revision mismatch (SolidWorks: {swLabel} / InvenTree: {itLabel}).\n\n"
                    + "The order cannot be determined automatically. Resolve the revision manually before comparing the BOM.",
                    "BOM Compare \u2014 Revision Ambiguous",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (revOrder == RevisionOrder.SwIsNewer)
            {
                // SW is ahead of InvenTree — offer to push the revision before proceeding.
                var swLabel = string.IsNullOrEmpty(swRev) ? "(blank)" : swRev;
                var itLabel = string.IsNullOrEmpty(itRev) ? "(blank)" : itRev;
                var answer  = System.Windows.MessageBox.Show(
                    $"Revision mismatch:\n  SolidWorks:  {swLabel}\n  InvenTree:   {itLabel}\n\n"
                    + $"Update InvenTree to revision \u201c{swLabel}\u201d and proceed?",
                    "BOM Compare \u2014 Revision Mismatch",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question);

                if (answer != System.Windows.MessageBoxResult.OK) return;

                try
                {
                    await _vm.PushRevisionToInventreeAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to update revision in InvenTree:{System.Environment.NewLine}{ex.Message}",
                        "BOM Compare \u2014 Revision Update Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }
            }

            var mapping = _mappingProvider?.GetMapping() ?? new PropertyMappingConfig();
            var bomVm   = new BomCompareViewModel(
                _client, _assemblyBomService, mapping, pk, _bomKeyword);

            bomVm.BomSynced += (_, diffCount) => _vm.UpdateBomStatus(diffCount);

            var bomInfo = _assemblyBomService.GetBomInfo(_bomKeyword);
            var window  = new BomCompareWindow(bomVm, _vm.PartNumber, _vm.NamePreview,
                                               bomInfo.TableName, bomInfo.NeedsRebuild);
            try
            {
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to open BOM comparison:{System.Environment.NewLine}{ex.Message}",
                    "BOM Compare Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        // -- Delegation to ViewModel -------------------------------------------

        public void LoadPartNumber()    => _vm.LoadPartNumber();
        public void RefreshProperties() => _vm.RefreshCurrentProperties();
        public void ClearAll()          => _vm.ClearAll();

        public void UpdateClient(IInventreeClient? client)
        {
            _client = client;
            _vm.UpdateClient(client);
        }

        public void UpdateMapping(IPropertyMappingProvider provider)
        {
            _mappingProvider = provider;
            _vm.UpdateMapping(provider);
        }

        public void UpdateBomState(IAssemblyBomService bomService, string keyword)
        {
            _assemblyBomService = bomService;
            _bomKeyword         = keyword;
        }
    }
}

