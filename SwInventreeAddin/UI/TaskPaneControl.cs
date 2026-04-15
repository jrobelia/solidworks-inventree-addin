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

