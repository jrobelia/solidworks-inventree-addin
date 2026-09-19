using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class BomCompareWindowTests
    {
        private SynchronizationContext? _previousContext;

        [SetUp]
        public void SetUp()
        {
            SolidWorksWindowHandle.Set(IntPtr.Zero);
            _previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        }

        [TearDown]
        public void TearDown()
        {
            SolidWorksWindowHandle.Set(IntPtr.Zero);
            SynchronizationContext.SetSynchronizationContext(_previousContext);
        }

        [Test, Timeout(10000)]
        public void ShowDialog_CentersOnOwnerWindow()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();

            SolidWorksWindowHandle.Set(form.Handle);

            var client = new StubInventreeClient();
            var bomService = new StubAssemblyBomService
            {
                LinesToReturn = new List<Bom.SwBomLine>(),
            };
            var mapping = PropertyMappingConfig.WithDefaults();
            var vm = new BomCompareViewModel(client, bomService, mapping, 1, "inventree");
            var dialog = new BomCompareWindow(vm, "TEST-001", "Test Assembly");
            var wait = HiddenTestWindow.WaitForCenteredOnOwnerAsync(dialog, form.Handle);

            dialog.ShowDialog();
            wait.GetAwaiter().GetResult();
        }
    }
}
