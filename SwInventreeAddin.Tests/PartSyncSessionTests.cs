using System;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class PartSyncSessionTests
    {
        private StubInventreeClient         _client;
        private StubDocumentPropertyService _propertyService;
        private PropertyMappingConfig       _mapping;

        private static readonly InventreePart SamplePart = new InventreePart
        {
            Pk          = 7,
            Name        = "Resistor 10k",
            Description = "10k ohm 1% 0402",
            Notes       = "SMD 0402",
            Revision    = "B",
            Ipn         = "R-10K-0402",
        };

        [SetUp]
        public void SetUp()
        {
            _client          = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            _mapping         = new PropertyMappingConfig();
        }

        private PartSyncSession CreateSession(byte[]? thumbnailBytes = null) =>
            new PartSyncSession(
                new InventreePart
                {
                    Pk          = SamplePart.Pk,
                    Name        = SamplePart.Name,
                    Description = SamplePart.Description,
                    Notes       = SamplePart.Notes,
                    Revision    = SamplePart.Revision,
                    Ipn         = SamplePart.Ipn,
                },
                _client,
                _propertyService,
                _mapping,
                thumbnailBytes);

        // ── Constructor ───────────────────────────────────────────────────────

        [Test]
        public void Constructor_Part_IsAccessible()
        {
            var session = CreateSession();

            Assert.That(session.Part.Pk, Is.EqualTo(SamplePart.Pk));
        }

        [Test]
        public void Constructor_ThumbnailBytes_IsNullWhenNotProvided()
        {
            var session = CreateSession();

            Assert.That(session.ThumbnailBytes, Is.Null);
        }

        [Test]
        public void Constructor_ThumbnailBytes_IsSetWhenProvided()
        {
            var bytes   = new byte[] { 1, 2, 3 };
            var session = CreateSession(thumbnailBytes: bytes);

            Assert.That(session.ThumbnailBytes, Is.EqualTo(bytes));
        }

        // ── Apply ─────────────────────────────────────────────────────────────

        [Test]
        public void Apply_WritesNameToDocument()
        {
            var session = CreateSession();

            session.Apply();

            Assert.That(_propertyService.GetCustomProperty(_mapping.NameProperty),
                        Is.EqualTo(SamplePart.Name));
        }

        [Test]
        public void Apply_WritesNotesToDocument()
        {
            var session = CreateSession();

            session.Apply();

            Assert.That(_propertyService.GetCustomProperty(_mapping.NotesProperty),
                        Is.EqualTo(SamplePart.Notes));
        }

        [Test]
        public void Apply_WritesDescriptionToDocument()
        {
            var session = CreateSession();

            session.Apply();

            Assert.That(_propertyService.GetCustomProperty(_mapping.DescriptionProperty),
                        Is.EqualTo(SamplePart.Description));
        }

        // ── ApplyName ─────────────────────────────────────────────────────────

        [Test]
        public void ApplyName_WritesNameToMappedProperty()
        {
            var session = CreateSession();

            session.ApplyName();

            Assert.That(_propertyService.GetCustomProperty(_mapping.NameProperty),
                        Is.EqualTo(SamplePart.Name));
        }

        [Test]
        public void ApplyName_DoesNotWriteNotesProperty()
        {
            var session = CreateSession();

            session.ApplyName();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain(_mapping.NotesProperty));
        }

        // ── ApplyNotes ────────────────────────────────────────────────────────

        [Test]
        public void ApplyNotes_WritesNotesToMappedProperty()
        {
            var session = CreateSession();

            session.ApplyNotes();

            Assert.That(_propertyService.GetCustomProperty(_mapping.NotesProperty),
                        Is.EqualTo(SamplePart.Notes));
        }

        [Test]
        public void ApplyNotes_DoesNotWriteNameProperty()
        {
            var session = CreateSession();

            session.ApplyNotes();

            Assert.That(_propertyService.SetCallLog, Does.Not.Contain(_mapping.NameProperty));
        }

        // ── ApplyDescription ──────────────────────────────────────────────────

        [Test]
        public void ApplyDescription_WritesDescriptionToMappedProperty()
        {
            var session = CreateSession();

            session.ApplyDescription();

            Assert.That(_propertyService.GetCustomProperty(_mapping.DescriptionProperty),
                        Is.EqualTo(SamplePart.Description));
        }

        // ── ApplyPk ───────────────────────────────────────────────────────────

        [Test]
        public void ApplyPk_WritesPkAsStringToMappedProperty()
        {
            var session = CreateSession();

            session.ApplyPk();

            Assert.That(_propertyService.GetCustomProperty(_mapping.PkProperty),
                        Is.EqualTo(SamplePart.Pk.ToString()));
        }

        // ── GetMissingApplyProperties ─────────────────────────────────────────

        [Test]
        public void GetMissingApplyProperties_PropertyExists_ReturnsEmpty()
        {
            _propertyService.Seed(_mapping.NameProperty, "existing");
            var session = CreateSession();

            var missing = session.GetMissingApplyProperties(_mapping.NameProperty);

            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void GetMissingApplyProperties_PropertyMissing_ReturnsPropertyName()
        {
            var session = CreateSession();

            var missing = session.GetMissingApplyProperties(_mapping.NameProperty);

            Assert.That(missing, Contains.Item(_mapping.NameProperty));
        }

        [Test]
        public void GetMissingApplyProperties_EmptyPropertyName_ReturnsEmpty()
        {
            var session = CreateSession();

            var missing = session.GetMissingApplyProperties(string.Empty);

            Assert.That(missing, Is.Empty);
        }

        // ── PushNameAsync ─────────────────────────────────────────────────────

        [Test]
        public async Task PushNameAsync_CallsClientWithCorrectPkAndValue()
        {
            _propertyService.Seed(_mapping.NameProperty, "Updated Name");
            var session = CreateSession();

            await session.PushNameAsync();

            Assert.That(_client.LastPushedPk,   Is.EqualTo(SamplePart.Pk));
            Assert.That(_client.LastPushedName,  Is.EqualTo("Updated Name"));
        }

        [Test]
        public async Task PushNameAsync_UpdatesPartNameOnSuccess()
        {
            _propertyService.Seed(_mapping.NameProperty, "New Name");
            var session = CreateSession();

            await session.PushNameAsync();

            Assert.That(session.Part.Name, Is.EqualTo("New Name"));
        }

        [Test]
        public void PushNameAsync_PropagatesExceptionOnFailure()
        {
            _client.ThrowOnUpdate = new HttpRequestException("server error");
            var session = CreateSession();

            Assert.ThrowsAsync<HttpRequestException>(() => session.PushNameAsync());
        }

        // ── PushNotesAsync ────────────────────────────────────────────────────

        [Test]
        public async Task PushNotesAsync_CallsClientWithCorrectPkAndValue()
        {
            _propertyService.Seed(_mapping.NotesProperty, "Updated Notes");
            var session = CreateSession();

            await session.PushNotesAsync();

            Assert.That(_client.LastPushedPk,    Is.EqualTo(SamplePart.Pk));
            Assert.That(_client.LastPushedNotes,  Is.EqualTo("Updated Notes"));
        }

        [Test]
        public async Task PushNotesAsync_UpdatesPartNotesOnSuccess()
        {
            _propertyService.Seed(_mapping.NotesProperty, "New Notes");
            var session = CreateSession();

            await session.PushNotesAsync();

            Assert.That(session.Part.Notes, Is.EqualTo("New Notes"));
        }

        [Test]
        public void PushNotesAsync_PropagatesExceptionOnFailure()
        {
            _client.ThrowOnUpdate = new HttpRequestException("server error");
            var session = CreateSession();

            Assert.ThrowsAsync<HttpRequestException>(() => session.PushNotesAsync());
        }

        // ── PushDescriptionAsync ──────────────────────────────────────────────

        [Test]
        public async Task PushDescriptionAsync_CallsClientWithCorrectPkAndValue()
        {
            _propertyService.Seed(_mapping.DescriptionProperty, "Updated Description");
            var session = CreateSession();

            await session.PushDescriptionAsync();

            Assert.That(_client.LastPushedPk,          Is.EqualTo(SamplePart.Pk));
            Assert.That(_client.LastPushedDescription,  Is.EqualTo("Updated Description"));
        }

        [Test]
        public async Task PushDescriptionAsync_UpdatesPartDescriptionOnSuccess()
        {
            _propertyService.Seed(_mapping.DescriptionProperty, "New Description");
            var session = CreateSession();

            await session.PushDescriptionAsync();

            Assert.That(session.Part.Description, Is.EqualTo("New Description"));
        }

        [Test]
        public void PushDescriptionAsync_PropagatesExceptionOnFailure()
        {
            _client.ThrowOnUpdate = new HttpRequestException("server error");
            var session = CreateSession();

            Assert.ThrowsAsync<HttpRequestException>(() => session.PushDescriptionAsync());
        }

        // ── PushRevisionAsync ─────────────────────────────────────────────────

        [Test]
        public async Task PushRevisionAsync_CallsClientWithCorrectPkAndValue()
        {
            _propertyService.Seed(_mapping.RevisionProperty, "C");
            var session = CreateSession();

            await session.PushRevisionAsync();

            Assert.That(_client.LastPushedPk,       Is.EqualTo(SamplePart.Pk));
            Assert.That(_client.LastPushedRevision,  Is.EqualTo("C"));
        }

        [Test]
        public async Task PushRevisionAsync_UpdatesPartRevisionOnSuccess()
        {
            _propertyService.Seed(_mapping.RevisionProperty, "C");
            var session = CreateSession();

            await session.PushRevisionAsync();

            Assert.That(session.Part.Revision, Is.EqualTo("C"));
        }

        [Test]
        public void PushRevisionAsync_PropagatesExceptionOnFailure()
        {
            _client.ThrowOnUpdate = new HttpRequestException("server error");
            var session = CreateSession();

            Assert.ThrowsAsync<HttpRequestException>(() => session.PushRevisionAsync());
        }

        // ── SetThumbnail ──────────────────────────────────────────────────────

        [Test]
        public void SetThumbnail_UpdatesThumbnailBytes()
        {
            var session  = CreateSession();
            var newBytes = new byte[] { 9, 8, 7 };

            session.SetThumbnail(newBytes);

            Assert.That(session.ThumbnailBytes, Is.EqualTo(newBytes));
        }

        [Test]
        public void SetThumbnail_ReplacesExistingThumbnailBytes()
        {
            var original = new byte[] { 1, 2, 3 };
            var session  = CreateSession(thumbnailBytes: original);
            var updated  = new byte[] { 4, 5, 6 };

            session.SetThumbnail(updated);

            Assert.That(session.ThumbnailBytes, Is.EqualTo(updated));
        }
    }
}
