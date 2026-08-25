using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SwInventreeAddin;
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
        /// <summary>
        /// Static constructor — runs once when SolidWorks first loads this class,
        /// before any instance is created or any HttpClient is allocated.
        /// </summary>
        static SwAddin()
        {
            // .NET Framework 4.8 defaults to TLS 1.0; InvenTree requires TLS 1.2+.
            // Setting this here (rather than in ConnectToSW) guarantees it covers every
            // HttpClient in the process, including those in SettingsWindow.
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;

            // Registers an assembly resolver so .NET can find dependency DLLs
            // in the add-in's own folder, not just the SolidWorks installation folder.
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

        private ISldWorks?               _swApp;
        private SldWorks?               _swEvents;   // concrete class needed to subscribe to COM events
        private PartDoc?                _partDocEvents;
        private AssemblyDoc?            _assemblyDocEvents;
        private bool                    _hasActiveDoc;  // tracks whether a document was open on last check
        private TaskPaneControl?        _taskPaneControl;
        private ITaskpaneView?          _taskPaneView;
        private System.Net.Http.HttpClient? _httpClient;
        private int                     _addinCookie;
        private EncryptedConfigProvider? _configProvider;
        private IPropertyMappingProvider? _mappingProvider;

        public bool ConnectToSW(object thisSW, int cookie)
        {
            try
            {
                _swApp       = (ISldWorks)thisSW;
                _addinCookie = cookie;

                // Capture the SolidWorks main window handle for reliable WPF dialog parenting.
                // Process.MainWindowHandle is unreliable inside SW (returns IntPtr.Zero on SW 2026),
                // so we use the COM API instead. See issue #82.
                try
                {
                    var frame = _swApp.IFrameObject() as IFrame;
                    if (frame != null)
                        SolidWorksWindowHandle.Set(new IntPtr(frame.GetHWndx64()));
                }
                catch { /* fallback in SolidWorksWindowHandle.Get handles this */ }

                // Tell SolidWorks our cookie so it can track us
                _swApp.SetAddinCallbackInfo2(0, this, cookie);

                var configProvider  = new EncryptedConfigProvider();
                _configProvider     = configProvider;
                var config          = configProvider.GetServerConfig();

                IInventreeClient? inventreeClient = null;
                if (config != null)
                {
                    _httpClient             = new System.Net.Http.HttpClient();
                    _httpClient.BaseAddress = new System.Uri(config.Url);
                    inventreeClient = new InventreeHttpClient(_httpClient, config.ApiKey);
                }

                var propertyService  = new SwDocumentPropertyService(_swApp);
                var viewportService  = new SwViewportCaptureService(_swApp);

                _mappingProvider = new PropertyMappingProvider(config?.MappingSourcePath);

                _taskPaneControl = new TaskPaneControl(
                    inventreeClient, propertyService, viewportService, _mappingProvider, _configProvider);
                _taskPaneControl.SettingsRequested += OnSettingsRequested;

                var assemblyBomService = new SwAssemblyBomService(_swApp);
                _taskPaneControl.UpdateBomState(assemblyBomService, config?.BomKeyword ?? "inventree");
                _taskPaneControl.UpdateWaitForAutoPartNumber(config?.WaitForAutoPartNumber ?? true);

                // Refresh the PartNo field whenever the user opens or switches documents.
                // OnIdleNotify detects when the last document is closed (ActiveDoc becomes null).
                _swEvents = (SldWorks)thisSW;
                _swEvents.ActiveDocChangeNotify += OnActiveDocChange;
                _swEvents.DocumentLoadNotify2   += OnDocumentLoad;
                _swEvents.OnIdleNotify          += OnIdle;

                var iconPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "Resources", "inventree_icon.png");

                _taskPaneView = (ITaskpaneView)_swApp.CreateTaskpaneView2(iconPath, AddinTitle);
                _taskPaneView.DisplayWindowFromHandlex64(_taskPaneControl.Handle.ToInt64());

                return true;
            }
            catch (Exception ex)
            {
                var owner = new WindowHandleOwner(SolidWorksWindowHandle.Get());
                System.Windows.Forms.MessageBox.Show(
                    owner,
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
                _swEvents.OnIdleNotify          -= OnIdle;
                _swEvents = null;
            }

            UnsubscribeFromDocumentEvents();
            _taskPaneView?.DeleteView();
            if (_taskPaneView != null)
            {
                Marshal.ReleaseComObject(_taskPaneView);
                _taskPaneView = null;
            }

            if (_taskPaneControl != null)
            {
                _taskPaneControl.SettingsRequested -= OnSettingsRequested;
                _taskPaneControl.Dispose();
                _taskPaneControl = null;
            }

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
            _hasActiveDoc = (_swApp?.ActiveDoc != null);
            SubscribeToDocumentEvents();
            _taskPaneControl?.LoadPartNumber();
            return 0;
        }

        private int OnDocumentLoad(string title, string path)
        {
            _hasActiveDoc = true;
            SubscribeToDocumentEvents();
            _taskPaneControl?.LoadPartNumber();
            return 0;
        }

        /// <summary>
        /// Fires repeatedly when SolidWorks is idle. Checks whether the last document
        /// has been closed — ActiveDoc transitions from non-null to null.
        /// This is the only reliable way to detect "last document closed" because
        /// FileCloseNotify and ActiveDocChangeNotify do NOT fire in that scenario.
        /// </summary>
        private int OnIdle()
        {
            bool hasDoc = (_swApp?.ActiveDoc != null);
            if (_hasActiveDoc && !hasDoc)
                _taskPaneControl?.ClearAll();
            _hasActiveDoc = hasDoc;
            return 0;
        }

        private void SubscribeToDocumentEvents()
        {
            UnsubscribeFromDocumentEvents();
            var doc = _swApp?.ActiveDoc;
            if (doc == null) return;

            try
            {
                if (doc is PartDoc part)
                {
                    _partDocEvents = part;
                    _partDocEvents.AddCustomPropertyNotify    += OnDocCustomPropertyAdd;
                    _partDocEvents.ChangeCustomPropertyNotify += OnDocCustomPropertyChange;
                    _partDocEvents.DeleteCustomPropertyNotify += OnDocCustomPropertyDelete;
                }
                else if (doc is AssemblyDoc asm)
                {
                    _assemblyDocEvents = asm;
                    _assemblyDocEvents.AddCustomPropertyNotify    += OnDocCustomPropertyAdd;
                    _assemblyDocEvents.ChangeCustomPropertyNotify += OnDocCustomPropertyChange;
                    _assemblyDocEvents.DeleteCustomPropertyNotify += OnDocCustomPropertyDelete;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[SwInventreeAddin] SubscribeToDocumentEvents failed: {ex.Message}");
            }
        }

        private void UnsubscribeFromDocumentEvents()
        {
            if (_partDocEvents != null)
            {
                _partDocEvents.AddCustomPropertyNotify    -= OnDocCustomPropertyAdd;
                _partDocEvents.ChangeCustomPropertyNotify -= OnDocCustomPropertyChange;
                _partDocEvents.DeleteCustomPropertyNotify -= OnDocCustomPropertyDelete;
                _partDocEvents = null;
            }

            if (_assemblyDocEvents != null)
            {
                _assemblyDocEvents.AddCustomPropertyNotify    -= OnDocCustomPropertyAdd;
                _assemblyDocEvents.ChangeCustomPropertyNotify -= OnDocCustomPropertyChange;
                _assemblyDocEvents.DeleteCustomPropertyNotify -= OnDocCustomPropertyDelete;
                _assemblyDocEvents = null;
            }
        }

        private int OnDocCustomPropertyAdd(string propName, string configuration, string value, int valueType)
            { OnDocCustomPropertyChanged(); return 0; }

        private int OnDocCustomPropertyChange(string propName, string configuration, string oldValue, string newValue, int valueType)
            { OnDocCustomPropertyChanged(); return 0; }

        private int OnDocCustomPropertyDelete(string propName, string configuration, string value, int valueType)
            { OnDocCustomPropertyChanged(); return 0; }

        private void OnDocCustomPropertyChanged() => _taskPaneControl?.LoadPartNumber();

        private void OnSettingsRequested(object sender, EventArgs e)        {
            if (_configProvider == null || _mappingProvider == null)
            {
                System.Diagnostics.Trace.WriteLine("[SwInventreeAddin] OnSettingsRequested: provider not initialised — settings dialog suppressed.");
                return;
            }

            var form = new SettingsWindow(_configProvider, _mappingProvider, new AssemblyVersionInfo());
            form.MappingApplied += (_, provider) =>
            {
                _mappingProvider = provider;
                _taskPaneControl?.UpdateMapping(provider);
            };
            if (form.ShowDialog() != true) return;

            // MappingApplied already updated _mappingProvider and refreshed the task pane.
            // Only rebuild the HTTP client with the saved credentials.
            var newConfig = _configProvider.GetServerConfig();
            if (newConfig == null) return;

            _httpClient?.Dispose();
            _httpClient             = new System.Net.Http.HttpClient();
            _httpClient.BaseAddress = new System.Uri(newConfig.Url);
            var newClient = new InventreeHttpClient(_httpClient, newConfig.ApiKey);
            _taskPaneControl?.UpdateClient(newClient);
            _taskPaneControl?.UpdateWaitForAutoPartNumber(newConfig.WaitForAutoPartNumber);
        }
    }
}
