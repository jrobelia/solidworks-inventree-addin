using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Code-behind for TaskPaneView.xaml.
    /// All business logic lives in TaskPaneViewModel — this file is purely
    /// wiring: route button clicks and keep the status-stripe colour in sync.
    /// </summary>
    public partial class TaskPaneView : UserControl
    {
        private TaskPaneViewModel? _vm;

        public TaskPaneView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // ── DataContext wiring ─────────────────────────────────────────────

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= Vm_PropertyChanged;

            _vm = DataContext as TaskPaneViewModel;

            if (_vm != null)
                _vm.PropertyChanged += Vm_PropertyChanged;

            UpdateStatusStripe();
        }

        private void Vm_PropertyChanged(object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TaskPaneViewModel.StatusSeverity))
                UpdateStatusStripe();
        }

        private void UpdateStatusStripe()
        {
            if (StatusStripe == null || _vm == null) return;

            var brushKey = _vm.StatusSeverity switch
            {
                StatusSeverity.Success => "BrushStatusSuccess",
                StatusSeverity.Warning => "BrushStatusWarning",
                StatusSeverity.Error   => "BrushStatusError",
                _                      => "BrushStatusNone",
            };

            var iconGlyph = _vm.StatusSeverity switch
            {
                StatusSeverity.Success => "\uE73E",
                StatusSeverity.Warning => "\uE7BA",
                StatusSeverity.Error   => "\uE783",
                _                      => "",
            };

            var brush = (Brush)FindResource(brushKey);
            StatusStripe.Background = brush;

            if (StatusIcon != null)
            {
                StatusIcon.Text       = iconGlyph;
                StatusIcon.Foreground = brush;
                StatusIcon.Visibility = iconGlyph.Length > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        // ── Button click handlers ──────────────────────────────────────────

        private void Settings_Click(object sender, RoutedEventArgs e) =>
            _vm?.RequestSettings();

        private void Fetch_Click(object sender, RoutedEventArgs e) =>
            _ = _vm?.FetchPartAsync();

        private void CreatePart_Click(object sender, RoutedEventArgs e)
        {
            _vm?.OpenCreatePartWindow(vm =>
            {
                var window = new CreatePartWindow();
                window.Initialise(vm);
                window.ShowDialog();
            });
        }

        private void ApplyName_Click(object sender, RoutedEventArgs e) =>
            _vm?.ApplyNameToDocument();

        private void ApplyNotes_Click(object sender, RoutedEventArgs e) =>
            _vm?.ApplyNotesToDocument();

        private void PushRevision_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PushRevisionConfirmDialog(
                "Push the SolidWorks revision to InvenTree? This will overwrite the revision currently stored in InvenTree.",
                imageCheckedByDefault: true);

            if (dlg.ShowDialog() != true) return;

            _ = _vm?.PushRevisionToInventreeAsync();

            if (dlg.IncludeImage)
                _ = _vm?.PushImageAsync();
        }

        private void PushName_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PushRevisionConfirmDialog(
                "Push the SolidWorks name/description to InvenTree? This will overwrite the name currently stored in InvenTree.",
                imageCheckedByDefault: false);

            if (dlg.ShowDialog() != true) return;

            _ = _vm?.PushNameToInvenTreeAsync();

            if (dlg.IncludeImage)
                _ = _vm?.PushImageAsync();
        }

        private void PushNotes_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PushRevisionConfirmDialog(
                "Push the SolidWorks notes to InvenTree? This will overwrite the notes currently stored in InvenTree.",
                imageCheckedByDefault: false);

            if (dlg.ShowDialog() != true) return;

            _ = _vm?.PushNotesToInvenTreeAsync();

            if (dlg.IncludeImage)
                _ = _vm?.PushImageAsync();
        }

        private void PushImage_Click(object sender, RoutedEventArgs e) =>
            _ = _vm?.PushImageAsync();
    }
}
