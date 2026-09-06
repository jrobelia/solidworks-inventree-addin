using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SwInventreeAddin.UI
{
    public partial class BomCompareWindow : Window
    {
        private readonly BomCompareViewModel _vm;

        // This window's own HWND — pop-ups it spawns are owned by and centered
        // over it, not the SolidWorks main window.
        private IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

        public BomCompareWindow(BomCompareViewModel vm, string assemblyIpn, string partName = "",
                                 string bomTableName = "")
        {
            InitializeComponent();
            _vm = vm;

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());

            _vm.ConfirmPush = (newCount, conflictCount) =>
                MessageDialog.ShowYesNo(
                    WindowHandle,
                    $"Push {newCount} new line(s) and update {conflictCount} conflict(s) to InvenTree?",
                    "Confirm BOM Push",
                    System.Windows.Forms.MessageBoxIcon.Question) == MessageDialogResult.Yes;

            DataContext = _vm;
            AssemblyIpn.Text  = assemblyIpn;
            AssemblyName.Text = partName;

            BomTableName.Text = string.IsNullOrEmpty(bomTableName) ? "" : $"BOM Table: {bomTableName}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.LoadAsync();
            }
            catch (Exception ex)
            {
                MessageDialog.ShowOK(
                    WindowHandle,
                    $"Failed to load BOM data:{System.Environment.NewLine}{ex.Message}",
                    "BOM Load Error",
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in _vm.Lines.Where(l => l.CanCheck))
                line.IsChecked = true;
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in _vm.Lines)
                line.IsChecked = false;
        }

        private void SelectNew_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in _vm.Lines)
                line.IsChecked = line.CanCheck && line.State == SwInventreeAddin.Bom.BomDiffState.New;
        }

        private void SelectConflicts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var line in _vm.Lines)
                line.IsChecked = line.CanCheck && line.State == SwInventreeAddin.Bom.BomDiffState.Conflict;
        }

        private async void Push_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.PushAsync();
            }
            catch (Exception ex)
            {
                MessageDialog.ShowOK(
                    WindowHandle,
                    $"Failed to push BOM:{System.Environment.NewLine}{ex.Message}",
                    "BOM Push Error",
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Synchronise the group-header overlay widths with the DataGrid's actual column widths.
        // Columns: 0-2 = left zone (grey), 3-5 = SolidWorks (blue), 6-9 = InvenTree (yellow).
        private void BomGrid_LayoutUpdated(object sender, EventArgs e)
        {
            var cols = BomGrid.Columns;
            if (cols.Count < 10) return;

            double left = cols[0].ActualWidth + cols[1].ActualWidth + cols[2].ActualWidth;
            double sw   = cols[3].ActualWidth + cols[4].ActualWidth + cols[5].ActualWidth;
            double it   = cols[6].ActualWidth + cols[7].ActualWidth + cols[8].ActualWidth + cols[9].ActualWidth;

            if (left <= 0 || sw <= 0 || it <= 0) return;

            GhColLeft.Width = new GridLength(left);
            GhColSw.Width   = new GridLength(sw);
            GhColIt.Width   = new GridLength(it);

            GroupHeaderGrid.Width = left + sw + it;
        }
    }
}
