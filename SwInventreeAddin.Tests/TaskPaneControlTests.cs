using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests the logic of TaskPaneControl in isolation.
    /// WinForms requires an STA thread; NUnit honours the [Apartment] attribute.
    /// All dependencies are replaced by hand-written stubs  no real SolidWorks
    /// process and no real InvenTree server is needed.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class TaskPaneControlTests
    {
        private StubInventreeClient _client;
        private StubDocumentPropertyService _propertyService;
        private TaskPaneControl _control;

        private static readonly InventreePart SamplePart = new InventreePart
        {
            Pk       = 42,
            Name     = "Resistor 10k",
            Notes    = "SMD 0402",
            Revision = "A",
            Ipn      = "R-10K-0402"
        };

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
        }

        [TearDown]
        public void TearDown()
        {
            _control?.Dispose();
        }

        private void CreateControl(string seedPartNo = "R-10K-0402")
        {
            _propertyService.Seed("PartNo", seedPartNo);
            _control = new TaskPaneControl(_client, _propertyService);
        }

        // --- initialisation ---

        [Test]
        public void OnInitialisation_PartNumberTextBox_IsPopulatedFromCustomProperty()
        {
            CreateControl("R-10K-0402");

            Assert.That(_control.PartNumberTextBox.Text, Is.EqualTo("R-10K-0402"));
        }

        // --- after a successful fetch ---

        [Test]
        public async Task AfterSuccessfulFetch_NamePreviewField_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.NamePreviewTextBox.Text, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_NotesPreviewField_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.NotesPreviewTextBox.Text, Is.EqualTo("SMD 0402"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_RevisionPreviewField_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.RevisionPreviewTextBox.Text, Is.EqualTo("A"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_ApplyButton_BecomesEnabled()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.ApplyButton.Enabled, Is.True);
        }

        // --- after clicking Apply ---

        [Test]
        public async Task AfterApply_SetsDescriptionToPartName()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            _control.ApplyToDocument();

            Assert.That(_propertyService.GetCustomProperty("Description"),
                Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task AfterApply_SetsNotesToPartNotes()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            _control.ApplyToDocument();

            Assert.That(_propertyService.GetCustomProperty("Notes"),
                Is.EqualTo("SMD 0402"));
        }

        [Test]
        public async Task AfterApply_NeverWritesPartNoProperty()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            _control.ApplyToDocument();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain("PartNo"),
                "The IPN (PartNo) field must never be written back by Apply");
        }

        [Test]
        public async Task AfterApply_NeverWritesRevisionProperty()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            _control.ApplyToDocument();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain("Revision"),
                "Revision is display-only and must never be written by Apply");
        }

        // --- part not found ---

        [Test]
        public async Task WhenPartNotFound_ApplyButton_RemainsDisabled()
        {
            _client.PartToReturn = null;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.ApplyButton.Enabled, Is.False);
        }

        [Test]
        public async Task WhenPartNotFound_StatusLabel_ShowsNotFoundMessage()
        {
            _client.PartToReturn = null;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.StatusLabel.Text,
                Does.Contain("not found").IgnoreCase
                .Or.Contain("no part").IgnoreCase,
                "Status label must indicate that the part was not found");
        }

        // --- PushRevisionButton state ---

        [Test]
        public void PushRevisionButton_IsDisabledOnInitialisation()
        {
            CreateControl();

            Assert.That(_control.PushRevisionButton.Enabled, Is.False);
        }

        [Test]
        public async Task PushRevisionButton_IsEnabledAfterSuccessfulFetch()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();

            await _control.FetchPartAsync();

            Assert.That(_control.PushRevisionButton.Enabled, Is.True);
        }

        [Test]
        public async Task PushRevisionButton_IsDisabledAfterClearAll()
        {
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            _control.ClearAll();

            Assert.That(_control.PushRevisionButton.Enabled, Is.False);
        }

        // --- PushRevisionToInventreeAsync behaviour ---

        [Test]
        public async Task PushRevision_WhenNoPartFetched_DoesNotCallClient()
        {
            // No fetch — _lastFetchedPart is null
            CreateControl();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_client.LastPushedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushRevision_ReadsSWRevisionCustomProperty()
        {
            _propertyService.Seed("Revision", "B");
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_client.LastPushedRevision, Is.EqualTo("B"));
        }

        [Test]
        public async Task PushRevision_CallsClientWithCorrectPkAndRevision()
        {
            _propertyService.Seed("Revision", "C");
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_client.LastPushedPk,       Is.EqualTo(42));
            Assert.That(_client.LastPushedRevision, Is.EqualTo("C"));
        }

        [Test]
        public async Task PushRevision_OnSuccess_UpdatesRevisionPreviewTextBox()
        {
            _propertyService.Seed("Revision", "D");
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_control.RevisionPreviewTextBox.Text, Is.EqualTo("D"));
        }

        [Test]
        public async Task PushRevision_OnSuccess_ShowsSuccessInStatusLabel()
        {
            _propertyService.Seed("Revision", "E");
            _client.PartToReturn = SamplePart;
            CreateControl();
            await _control.FetchPartAsync();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_control.StatusLabel.Text,
                Does.Contain("pushed").IgnoreCase
                .Or.Contain("\u2713"));
        }

        [Test]
        public async Task PushRevision_OnHttpError_ShowsErrorInStatusLabel()
        {
            _client.PartToReturn = SamplePart;
            _client.ThrowOnUpdate = new System.Net.Http.HttpRequestException("InvenTree returned 500");
            CreateControl();
            await _control.FetchPartAsync();

            await _control.PushRevisionToInventreeAsync();

            Assert.That(_control.StatusLabel.Text, Does.Contain("Error").IgnoreCase);
        }
    }
}
