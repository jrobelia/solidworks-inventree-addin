using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class PropertyMappingEditorWindowTests
    {
        [Test]
        public void Constructor_SharedHealthyMapping_OpensEditableAndEnablesSave()
        {
            var provider = new StubPropertyMappingProvider
            {
                SourceFilePath = "C:\\shared.json",
                SourceFileExists = true,
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var window = new PropertyMappingEditorWindow(provider);

            Assert.That(window.SaveButton.IsEnabled, Is.True);
            Assert.That(window.StatusBar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.StatusBar.StatusText.Text, Is.EqualTo("No validation errors."));
        }

        [Test]
        public void Constructor_LocalHealthyMapping_EnablesSaveAndShowsDefaultStatus()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var window = new PropertyMappingEditorWindow(provider);

            Assert.That(window.SaveButton.IsEnabled, Is.True);
            Assert.That(window.StatusBar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.StatusBar.StatusText.Text, Is.EqualTo("No validation errors."));
        }

        [Test]
        public void Save_Button_WhenSaveFails_DisplaysStatusBarError()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                },
                ThrowOnSave = new InvalidOperationException("The Property Mapping file could not be saved.")
            };

            var window = new PropertyMappingEditorWindow(provider);
            window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, window.SaveButton));

            Assert.That(window.StatusBar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.StatusBar.StatusText.Text, Does.Contain("could not be saved").IgnoreCase);
            Assert.That(window.StatusBar.StatusStripe.Background, Is.SameAs(window.FindResource("BrushStatusError")));
        }

        [Test]
        public void PropertyTextBoxes_BoundToPlaceholdersAndValues()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "MyIPN",
                    NameProperty = "MyName",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var window = new PropertyMappingEditorWindow(provider);
            var vm = (MappingEditorViewModel)window.DataContext;

            AssertTextBox(window, vm.IpnPlaceholder, "MyIPN");
            AssertTextBox(window, vm.NamePlaceholder, "MyName");
        }

        [Test]
        public void BomColumnTextBoxes_MissingFields_AreEmptyAndShowPlaceholders()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = "2",
                    IpnProperty = "PartNo",
                    NameProperty = "Description",
                    NotesProperty = "Notes",
                    RevisionProperty = "Revision",
                    DescriptionProperty = "Description Long",
                    PkProperty = "InvenTree PK",
                }
            };

            var window = new PropertyMappingEditorWindow(provider);
            var vm = (MappingEditorViewModel)window.DataContext;

            AssertTextBox(window, vm.BomColumnIpnPlaceholder, "");
            AssertTextBox(window, vm.BomColumnQtyPlaceholder, "");
            AssertTextBox(window, vm.BomColumnReferencePlaceholder, "");
            AssertTextBox(window, vm.BomColumnNotePlaceholder, "");
        }

        // ── #377 design conformance ──────────────────────────────────────────

        [Test]
        public void SectionHeaders_SitInBandedHeaderBands()
        {
            var window = new PropertyMappingEditorWindow(new StubPropertyMappingProvider());

            foreach (var title in new[] { "Property Field Mappings", "BOM Column Aliases" })
            {
                LogicalTreeTestHelper.AssertBandedHeader(window, title);
            }
        }

        [Test]
        public void Body_HasNoSeparator_BandsAreTheSectionBreaks()
        {
            var window = new PropertyMappingEditorWindow(new StubPropertyMappingProvider());

            Assert.That(LogicalTreeTestHelper.FindLogicalChildren<Separator>(window).Any(), Is.False);
        }

        [Test]
        public void ColumnHeaders_UseSubtleColumnHeaderStyle()
        {
            var window = new PropertyMappingEditorWindow(new StubPropertyMappingProvider());
            var style = window.TryFindResource("SubtleColumnHeaderStyle");
            Assert.That(style, Is.Not.Null, "SubtleColumnHeaderStyle token is missing.");

            var headers = LogicalTreeTestHelper.FindLogicalChildren<TextBlock>(window)
                .Where(tb => ReferenceEquals(tb.Style, style))
                .Select(tb => tb.Text)
                .ToList();

            Assert.That(headers, Is.EquivalentTo(new[]
            {
                "InvenTree Part Property", "SolidWorks Custom Property",
                "InvenTree BOM Field", "SolidWorks BOM Column",
            }));
        }

        [Test]
        public void MappingField_Placeholder_RendersItalic()
        {
            var window = new PropertyMappingEditorWindow(new StubPropertyMappingProvider());
            var box = FindTextBoxes(window).First();
            box.ApplyTemplate();

            var placeholder = box.Template.FindName("Placeholder", box) as TextBlock;

            Assert.That(placeholder?.FontStyle, Is.EqualTo(FontStyles.Italic));
        }

        private static void AssertTextBox(DependencyObject window, string placeholder, string expectedText)
        {
            var box = FindTextBoxes(window)
                .FirstOrDefault(tb => (tb.Tag as string) == placeholder);

            Assert.That(box, Is.Not.Null, $"No TextBox found with placeholder '{placeholder}'.");
            Assert.That(box!.Text, Is.EqualTo(expectedText));
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static System.Collections.Generic.IEnumerable<TextBox> FindTextBoxes(DependencyObject parent)
        {
            return LogicalTreeTestHelper.FindLogicalChildren<TextBox>(parent);
        }
    }
}
