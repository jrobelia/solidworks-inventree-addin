using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
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



        [Test]
        public void Constructor_AssemblyRevision_ShowsRevisionWithoutLiteralRevLabel()
        {
            var client = new StubInventreeClient();
            var bomService = new StubAssemblyBomService();
            var mapping = PropertyMappingConfig.WithDefaults();
            var vm = new BomCompareViewModel(client, bomService, mapping, 1, "inventree");
            var window = new BomCompareWindow(vm, "TEST-001", "Test Assembly", assemblyRevision: "C");

            Assert.That(window.AssemblyRevision.Text, Is.EqualTo("C"));
            Assert.That(window.AssemblyRevision.Text, Does.Not.Contain("Rev"));
        }

        [Test]
        public void Columns_MatchConfirmedIssue403Order()
        {
            var client = new StubInventreeClient();
            var bomService = new StubAssemblyBomService();
            var mapping = PropertyMappingConfig.WithDefaults();
            var vm = new BomCompareViewModel(client, bomService, mapping, 1, "inventree");
            var window = new BomCompareWindow(vm, "TEST-001", "Test Assembly");
            var grid = (System.Windows.Controls.DataGrid)LogicalTreeHelper.FindLogicalNode(window, "BomGrid")!;

            var headers = grid.Columns.Select(c => c.Header?.ToString() ?? string.Empty).ToArray();

            Assert.That(headers, Is.EqualTo(new[]
            {
                "", "State", "IPN", "SW Rev", "SW Qty", "SW Ref.", "SW Note",
                "Inv Rev", "Inv Qty", "Inv Ref.", "Inv Note", "Substitute Parts"
            }));
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
