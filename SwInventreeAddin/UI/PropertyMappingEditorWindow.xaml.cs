using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal dialog for viewing and editing the five InvenTree → SolidWorks property-name mappings.
    /// Receives <see cref="IPropertyMappingProvider"/> and delegates all persistence to it.
    /// Opens in read-only mode when <see cref="IPropertyMappingProvider.IsReadOnly"/> is true.
    /// </summary>
    public partial class PropertyMappingEditorWindow : Window
    {
        private readonly IPropertyMappingProvider _provider;

        public PropertyMappingEditorWindow(IPropertyMappingProvider provider)
        {
            _provider = provider;
            InitializeComponent();

            var mapping = _provider.GetMapping();
            IpnPropertyBox.Text         = mapping.IpnProperty         ?? string.Empty;
            NamePropertyBox.Text        = mapping.NameProperty         ?? string.Empty;
            DescriptionPropertyBox.Text = mapping.DescriptionProperty  ?? string.Empty;
            RevisionPropertyBox.Text    = mapping.RevisionProperty     ?? string.Empty;
            NotesPropertyBox.Text       = mapping.NotesProperty        ?? string.Empty;
            PkPropertyBox.Text          = mapping.PkProperty           ?? string.Empty;

            if (_provider.IsReadOnly)
                ApplyReadOnlyMode();
        }

        // ── Read-only mode ─────────────────────────────────────────────────────

        private void ApplyReadOnlyMode()
        {
            ReadOnlyBanner.Visibility = Visibility.Visible;
            SaveButton.IsEnabled      = false;

            var greyBrush = TryFindResource("BrushSectionHeader") as Brush
                            ?? SystemColors.ControlBrush;
            foreach (var box in MappingBoxes())
            {
                box.IsReadOnly = true;
                box.Background = greyBrush;
            }
        }

        // ── Save ───────────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var names = MappingBoxes()
                .Select(b => b.Text.Trim())
                .ToList();

            if (HasDuplicatePropertyNames(names))
            {
                ErrorText.Text =
                    "Two or more fields share the same SolidWorks property name. Each name must be unique.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                _provider.SaveMapping(new PropertyMappingConfig
                {
                    SchemaVersion       = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty         = IpnPropertyBox.Text.Trim(),
                    NameProperty        = NamePropertyBox.Text.Trim(),
                    DescriptionProperty = DescriptionPropertyBox.Text.Trim(),
                    RevisionProperty    = RevisionPropertyBox.Text.Trim(),
                    NotesProperty       = NotesPropertyBox.Text.Trim(),
                    PkProperty          = PkPropertyBox.Text.Trim(),
                });
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Could not save mapping: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
        }

        // ── Cancel ─────────────────────────────────────────────────────────────

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private IEnumerable<TextBox> MappingBoxes() =>
            new[] { IpnPropertyBox, NamePropertyBox, DescriptionPropertyBox,
                    RevisionPropertyBox, NotesPropertyBox, PkPropertyBox };

        /// <summary>
        /// Returns true when any two non-blank names in <paramref name="names"/> are
        /// equal (case-insensitive). Blank/whitespace entries are ignored (unmapped fields).
        /// Public so unit tests in a separate assembly can call it without STA.
        /// </summary>
        public static bool HasDuplicatePropertyNames(IEnumerable<string> names)
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            return names.Where(n => !string.IsNullOrWhiteSpace(n))
                        .Any(n => !seen.Add(n));
        }
    }
}
