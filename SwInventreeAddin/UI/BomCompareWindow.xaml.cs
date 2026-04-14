using System;
using System.Linq;
using System.Windows;

namespace SwInventreeAddin.UI
{
    public partial class BomCompareWindow : Window
    {
        private readonly BomCompareViewModel _vm;

        public BomCompareWindow(BomCompareViewModel vm, string assemblyLabel)
        {
            InitializeComponent();
            _vm = vm;

            _vm.ConfirmPush = (newCount, conflictCount) =>
                MessageBox.Show(
                    $"Push {newCount} new line(s) and update {conflictCount} conflict(s) to InvenTree?",
                    "Confirm BOM Push",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;

            DataContext = _vm;
            AssemblyLabel.Text = assemblyLabel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load BOM data:{System.Environment.NewLine}{ex.Message}",
                    "BOM Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _vm.ApplyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to push BOM:{System.Environment.NewLine}{ex.Message}",
                    "BOM Push Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
