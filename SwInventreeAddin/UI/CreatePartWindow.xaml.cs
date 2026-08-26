using System;
using System.Windows;
using System.Windows.Controls;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Dialog that lets the user choose a category and create a new InvenTree part.
    /// Call <see cref="Initialise"/> before showing the window.
    /// </summary>
    public partial class CreatePartWindow : Window
    {
        private CreatePartViewModel? _vm;

        public CreatePartWindow()
        {
            InitializeComponent();

            WindowCentering.Attach(this, SolidWorksWindowHandle.Get());
        }

        /// <summary>
        /// Binds the window to a ViewModel and wires up event subscriptions.
        /// Must be called once before <see cref="Window.ShowDialog"/>.
        /// </summary>
        public void Initialise(CreatePartViewModel vm)
        {
            _vm              = vm;
            DataContext      = vm;
            PartNameBox.Text = vm.PartName;

            // Two-way text binding wired in code so the XAML stays clean.
            PartNameBox.TextChanged += (_, __) => vm.PartName = PartNameBox.Text;
            IpnEntryBox.TextChanged += (_, __) => vm.IpnEntry = IpnEntryBox.Text;

            // Close the dialog with a success result as soon as the part is created.
            vm.PartCreated += OnPartCreated;

            // Mirror StatusText onto the status bar.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CreatePartViewModel.StatusText))
                    StatusTextBlock.Text = vm.StatusText;
            };

            // Kick off the initial category load.
            _ = vm.LoadRootCategoriesAsync();
        }

        private void OnPartCreated(object sender, InventreePart part)
        {
            DialogResult = true;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            await _vm.CreateAsync();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CategoryTree_SelectedItemChanged(
            object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_vm == null) return;
            _vm.SelectedCategory = e.NewValue as CategoryNode;
            SelectedCategoryLabel.Text = _vm.SelectedCategory?.Category.Name ?? "(none)";
        }

        private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            if ((sender as TreeViewItem)?.DataContext is CategoryNode node)
                await _vm.LoadChildrenAsync(node);
        }
    }
}
