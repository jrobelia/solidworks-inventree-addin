using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomCompareReadinessCheckTests
    {
        private const string DefaultBomKeyword = "inventree";

        // -- Stub ---------------------------------------------------------------

        // -- Helpers ------------------------------------------------------------

        private static IAssemblyBomService CreateBomService(bool hasTable = true) =>
            new StubAssemblyBomService { HasBomTableResult = hasTable };

        private sealed class StubSource : IBomReadinessSource
        {
            public int    CurrentInvenTreePk { get; set; }
            public string PartNumber         { get; set; } = "PART-001";
            public string CurrentPk          { get; set; } = string.Empty;
            public string CurrentRevision    { get; set; } = string.Empty;
            public string RevisionPreview    { get; set; } = string.Empty;

            public bool FetchCalled         { get; private set; }
            public bool RefreshCalled       { get; private set; }
            public bool PushRevisionCalled  { get; private set; }

            /// <summary>Side-effect applied when FetchPartAsync is called.</summary>
            public Action? OnFetch { get; set; }

            public Task FetchPartAsync()
            {
                FetchCalled = true;
                OnFetch?.Invoke();
                return Task.CompletedTask;
            }

            public void RefreshCurrentProperties() => RefreshCalled = true;

            public Task PushRevisionToInventreeAsync()
            {
                PushRevisionCalled = true;
                return Task.CompletedTask;
            }
        }

        // -- CheckAsync ---------------------------------------------------------

        [Test]
        public async Task CheckAsync_PkInMemory_DoesNotFetch()
        {
            var source = new StubSource { CurrentInvenTreePk = 42, CurrentPk = "42" };
            var check  = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            await check.CheckAsync();

            Assert.That(source.FetchCalled, Is.False);
        }

        [Test]
        public async Task CheckAsync_PkNotInMemory_AutoFetches()
        {
            var source = new StubSource { CurrentInvenTreePk = 0 };
            var check  = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            await check.CheckAsync();

            Assert.That(source.FetchCalled, Is.True);
        }

        [Test]
        public async Task CheckAsync_StillNoPkAfterFetch_ReturnsPkNotFound()
        {
            var source = new StubSource { CurrentInvenTreePk = 0 };
            var check  = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PkNotFound));
        }

        [Test]
        public async Task CheckAsync_FetchSucceeds_PkNotStampedInDocument_ReturnsPkNotStamped()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 0,
                CurrentPk          = string.Empty,
            };
            source.OnFetch = () => source.CurrentInvenTreePk = 99;
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PkNotStamped));
        }

        [Test]
        public async Task CheckAsync_PkStamped_RevisionsEqual_ReturnsReady()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "A",
                RevisionPreview    = "A",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.Ready));
        }

        [Test]
        public async Task CheckAsync_ItIsNewer_ReturnsItIsNewer()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "A",
                RevisionPreview    = "B",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.ItIsNewer));
        }

        [Test]
        public async Task CheckAsync_SwIsNewer_ReturnsSwIsNewer()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "B",
                RevisionPreview    = "A",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.SwIsNewer));
        }

        [Test]
        public async Task CheckAsync_AmbiguousRevisions_ReturnsAmbiguous()
        {
            // Non-comparable revision strings (e.g. "1.0" vs "A") produce Ambiguous.
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "1.0",
                RevisionPreview    = "A",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.Ambiguous));
        }

        [Test]
        public async Task CheckAsync_PopulatesRevisionLabelsOnResult()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "B",
                RevisionPreview    = "A",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.SwRevision, Is.EqualTo("B"));
            Assert.That(result.ItRevision, Is.EqualTo("A"));
        }

        [Test]
        public async Task CheckAsync_NoBomTable_ReturnsBomTableMissing()
        {
            var source = new StubSource
            {
                CurrentInvenTreePk = 42,
                CurrentPk          = "42",
                CurrentRevision    = "A",
                RevisionPreview    = "A",
            };
            var check = new BomCompareReadinessCheck(source, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
        }

        [Test]
        public async Task CheckAsync_NoBomTable_PkNotInMemory_DoesNotFetch()
        {
            var source = new StubSource { CurrentInvenTreePk = 0 };
            source.OnFetch = () => source.CurrentInvenTreePk = 99;
            var check = new BomCompareReadinessCheck(source, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
            Assert.That(source.FetchCalled, Is.False);
            Assert.That(source.CurrentInvenTreePk, Is.EqualTo(0));
        }

        // -- PushRevisionAsync --------------------------------------------------

        [Test]
        public async Task PushRevisionAsync_DelegatesToSource()
        {
            var source = new StubSource();
            var check  = new BomCompareReadinessCheck(source, CreateBomService(), DefaultBomKeyword);

            await check.PushRevisionAsync();

            Assert.That(source.PushRevisionCalled, Is.True);
        }

        // -- Constructor guard --------------------------------------------------

        [Test]
        public void Constructor_NullSource_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(null!, CreateBomService(), DefaultBomKeyword),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_NullBomService_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(new StubSource(), null!, DefaultBomKeyword),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_NullBomKeyword_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(new StubSource(), CreateBomService(), null!),
                Throws.ArgumentNullException);
        }
    }
}
