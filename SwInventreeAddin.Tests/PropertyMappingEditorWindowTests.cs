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
        public void Constructor_SharedOlderMapping_ShowsCopyToLocalAndDisablesSave()
        {
            var provider = new StubPropertyMappingProvider
            {
                IsReadOnly = true,
                Config = new PropertyMappingConfig { SchemaVersion = "2" }
            };

            var window = new PropertyMappingEditorWindow(provider);

            Assert.That(window.CopyToLocalPanel.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.CopyToLocalButton.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.ReadOnlyBanner.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(window.SaveButton.IsEnabled, Is.False);
        }

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
            Assert.That(window.CopyToLocalPanel.Visibility, Is.EqualTo(Visibility.Collapsed));
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
            Assert.That(window.CopyToLocalPanel.Visibility, Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void CopyToLocal_WhenSharedOlder_HidesButtonAndShowsInstruction()
        {
            var provider = new StubPropertyMappingProvider
            {
                IsReadOnly = true,
                Config = new PropertyMappingConfig { SchemaVersion = "2" }
            };

            var window = new PropertyMappingEditorWindow(provider);

            window.CopyToLocalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, window.CopyToLocalButton));

            Assert.That(window.CopyToLocalButton.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(window.CopyToLocalInstructionText.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.CopyToLocalInstructionText.Text, Does.Contain("Local in Settings").IgnoreCase);
            Assert.That(provider.CopyToLocalCalled, Is.True);
        }

        [Test]
        public void IpnTextBox_BoundToIpnPlaceholder()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                    IpnProperty = "MyIPN",
                    BomColumnIpn = "IPN",
                    BomColumnQty = "Qty",
                    BomColumnReference = "Reference",
                    BomColumnNote = "Note",
                }
            };

            var window = new PropertyMappingEditorWindow(provider);
            var vm = (MappingEditorViewModel)window.DataContext;

            var ipnBox = FindTextBoxes(window)
                .FirstOrDefault(tb => (tb.Tag as string) == vm.IpnPlaceholder);

            Assert.That(ipnBox, Is.Not.Null);
            Assert.That(ipnBox!.Text, Is.EqualTo("MyIPN"));
        }

        [Test]
        public void BomIpnTextBox_MissingField_IsEmptyAndHasPlaceholderText()
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

            var bomIpnBox = FindTextBoxes(window)
                .FirstOrDefault(tb => (tb.Tag as string) == vm.BomColumnIpnPlaceholder && string.IsNullOrEmpty(tb.Text));

            Assert.That(bomIpnBox, Is.Not.Null);
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
