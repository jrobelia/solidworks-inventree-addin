using System;
using System.IO;
using System.Net;
using System.Reflection;
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
        /// Static constructor — runs once when SolidWorks first loads this class.
        /// Registers an assembly resolver so .NET can find our dependency DLLs
        /// in the add-in's own folder, not just the SolidWorks installation folder.
        /// </summary>
        static SwAddin()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var addinDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? string.Empty;

                // Strip the version/culture info to get just the DLL name
                var assemblyName = new AssemblyName(args.Name).Name + ".dll";
                var fullPath     = Path.Combine(addinDir, assemblyName);

                return File.Exists(fullPath)
                    ? Assembly.LoadFrom(fullPath)
                    : null;  // return null = let .NET try its normal search
            };
        }


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
        private SldWorks?          _swEvents;   // concrete class needed to subscribe to COM events
        private string?            _currentDisplayedPath; // path of the file currently shown in the panel
        private string?            _closingDocPath;       // path of the file currently being closed
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

                // .NET Framework 4.8 defaults to TLS 1.0; InvenTree requires TLS 1.2+
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

                var configProvider  = new JsonFileConfigProvider(ResolveConfigPath());
                var config          = configProvider.GetServerConfig();

                _httpClient             = new System.Net.Http.HttpClient();
                _httpClient.BaseAddress = new System.Uri(config.Url);

                var inventreeClient = new InventreeHttpClient(_httpClient, config.ApiKey);
                var propertyService = new SwDocumentPropertyService(_swApp);

                _taskPaneControl = new TaskPaneControl(inventreeClient, propertyService);

                // Refresh the PartNo field whenever the user opens, switches, or closes documents
                _swEvents = (SldWorks)thisSW;
                _swEvents.ActiveDocChangeNotify += OnActiveDocChange;
                _swEvents.DocumentLoadNotify2   += OnDocumentLoad;
                _swEvents.FileCloseNotify       += OnFileClose;

                var iconPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "Resources", "inventree_icon.png");

                _taskPaneView = (ITaskpaneView)_swApp.CreateTaskpaneView2(iconPath, AddinTitle);
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
            if (_swEvents != null)
            {
                _swEvents.ActiveDocChangeNotify -= OnActiveDocChange;
                _swEvents.DocumentLoadNotify2   -= OnDocumentLoad;
                _swEvents.FileCloseNotify       -= OnFileClose;
                _swEvents = null;
            }

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

        private int OnActiveDocChange()
        {
            var doc = _swApp?.ActiveDoc as IModelDoc2;
            var activePath = doc?.GetPathName();

            // If ActiveDoc is still pointing at the file currently being closed,
            // SolidWorks has not finished the transition yet — ignore this event.
            if (activePath != null &&
                string.Equals(activePath, _closingDocPath, StringComparison.OrdinalIgnoreCase))
                return 0;

            _closingDocPath = null;
            _currentDisplayedPath = activePath;
            _taskPaneControl?.LoadPartNumber();
            return 0;
        }

        private int OnDocumentLoad(string title, string path)
        {
            _closingDocPath = null;
            _currentDisplayedPath = path;
            _taskPaneControl?.LoadPartNumber();
            return 0;
        }

        private int OnFileClose(string fileName, int reason)
        {
            _closingDocPath = fileName;

            // Only clear the form if the file being closed is the one we are displaying.
            // If a background document (not the active one) closes, leave the form alone.
            if (string.Equals(fileName, _currentDisplayedPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentDisplayedPath = null;
                _taskPaneControl?.ClearAll();
            }
            return 0;
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
