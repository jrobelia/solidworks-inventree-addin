using System.Runtime.InteropServices;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.AddIn
{
    /// <summary>
    /// Entry point for the SolidWorks COM add-in.
    /// SolidWorks instantiates this class via COM when the add-in loads.
    /// </summary>
    [ComVisible(true)]
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public class SwAddin
    {
        private dynamic?        _swApp;
        private TaskPaneControl? _taskPaneControl;
        private dynamic?        _taskPaneView;

        public bool ConnectToSW(object thisSW, int cookie)
        {
            _swApp = thisSW;

            var configProvider  = new JsonFileConfigProvider(ResolveConfigPath());
            var httpClient      = new System.Net.Http.HttpClient();
            var config          = configProvider.GetServerConfig();
            httpClient.BaseAddress = new System.Uri(config.Url);
            var inventreeClient = new InventreeHttpClient(httpClient, config.ApiKey);
            var propertyService = new SwDocumentPropertyService(_swApp);

            _taskPaneControl = new TaskPaneControl(inventreeClient, propertyService);

            _taskPaneView = _swApp.CreateTaskpaneView2("", "InvenTree");
            _taskPaneView.DisplayWindowFromHandle(_taskPaneControl.Handle.ToInt32());

            return true;
        }

        public bool DisconnectFromSW()
        {
            _taskPaneView?.DeleteView();
            _taskPaneControl?.Dispose();
            _taskPaneControl = null;
            _taskPaneView    = null;

            if (_swApp != null)
            {
                Marshal.ReleaseComObject(_swApp);
                _swApp = null;
            }

            return true;
        }

        private static string ResolveConfigPath()
        {
            var assemblyDir = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
                ?? string.Empty;
            return System.IO.Path.Combine(assemblyDir, "inventree_servers.json");
        }
    }
}
