using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class CreatePartWindowTests
    {
        [Test]
        public void Header_CreatePart_SitsInBandedHeaderBand()
        {
            var window = CreateWindow();

            LogicalTreeTestHelper.AssertBandedHeader(window, "Create InvenTree Part");
        }

        [Test]
        public void InputFields_CreatePart_UseSWFieldStyle()
        {
            var window = CreateWindow();
            var style = window.TryFindResource("SWFieldStyle");

            Assert.That(style, Is.Not.Null, "SWFieldStyle token is missing.");
            Assert.That(window.PartNameBox.Style, Is.SameAs(style));
            Assert.That(window.IpnEntryBox.Style, Is.SameAs(style));
        }

        [Test]
        public void FieldLabels_CreatePart_UseFieldLabelStyle()
        {
            var window = CreateWindow();
            var style = window.TryFindResource("FieldLabelStyle");
            Assert.That(style, Is.Not.Null, "FieldLabelStyle token is missing.");

            foreach (var labelText in new[] { "Part Name", "IPN (optional)", "Category", "InvenTree Flags" })
            {
                var label = LogicalTreeTestHelper.FindLogicalChildren<TextBlock>(window)
                    .FirstOrDefault(tb => tb.Text == labelText);
                Assert.That(label, Is.Not.Null, $"Could not find label '{labelText}'.");
                Assert.That(label!.Style, Is.SameAs(style), $"'{labelText}' must use FieldLabelStyle.");
            }
        }

        [Test]
        public void IpnPlaceholder_CreatePart_UsesApprovedAutoAssignWording()
        {
            var window = CreateWindow();

            var placeholder = LogicalTreeTestHelper.FindLogicalChildren<TextBlock>(window)
                .FirstOrDefault(tb => tb.Text == "(leave blank for auto-assign)");

            Assert.That(placeholder, Is.Not.Null, "Approved IPN placeholder wording is missing.");
        }

        [Test]
        public void ButtonRow_CreatePart_RightAlignsButtons()
        {
            var window = CreateWindow();
            var createButton = window.CreateButton;
            var buttonGrid = (Grid)createButton.Parent;
            var cancelButton = buttonGrid.Children.OfType<Button>().Single(b => !ReferenceEquals(b, createButton));

            Assert.That(buttonGrid.ColumnDefinitions.Select(c => c.Width), Is.EqualTo(new[]
            {
                new GridLength(1, GridUnitType.Star),
                new GridLength(8),
                GridLength.Auto,
                new GridLength(8),
                GridLength.Auto,
            }));
            Assert.That(Grid.GetColumn(createButton), Is.EqualTo(2));
            Assert.That(Grid.GetColumn(cancelButton), Is.EqualTo(4));
            Assert.That(createButton.Width, Is.EqualTo(80));
            Assert.That(cancelButton.Width, Is.EqualTo(80));
        }

        [Test]
        public void StatusStrip_CreatePart_UsesSharedStatusBarControl()
        {
            var client = new StubInventreeClient { ThrowOnGetCategories = true };
            var window = CreateWindow(client);

            var statusBar = LogicalTreeTestHelper.FindLogicalChildren<StatusBarControl>(window).SingleOrDefault();

            Assert.That(statusBar, Is.Not.Null, "Create Part must use the shared status strip.");
            Assert.That(statusBar!.StatusText.Text, Does.StartWith("Error loading categories:"));
            Assert.That(statusBar.StatusText.ToolTip, Is.EqualTo(statusBar.StatusText.Text));
            Assert.That(statusBar.StatusStripe.Background, Is.SameAs(window.FindResource("BrushStatusError")));
        }

        private static CreatePartWindow CreateWindow()
            => CreateWindow(new StubInventreeClient());

        private static CreatePartWindow CreateWindow(StubInventreeClient client)
        {
            var vm = new CreatePartViewModel(
                client,
                new StubCreatePartValidationErrorService(),
                "Bracket, Front",
                new StubPropertyMappingProvider(),
                documentType: DocumentType.Part);
            var window = new CreatePartWindow();
            window.Initialise(vm);
            return window;
        }
    }
}
