using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal dialog for viewing and editing the InvenTree → SolidWorks property-name mappings.
    /// Receives <see cref="IPropertyMappingProvider"/> and delegates all persistence to the
    /// <see cref="MappingEditorViewModel"/>.
    /// </summary>
    public partial class PropertyMappingEditorWindow : Window
    {
        private readonly MappingEditorViewModel _viewModel;

        /// <summary>
        /// Creates the mapping editor.
        /// </summary>
        /// <param name="provider">The mapping provider.</param>
        /// <param name="ownerWindow">The parent window. If null, the SolidWorks main window is used.</param>
        public PropertyMappingEditorWindow(IPropertyMappingProvider provider, Window? ownerWindow = null)
        {
            _viewModel = new MappingEditorViewModel(provider);
            DataContext = _viewModel;

            InitializeComponent();

            Owner = ownerWindow;

            var ownerHandle = ownerWindow != null
                ? new WindowInteropHelper(ownerWindow).Handle
                : SolidWorksWindowHandle.Get();
            WindowCentering.Attach(this, ownerHandle);

            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MappingEditorViewModel.ErrorMessage))
                    RefreshErrorText();
                else if (e.PropertyName == nameof(MappingEditorViewModel.CopyToLocalInstruction))
                    RefreshCopyToLocalInstruction();
            };

            ApplyReadOnlyState();
        }

        // ── Read-only / copy-to-local state ────────────────────────────────────

        private void ApplyReadOnlyState()
        {
            if (_viewModel.IsReadOnly)
            {
                SaveButton.IsEnabled = false;
                SetReadOnlyBackground();

                var copyVisible = _viewModel.CanCopyToLocal ||
                                  !string.IsNullOrEmpty(_viewModel.CopyToLocalInstruction);

                if (copyVisible)
                {
                    CopyToLocalPanel.Visibility = Visibility.Visible;
                    CopyToLocalButton.Visibility = _viewModel.CanCopyToLocal
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    ReadOnlyBanner.Visibility   = Visibility.Collapsed;
                }
                else
                {
                    CopyToLocalPanel.Visibility = Visibility.Collapsed;
                    ReadOnlyBanner.Visibility   = Visibility.Visible;
                    ReadOnlyBannerText.Text     =
                        "Loaded from a shared file — switch to Local in Settings to edit mappings.";
                }
            }
            else
            {
                SaveButton.IsEnabled        = true;
                CopyToLocalPanel.Visibility = Visibility.Collapsed;
                ReadOnlyBanner.Visibility   = Visibility.Collapsed;
            }

            RefreshErrorText();
            RefreshCopyToLocalInstruction();
        }

        private void SetReadOnlyBackground()
        {
            var greyBrush = TryFindResource("BrushSectionHeader") as Brush
                            ?? SystemColors.ControlBrush;

            foreach (var box in FindTextBoxesInTemplate())
                box.Background = greyBrush;
        }

        private IEnumerable<TextBox> FindTextBoxesInTemplate()
        {
            return LogicalTreeHelper.GetChildren(this)
                .OfType<DependencyObject>()
                .SelectMany(FindVisualChildren<TextBox>);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    yield return typed;

                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        // ── Save ───────────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Save())
                DialogResult = true;
        }

        // ── Cancel ─────────────────────────────────────────────────────────────

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Cancel();
            DialogResult = false;
        }

        // ── Copy to local ──────────────────────────────────────────────────────

        private void CopyToLocal_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CopyToLocal();
            ApplyReadOnlyState();
        }

        // ── UI refresh ─────────────────────────────────────────────────────────

        private void RefreshErrorText()
        {
            if (string.IsNullOrEmpty(_viewModel.ErrorMessage))
            {
                ErrorText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text       = _viewModel.ErrorMessage;
                ErrorText.Visibility = Visibility.Visible;
            }
        }

        private void RefreshCopyToLocalInstruction()
        {
            if (string.IsNullOrEmpty(_viewModel.CopyToLocalInstruction))
            {
                CopyToLocalInstructionText.Visibility = Visibility.Collapsed;
            }
            else
            {
                CopyToLocalInstructionText.Text       = _viewModel.CopyToLocalInstruction;
                CopyToLocalInstructionText.Visibility = Visibility.Visible;
            }
        }

    }
}
