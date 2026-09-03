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
            };

            RefreshErrorText();
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

        // ── UI refresh ─────────────────────────────────────────────────────────

        private void RefreshErrorText()
        {
            if (string.IsNullOrEmpty(_viewModel.ErrorMessage))
            {
                ErrorTextBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorText.Text          = _viewModel.ErrorMessage;
                ErrorTextBar.Visibility = Visibility.Visible;
            }
        }

    }
}
