using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
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
        private readonly TaskPaneViewModel _vm;

        public event EventHandler? SettingsRequested;

        // -- Constructors ------------------------------------------------------

        public TaskPaneControl(IInventreeClient? client, IDocumentPropertyService propertyService)
            : this(client, propertyService, null) { }

        public TaskPaneControl(
            IInventreeClient?        client,
            IDocumentPropertyService propertyService,
            IViewportCaptureService? viewportService)
        {
            _vm = new TaskPaneViewModel(client, propertyService, viewportService);
            _vm.SettingsRequested += (s, e) => SettingsRequested?.Invoke(this, e);

            var view = new TaskPaneView { DataContext = _vm };

            var host = new ElementHost
            {
                Dock  = DockStyle.Fill,
                Child = view,
            };

            Controls.Add(host);
            Dock = DockStyle.Fill;
        }

        // -- Delegation to ViewModel -------------------------------------------

        public void LoadPartNumber()    => _vm.LoadPartNumber();
        public void ClearAll()          => _vm.ClearAll();
        public void UpdateClient(IInventreeClient? client) => _vm.UpdateClient(client);
    }
}
