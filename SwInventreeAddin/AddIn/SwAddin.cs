using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.AddIn
{
    /// <summary>
    /// Entry point for the SolidWorks COM add-in.
    /// SolidWorks instantiates this class via COM when the add-in loads.
    /// 
    /// The ComRegisterFunction / ComUnregisterFunction methods are called
    /// automatically by RegAsm and write the SolidWorks-specific registry
    /// entries that make this add-in visible in the SolidWorks Add-ins dialog.
    /// </summary>
    [ComVisible(true)]
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    public class SwAddin : ISwAddin
    {
        private const string AddinGuid        = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890";
        private const string AddinTitle       = "InvenTree";
        private const string AddinDescription = "Imports part data from InvenTree into SolidWorks custom properties";

        /// <summary>
        /// Called automatically by RegAsm /codebase — writes the registry keys
        /// that tell SolidWorks this add-in exists and should appear in its menu.
        /// </summary>
        [ComRegisterFunction]
        public static void RegisterFunction(System.Type t)
        {
            // HKLM key: SolidWorks reads this to discover the add-in
            using (var hklm = Registry.LocalMachine.CreateSubKey(
                $@"SOFTWARE\SolidWorks\Addins\{{{AddinGuid}}}"))
            {
                hklm.SetValue(null,          0);               // 0 = don't force load at startup
                hklm.SetValue("Title",       AddinTitle);
                hklm.SetValue("Description", AddinDescription);
            }

            // HKCU key: per-user load-at-startup preference (1 = enabled)
            using (var hkcu = Registry.CurrentUser.CreateSubKey(
                $@"Software\SolidWorks\Addins\{{{AddinGuid}}}"))
            {
                hkcu.SetValue(null, 1);   // 1 = load automatically on SolidWorks start
            }
        }

        /// <summary>
        /// Called automatically by RegAsm /u — removes the SolidWorks registry
        /// entries when the add-in is unregistered.
        /// </summary>
        [ComUnregisterFunction]
        public static void UnregisterFunction(System.Type t)
        {
            Registry.LocalMachine.DeleteSubKey(
                $@"SOFTWARE\SolidWorks\Addins\{{{AddinGuid}}}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKey(
                $@"Software\SolidWorks\Addins\{{{AddinGuid}}}", throwOnMissingSubKey: false);
        }

        private ISldWorks?        _swApp;
        private TaskPaneControl?  _taskPaneControl;
        private ITaskpaneView?    _taskPaneView;
        private System.Net.Http.HttpClient? _httpClient;
        private int               _addinCookie;

        public bool ConnectToSW(object thisSW, int cookie)
        {
            try
            {
                _swApp       = (ISldWorks)thisSW;
                _addinCookie = cookie;

                // Tell SolidWorks our cookie so it can track us
                _swApp.SetAddinCallbackInfo2(0, this, cookie);

                var configProvider  = new JsonFileConfigProvider(ResolveConfigPath());
                var config          = configProvider.GetServerConfig();

                _httpClient             = new System.Net.Http.HttpClient();
                _httpClient.BaseAddress = new System.Uri(config.Url);

                var inventreeClient = new InventreeHttpClient(_httpClient, config.ApiKey);
                var propertyService = new SwDocumentPropertyService(_swApp);

                _taskPaneControl = new TaskPaneControl(inventreeClient, propertyService);

                _taskPaneView = (ITaskpaneView)_swApp.CreateTaskpaneView2("", AddinTitle);
                _taskPaneView.DisplayWindowFromHandle(_taskPaneControl.Handle.ToInt32());

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"InvenTree add-in failed to load:{System.Environment.NewLine}{ex.Message}",
                    "InvenTree Add-In Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            _taskPaneView?.DeleteView();
            if (_taskPaneView != null)
            {
                Marshal.ReleaseComObject(_taskPaneView);
                _taskPaneView = null;
            }

            _taskPaneControl?.Dispose();
            _taskPaneControl = null;

            _httpClient?.Dispose();
            _httpClient = null;

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
