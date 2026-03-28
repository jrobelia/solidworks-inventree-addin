using System.Drawing;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for TaskPaneViewModel — the business logic layer of the task pane.
    /// No WinForms, no STA thread requirement, no UI controls to create or dispose.
    /// </summary>
    [TestFixture]
    public class TaskPaneViewModelTests
    {
        private StubInventreeClient          _client;
        private StubDocumentPropertyService  _propertyService;
        private TaskPaneViewModel            _vm;

        private static readonly InventreePart SamplePart = new InventreePart
        {
            Pk       = 42,
            Name     = "Resistor 10k",
            Notes    = "SMD 0402",
            Revision = "A",
            Ipn      = "R-10K-0402",
        };

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
        }

        private void CreateVm(string seedPartNo = "R-10K-0402")
        {
            _propertyService.Seed("PartNo", seedPartNo);
            _vm = new TaskPaneViewModel(_client, _propertyService);
        }

        // ── Initialisation ─────────────────────────────────────────────────────

        [Test]
        public void OnInitialisation_PartNumber_IsPopulatedFromCustomProperty()
        {
            CreateVm("R-10K-0402");

            Assert.That(_vm.PartNumber, Is.EqualTo("R-10K-0402"));
        }

        [Test]
        public void OnInitialisation_ApplyEnabled_IsFalse()
        {
            CreateVm();

            Assert.That(_vm.ApplyEnabled, Is.False);
        }

        [Test]
        public void OnInitialisation_PushRevisionVisible_IsFalse()
        {
            CreateVm();

            Assert.That(_vm.PushRevisionVisible, Is.False);
        }

        [Test]
        public void OnInitialisation_PushImageVisible_IsFalse()
        {
            CreateVm();

            Assert.That(_vm.PushImageVisible, Is.False);
        }

        [Test]
        public void OnInitialisation_WithNoClient_FetchEnabled_IsFalse()
        {
            _propertyService.Seed("PartNo", "R-10K-0402");
            _vm = new TaskPaneViewModel(null, _propertyService);

            Assert.That(_vm.FetchEnabled, Is.False);
        }

        [Test]
        public void OnInitialisation_WithClient_FetchEnabled_IsTrue()
        {
            CreateVm();

            Assert.That(_vm.FetchEnabled, Is.True);
        }

        // ── After successful fetch ─────────────────────────────────────────────

        [Test]
        public async Task AfterSuccessfulFetch_NamePreview_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.NamePreview, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_NotesPreview_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.NotesPreview, Is.EqualTo("SMD 0402"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_RevisionPreview_IsPopulated()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.RevisionPreview, Is.EqualTo("A"));
        }

        [Test]
        public async Task AfterSuccessfulFetch_ApplyEnabled_IsTrue()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.ApplyEnabled, Is.True);
        }

        [Test]
        public async Task AfterSuccessfulFetch_PushRevisionVisible_IsTrue()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.PushRevisionVisible, Is.True);
        }

        [Test]
        public async Task AfterSuccessfulFetch_PushImageVisible_IsTrue()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.PushImageVisible, Is.True);
        }

        [Test]
        public async Task AfterSuccessfulFetch_PropertiesSectionVisible_IsTrue()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.PropertiesSectionVisible, Is.True);
        }

        [Test]
        public async Task AfterSuccessfulFetch_StatusText_IsEmpty()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.StatusText, Is.Empty);
        }

        // ── Part not found ─────────────────────────────────────────────────────

        [Test]
        public async Task WhenPartNotFound_ApplyEnabled_RemainsDisabled()
        {
            _client.PartToReturn = null;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.ApplyEnabled, Is.False);
        }

        [Test]
        public async Task WhenPartNotFound_StatusText_ShowsNotFoundMessage()
        {
            _client.PartToReturn = null;
            CreateVm();

            await _vm.FetchPartAsync();

            Assert.That(_vm.StatusText,
                Does.Contain("not found").IgnoreCase
                .Or.Contain("no part").IgnoreCase);
        }

        // ── Fetch error ────────────────────────────────────────────────────────

        [Test]
        public async Task WhenFetchThrows_StatusText_ShowsError()
        {
            _propertyService.Seed("PartNo", "R-10K-0402");
            _vm = new TaskPaneViewModel(new ThrowingStubClient(), _propertyService);

            await _vm.FetchPartAsync();

            Assert.That(_vm.StatusText, Does.Contain("Error").IgnoreCase);
        }

        // ── ClearAll ──────────────────────────────────────────────────────────

        [Test]
        public async Task AfterClearAll_PushRevisionVisible_IsFalse()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ClearAll();

            Assert.That(_vm.PushRevisionVisible, Is.False);
        }

        [Test]
        public async Task AfterClearAll_PushImageVisible_IsFalse()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ClearAll();

            Assert.That(_vm.PushImageVisible, Is.False);
        }

        [Test]
        public async Task AfterClearAll_ApplyEnabled_IsFalse()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ClearAll();

            Assert.That(_vm.ApplyEnabled, Is.False);
        }

        [Test]
        public async Task AfterClearAll_PartNumber_IsEmpty()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ClearAll();

            Assert.That(_vm.PartNumber, Is.Empty);
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        [Test]
        public async Task ApplyToDocument_SetsDescriptionToPartName()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyToDocument();

            Assert.That(_propertyService.GetCustomProperty("Description"),
                Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task ApplyToDocument_SetsNotesToPartNotes()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyToDocument();

            Assert.That(_propertyService.GetCustomProperty("Notes"),
                Is.EqualTo("SMD 0402"));
        }

        [Test]
        public async Task ApplyToDocument_NeverWritesPartNoProperty()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyToDocument();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain("PartNo"));
        }

        [Test]
        public async Task ApplyToDocument_NeverWritesRevisionProperty()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyToDocument();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain("Revision"));
        }

        [Test]
        public async Task ApplyNameToDocument_SetsDescription()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyNameToDocument();

            Assert.That(_propertyService.GetCustomProperty("Description"),
                Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task ApplyNotesToDocument_SetsNotes()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            _vm.ApplyNotesToDocument();

            Assert.That(_propertyService.GetCustomProperty("Notes"),
                Is.EqualTo("SMD 0402"));
        }

        // ── PushRevision ──────────────────────────────────────────────────────

        [Test]
        public async Task PushRevision_WhenNoPartFetched_DoesNotCallClient()
        {
            CreateVm();

            await _vm.PushRevisionToInventreeAsync();

            Assert.That(_client.LastPushedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushRevision_CallsClientWithCorrectPkAndRevision()
        {
            _propertyService.Seed("Revision", "C");
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            await _vm.PushRevisionToInventreeAsync();

            Assert.That(_client.LastPushedPk,       Is.EqualTo(42));
            Assert.That(_client.LastPushedRevision,  Is.EqualTo("C"));
        }

        [Test]
        public async Task PushRevision_OnSuccess_UpdatesRevisionPreview()
        {
            _propertyService.Seed("Revision", "D");
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            await _vm.PushRevisionToInventreeAsync();

            Assert.That(_vm.RevisionPreview, Is.EqualTo("D"));
        }

        [Test]
        public async Task PushRevision_OnSuccess_StatusText_ShowsSuccess()
        {
            _propertyService.Seed("Revision", "E");
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            await _vm.PushRevisionToInventreeAsync();

            Assert.That(_vm.StatusText,
                Does.Contain("pushed").IgnoreCase
                .Or.Contain("\u2713"));
        }

        [Test]
        public async Task PushRevision_OnHttpError_StatusText_ShowsError()
        {
            _client.PartToReturn  = SamplePart;
            _client.ThrowOnUpdate = new System.Net.Http.HttpRequestException("500");
            CreateVm();
            await _vm.FetchPartAsync();

            await _vm.PushRevisionToInventreeAsync();

            Assert.That(_vm.StatusText, Does.Contain("Error").IgnoreCase);
        }

        // ── PushImage ─────────────────────────────────────────────────────────

        [Test]
        public async Task PushImage_WhenNoPartFetched_DoesNotCallUpload()
        {
            CreateVm();

            using (var img = new Bitmap(100, 100))
                await _vm.PushImageAsync(imageOverride: img);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushImage_CallsUploadWithCorrectPk()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            using (var img = new Bitmap(100, 100))
                await _vm.PushImageAsync(imageOverride: img);

            Assert.That(_client.LastUploadedPk, Is.EqualTo(42));
        }

        [Test]
        public async Task PushImage_CallsUploadWithNonEmptyPngData()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            using (var img = new Bitmap(100, 100))
                await _vm.PushImageAsync(imageOverride: img);

            Assert.That(_client.LastUploadedImageData, Is.Not.Null.And.Not.Empty);
            // PNG magic bytes
            Assert.That(_client.LastUploadedImageData![0], Is.EqualTo(137));
            Assert.That(_client.LastUploadedImageData![1], Is.EqualTo(80));
        }

        [Test]
        public async Task PushImage_OnSuccess_StatusText_ShowsSuccess()
        {
            _client.PartToReturn = SamplePart;
            CreateVm();
            await _vm.FetchPartAsync();

            using (var img = new Bitmap(100, 100))
                await _vm.PushImageAsync(imageOverride: img);

            Assert.That(_vm.StatusText,
                Does.Contain("image").IgnoreCase
                .Or.Contain("\u2713"));
        }

        [Test]
        public async Task PushImage_OnUploadError_StatusText_ShowsError()
        {
            _client.PartToReturn  = SamplePart;
            _client.ThrowOnUpload = new System.Net.Http.HttpRequestException("upload failed");
            CreateVm();
            await _vm.FetchPartAsync();

            using (var img = new Bitmap(100, 100))
                await _vm.PushImageAsync(imageOverride: img);

            Assert.That(_vm.StatusText, Does.Contain("Error").IgnoreCase);
        }

        [Test]
        public async Task PushImage_WhenClientIsNull_DoesNotThrow()
        {
            _propertyService.Seed("PartNo", "R-10K-0402");
            _vm = new TaskPaneViewModel(null, _propertyService);

            using (var img = new Bitmap(100, 100))
                Assert.DoesNotThrowAsync(() => _vm.PushImageAsync(imageOverride: img));
        }

        // ── UpdateClient ──────────────────────────────────────────────────────

        [Test]
        public void UpdateClient_ToNull_FetchEnabled_IsFalse()
        {
            CreateVm();

            _vm.UpdateClient(null);

            Assert.That(_vm.FetchEnabled, Is.False);
        }

        [Test]
        public void UpdateClient_ToNull_StatusText_ShowsConfigureMessage()
        {
            CreateVm();

            _vm.UpdateClient(null);

            Assert.That(_vm.StatusText,
                Does.Contain("Settings").IgnoreCase
                .Or.Contain("configured").IgnoreCase);
        }

        [Test]
        public void UpdateClient_ToNewClient_FetchEnabled_IsTrue()
        {
            _propertyService.Seed("PartNo", "R-10K-0402");
            _vm = new TaskPaneViewModel(null, _propertyService);

            _vm.UpdateClient(new StubInventreeClient());

            Assert.That(_vm.FetchEnabled, Is.True);
        }

        // ── Mapping schema check ───────────────────────────────────────────────────

        [Test]
        public void OnInitialisation_MappingSchemaMatches_NoWarning()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig { SchemaVersion = "1" }
            };
            _propertyService.Seed("PartNo", "");
            _vm = new TaskPaneViewModel(_client, _propertyService, null, provider);

            Assert.That(_vm.StatusSeverity, Is.EqualTo(StatusSeverity.None));
        }

        [Test]
        public void OnInitialisation_MappingSchemaVersionMismatch_SetsWarningStatus()
        {
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig { SchemaVersion = "99" }
            };
            _propertyService.Seed("PartNo", "");
            _vm = new TaskPaneViewModel(_client, _propertyService, null, provider);

            Assert.That(_vm.StatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            Assert.That(_vm.StatusText,     Does.Contain("schema mismatch"));
        }

        [Test]
        public void UpdateMapping_SchemaVersionMismatch_SetsWarningStatus()
        {
            CreateVm();
            var provider = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig { SchemaVersion = "99" }
            };

            _vm.UpdateMapping(provider);

            Assert.That(_vm.StatusSeverity, Is.EqualTo(StatusSeverity.Warning));
        }

        [Test]
        public void UpdateMapping_SchemaVersionMatches_ClearsWarning()
        {
            // Start with a mismatch, then update to a matching provider
            var bad = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig { SchemaVersion = "99" }
            };
            _propertyService.Seed("PartNo", "");
            _vm = new TaskPaneViewModel(_client, _propertyService, null, bad);

            var good = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig { SchemaVersion = "1" }
            };
            _vm.UpdateMapping(good);

            Assert.That(_vm.StatusSeverity, Is.EqualTo(StatusSeverity.None));
        }

        [Test]
        public void UpdateMapping_WhenDocumentOpen_RefreshesCurrentProperties()
        {
            // Arrange: document open with default mapping property names
            _propertyService.Seed("PartNo",      "R-10K-0402");
            _propertyService.Seed("Description", "Resistor original");
            _propertyService.Seed("Notes",       "Old notes");
            _propertyService.Seed("Revision",    "A");
            _vm = new TaskPaneViewModel(_client, _propertyService);

            // Seed renamed properties that the new mapping will point to
            _propertyService.Seed("MyName",     "Resistor remapped");
            _propertyService.Seed("MyNotes",    "New notes");
            _propertyService.Seed("MyRevision", "B");

            // Act: switch to a provider with different property names
            var remapped = new StubPropertyMappingProvider
            {
                Config = new PropertyMappingConfig
                {
                    SchemaVersion    = "1",
                    IpnProperty      = "PartNo",
                    NameProperty     = "MyName",
                    NotesProperty    = "MyNotes",
                    RevisionProperty = "MyRevision",
                }
            };
            _vm.UpdateMapping(remapped);

            // Assert: SW property text boxes now reflect the remapped names
            Assert.That(_vm.CurrentName,     Is.EqualTo("Resistor remapped"));
            Assert.That(_vm.CurrentNotes,    Is.EqualTo("New notes"));
            Assert.That(_vm.CurrentRevision, Is.EqualTo("B"));
        }
    }

    /// <summary>Stub client that throws on GetPartByIpnAsync — used to test the fetch error path.</summary>
    internal sealed class ThrowingStubClient : IInventreeClient
    {
        public Task<InventreePart?> GetPartByIpnAsync(string ipn)
            => throw new System.Net.Http.HttpRequestException("simulated network error");

        public Task UpdatePartRevisionAsync(int pk, string revision)
            => Task.CompletedTask;

        public Task UpdatePartNameAsync(int pk, string name)
            => Task.CompletedTask;

        public Task UpdatePartNotesAsync(int pk, string notes)
            => Task.CompletedTask;

        public Task UploadPartImageAsync(int pk, byte[] pngData)
            => Task.CompletedTask;

        public Task<byte[]?> DownloadImageAsync(string url)
            => Task.FromResult<byte[]?>(null);

        public Task<InventreeServerInfo> GetServerInfoAsync()
            => Task.FromResult(new InventreeServerInfo());

        public Task<System.Collections.Generic.IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<InventreeCategory>>(
                new System.Collections.Generic.List<InventreeCategory>());

        public Task<int> CreatePartAsync(int categoryPk, string name, string? ipn = null)
            => Task.FromResult(0);

        public Task<InventreePart?> GetPartByPkAsync(int pk)
            => Task.FromResult<InventreePart?>(null);
    }
}

// ── Document-type awareness tests ──────────────────────────────────────────────
namespace SwInventreeAddin.Tests
{
    using SwInventreeAddin.SolidWorks;

    [TestFixture]
    public class DocumentTypeAwarenessTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;
        private TaskPaneViewModel           _vm;

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            // Seed a part number so the drawing block is the only thing preventing load
            _propertyService.Seed("PartNo", "DRW-001");
        }

        private void CreateVm() => _vm = new TaskPaneViewModel(_client, _propertyService);

        [Test]
        public void DrawingDocument_LoadPartNumber_ClearsPanel()
        {
            _propertyService.DocumentTypeToReturn = DocumentType.Drawing;
            CreateVm();

            // PartNumber should be empty — panel should be in cleared state
            Assert.That(_vm.PartNumber, Is.Empty);
            Assert.That(_vm.PropertiesSectionVisible, Is.False);
        }

        [Test]
        public void DrawingDocument_LoadPartNumber_ShowsWarningStatus()
        {
            _propertyService.DocumentTypeToReturn = DocumentType.Drawing;
            CreateVm();

            Assert.That(_vm.StatusText,     Does.Contain("Drawings").IgnoreCase);
            Assert.That(_vm.StatusSeverity, Is.EqualTo(StatusSeverity.Warning));
        }

        [Test]
        public void PartDocument_LoadPartNumber_LoadsNormally()
        {
            _propertyService.DocumentTypeToReturn = DocumentType.Part;
            CreateVm();

            Assert.That(_vm.PartNumber, Is.EqualTo("DRW-001"));
            Assert.That(_vm.PropertiesSectionVisible, Is.True);
        }

        [Test]
        public void AssemblyDocument_LoadPartNumber_LoadsNormally()
        {
            _propertyService.DocumentTypeToReturn = DocumentType.Assembly;
            CreateVm();

            Assert.That(_vm.PartNumber, Is.EqualTo("DRW-001"));
            Assert.That(_vm.PropertiesSectionVisible, Is.True);
        }

        [Test]
        public void SwitchingFromDrawingToPart_LoadsNormally()
        {
            // Start as drawing — panel should be blocked
            _propertyService.DocumentTypeToReturn = DocumentType.Drawing;
            CreateVm();
            Assert.That(_vm.PartNumber, Is.Empty);

            // Switch to part — panel should load correctly
            _propertyService.DocumentTypeToReturn = DocumentType.Part;
            _vm.LoadPartNumber();

            Assert.That(_vm.PartNumber, Is.EqualTo("DRW-001"));
            Assert.That(_vm.PropertiesSectionVisible, Is.True);
        }
    }
}

// ── Bidirectional property push tests ──────────────────────────────────────────
namespace SwInventreeAddin.Tests
{
    using System.Threading.Tasks;

    [TestFixture]
    public class BidirectionalPropertyTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;
        private TaskPaneViewModel           _vm;

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            _propertyService.Seed("PartNo", "ABC-001");

            _client.PartToReturn = new InventreePart
            {
                Pk       = 42,
                Ipn      = "ABC-001",
                Name     = "Widget",
                Notes    = "Some notes",
                Revision = "A"
            };

            _vm = new TaskPaneViewModel(_client, _propertyService);
        }

        // ── RevisionMatch ──────────────────────────────────────────────────────

        [Test]
        public async Task RevisionMatch_WhenEqual_IsTrue()
        {
            _propertyService.Seed("Revision", "A");
            await _vm.FetchPartAsync();

            Assert.That(_vm.RevisionMatch, Is.True);
        }

        [Test]
        public async Task RevisionMatch_WhenDifferent_IsFalse()
        {
            _propertyService.Seed("Revision", "B");
            await _vm.FetchPartAsync();

            Assert.That(_vm.RevisionMatch, Is.False);
        }

        // ── PushNameEnabled ────────────────────────────────────────────────────

        [Test]
        public async Task PushNameEnabled_WhenPartLoaded_IsTrue()
        {
            await _vm.FetchPartAsync();

            Assert.That(_vm.PushNameEnabled, Is.True);
        }

        // ── PushNameToInvenTreeAsync ───────────────────────────────────────────

        [Test]
        public async Task PushName_CallsClientWithSwValue()
        {
            _propertyService.Seed("Description", "My Part Name");
            await _vm.FetchPartAsync();

            await _vm.PushNameToInvenTreeAsync();

            Assert.That(_client.LastPushedName, Is.EqualTo("My Part Name"));
        }

        // ── PushNotesToInvenTreeAsync ──────────────────────────────────────────

        [Test]
        public async Task PushNotes_CallsClientWithSwValue()
        {
            _propertyService.Seed("Notes", "Custom notes here");
            await _vm.FetchPartAsync();

            await _vm.PushNotesToInvenTreeAsync();

            Assert.That(_client.LastPushedNotes, Is.EqualTo("Custom notes here"));
        }
    }
}

// ── Thumbnail tests ────────────────────────────────────────────────────────────
namespace SwInventreeAddin.Tests
{
    using System.Threading.Tasks;

    [TestFixture]
    public class ThumbnailTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;
        private TaskPaneViewModel           _vm;

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            _propertyService.Seed("PartNo", "TEST-001");

            _client.PartToReturn = new InventreePart
            {
                Pk           = 7,
                Ipn          = "TEST-001",
                Name         = "Widget",
                Notes        = string.Empty,
                Revision     = "A",
                ThumbnailUrl = "/media/thumbnails/widget.png"
            };

            _vm = new TaskPaneViewModel(_client, _propertyService);
        }

        [Test]
        public async Task ThumbnailBytes_AfterFetch_WhenClientReturnsBytes_IsSet()
        {
            var fakeBytes = new byte[] { 1, 2, 3 };
            _client.ThumbnailBytesToReturn = fakeBytes;

            await _vm.FetchPartAsync();

            Assert.That(_vm.ThumbnailBytes, Is.EqualTo(fakeBytes));
        }

        [Test]
        public async Task ThumbnailBytes_AfterFetch_WhenClientReturnsNull_IsNull()
        {
            _client.ThumbnailBytesToReturn = null;

            await _vm.FetchPartAsync();

            Assert.That(_vm.ThumbnailBytes, Is.Null);
        }

        [Test]
        public async Task ThumbnailBytes_AfterReset_IsNull()
        {
            _client.ThumbnailBytesToReturn = new byte[] { 9, 8, 7 };
            await _vm.FetchPartAsync();

            _vm.ClearAll();

            Assert.That(_vm.ThumbnailBytes, Is.Null);
        }

        [Test]
        public async Task ThumbnailBytes_WhenPartHasNoThumbnailUrl_DownloadNotCalled()
        {
            _client.PartToReturn!.ThumbnailUrl = null;

            await _vm.FetchPartAsync();

            Assert.That(_client.DownloadImageCallCount, Is.Zero);
        }

        [Test]
        public async Task ThumbnailBytes_WhenDownloadThrows_IsNullAndNoException()
        {
            _client.ThrowOnDownload = new System.Net.Http.HttpRequestException("network error");

            // Should complete without throwing and leave ThumbnailBytes null
            Assert.DoesNotThrowAsync(async () => await _vm.FetchPartAsync());
            Assert.That(_vm.ThumbnailBytes, Is.Null);
        }
    }
}

// ── PropertyMappingEditorWindow.HasDuplicatePropertyNames ──────────────────
namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class PropertyMappingEditorWindowHelpersTests
    {
        [Test]
        public void HasDuplicatePropertyNames_NoDuplicates_ReturnsFalse()
        {
            var names = new[] { "PartNo", "Description", "Notes" };

            Assert.That(
                PropertyMappingEditorWindow.HasDuplicatePropertyNames(names),
                Is.False);
        }

        [Test]
        public void HasDuplicatePropertyNames_CaseInsensitiveDuplicate_ReturnsTrue()
        {
            var names = new[] { "PartNo", "partno" };

            Assert.That(
                PropertyMappingEditorWindow.HasDuplicatePropertyNames(names),
                Is.True);
        }

        [Test]
        public void HasDuplicatePropertyNames_EmptyNamesIgnored_ReturnsFalse()
        {
            // Blank entries (unmapped fields) should not count as duplicates of each other
            var names = new[] { "", "", "PartNo" };

            Assert.That(
                PropertyMappingEditorWindow.HasDuplicatePropertyNames(names),
                Is.False);
        }

        [Test]
        public void HasDuplicatePropertyNames_AllEmpty_ReturnsFalse()
        {
            var names = new[] { "", "" };

            Assert.That(
                PropertyMappingEditorWindow.HasDuplicatePropertyNames(names),
                Is.False);
        }

        [Test]
        public void HasDuplicatePropertyNames_WhitespaceOnlyNamesIgnored_ReturnsFalse()
        {
            var names = new[] { "  ", "\t", "PartNo" };

            Assert.That(
                PropertyMappingEditorWindow.HasDuplicatePropertyNames(names),
                Is.False);
        }
    }
}

// ============================================================================
// TaskPaneViewModel — CreatePartEnabled + OpenCreatePartWindow
// ============================================================================

namespace SwInventreeAddin.Tests
{
    using SwInventreeAddin.InvenTree;
    using SwInventreeAddin.Tests.Stubs;
    using SwInventreeAddin.UI;

    [TestFixture]
    public class TaskPaneViewModelCreatePartTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            // Seed a populated document so LoadPartNumber doesn't immediately ClearAll.
            _propertyService.Seed("PartNo", "OA-001");
        }

        private TaskPaneViewModel CreateVm(bool withClient = true) =>
            new TaskPaneViewModel(
                withClient ? _client : null,
                _propertyService);

        // ── CreatePartEnabled ────────────────────────────────────────────────

        [Test]
        public void CreatePartEnabled_NoClient_IsFalse()
        {
            var vm = CreateVm(withClient: false);
            Assert.That(vm.CreatePartEnabled, Is.False);
        }

        [Test]
        public void CreatePartEnabled_WithClient_AndEmptyIpn_IsTrue()
        {
            // No IPN on the document — Create should be available.
            _propertyService.Seed("PartNo", string.Empty);
            var vm = CreateVm();
            Assert.That(vm.CreatePartEnabled, Is.True);
        }

        [Test]
        public void CreatePartEnabled_WithNonEmptyIpn_IsFalse()
        {
            // Part already has an IPN — Create would overwrite it.
            var vm = CreateVm();   // SetUp seeds PartNo="OA-001"
            Assert.That(vm.CreatePartEnabled, Is.False);
        }

        [Test]
        public void UpdateClient_ToNull_DisablesCreatePart()
        {
            // Start with a blank document so CreatePart is initially enabled.
            _propertyService.Seed("PartNo", string.Empty);
            var vm = CreateVm();
            Assert.That(vm.CreatePartEnabled, Is.True);

            vm.UpdateClient(null);

            Assert.That(vm.CreatePartEnabled, Is.False);
        }

        [Test]
        public void ClearAll_WithNoDocument_DisablesCreatePart()
        {
            // ClearAll represents "no document open" — Create should be disabled
            // even when a client exists, because there is no document to write IPN to.
            _propertyService.Seed("PartNo", string.Empty);
            var vm = CreateVm();
            vm.ClearAll();
            Assert.That(vm.CreatePartEnabled, Is.False);
        }

        [Test]
        public void LoadPartNumber_BlankPart_KeepsCreatePartEnabled()
        {
            // A document IS open but has no IPN yet — Create should be enabled.
            _propertyService.Seed("PartNo", string.Empty);
            var vm = CreateVm();
            Assert.That(vm.CreatePartEnabled, Is.True);
        }

        // ── OpenCreatePartWindow ─────────────────────────────────────────────

        [Test]
        public void OpenCreatePartWindow_OnPartCreated_PopulatesTaskPane()
        {
            var createdPart = new InventreePart
            {
                Pk       = 1,
                Ipn      = "R-NEW-001",
                Name     = "New Resistor",
                Notes    = string.Empty,
                Revision = string.Empty,
            };

            _propertyService.Seed("PartNo",      string.Empty);
            _propertyService.Seed("Description", string.Empty);
            var vm            = CreateVm();
            bool dialogOpened = false;

            vm.OpenCreatePartWindow(createVm =>
            {
                dialogOpened = true;

                // Fire the PartCreated event via its backing field (field-like event).
                var backingField = typeof(CreatePartViewModel)
                    .GetField("PartCreated",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public);
                var handler = backingField?.GetValue(createVm) as
                    System.EventHandler<InventreePart>;
                handler?.Invoke(createVm, createdPart);
            });

            Assert.That(dialogOpened,      Is.True);
            Assert.That(vm.PartNumber,     Is.EqualTo("R-NEW-001"));
            Assert.That(vm.NamePreview,    Is.EqualTo("New Resistor"));
            Assert.That(vm.CreatePartEnabled, Is.False);  // IPN now set — Create disabled
        }

        [Test]
        public void OpenCreatePartWindow_NullClient_DoesNotOpenDialog()
        {
            int callCount = 0;
            var vm = CreateVm(withClient: false);
            vm.OpenCreatePartWindow(_ => callCount++);
            Assert.That(callCount, Is.EqualTo(0));
        }
    }
}
