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
        public void Constructor_SharedHealthyMapping_ShowsReadOnlyBannerAndDisablesSave()
        {
            var provider = new StubPropertyMappingProvider
            {
                IsReadOnly = true,
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

            Assert.That(window.ReadOnlyBanner.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.SaveButton.IsEnabled, Is.False);
        }

        [Test]
        public void Constructor_LocalHealthyMapping_EnablesSaveAndHidesBanners()
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
            Assert.That(window.ReadOnlyBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
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
            return FindLogicalChildren<TextBox>(parent);
        }

        private static System.Collections.Generic.IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is T typed)
                    yield return typed;

                foreach (var descendant in FindLogicalChildren<T>(child))
                    yield return descendant;
            }
        }
    }
}
