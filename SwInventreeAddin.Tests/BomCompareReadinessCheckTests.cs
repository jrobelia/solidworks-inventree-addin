using System;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomCompareReadinessCheckTests
    {
        private const string DefaultBomKeyword = "inventree";

        // -- Helpers ------------------------------------------------------------

        private static IAssemblyBomService CreateBomService(bool hasTable = true) =>
            new StubAssemblyBomService { HasBomTableResult = hasTable };

        private static PropertyMappingConfig CreateMapping(
            string? ipnAlias = "IPN",
            string? qtyAlias = "Qty",
            string? referenceAlias = "Reference",
            string? noteAlias = "Note") =>
            new PropertyMappingConfig
            {
                SchemaVersion = PropertyMappingConfig.CurrentSchemaVersion,
                BomColumnIpn = ipnAlias,
                BomColumnQty = qtyAlias,
                BomColumnReference = referenceAlias,
                BomColumnNote = noteAlias,
            };

        /// <summary>
        /// In-memory <see cref="IBomReadinessContext"/>: one coherent snapshot per
        /// capture plus recording for the two coordinator commands.
        /// </summary>
        private sealed class StubContext : IBomReadinessContext
        {
            public string Ipn { get; set; } = "PART-001";
            public int InMemoryPartPk { get; set; }
            public string StampedPkText { get; set; } = string.Empty;
            public string SwRevision { get; set; } = string.Empty;
            public string FetchedRevision { get; set; } = string.Empty;
            public PropertyMappingConfig Mapping { get; set; } = CreateMapping();

            public bool FetchCalled { get; private set; }
            public bool PushRevisionCalled { get; private set; }

            /// <summary>Side-effect applied when EnsurePartPopulatedAsync is called.</summary>
            public Action? OnFetch { get; set; }

            /// <summary>The typed result EnsurePartPopulatedAsync returns.</summary>
            public PartSyncResult EnsureResult { get; set; } =
                new PartSyncResult(PartSyncOutcome.Success);

            public BomReadinessSnapshot CaptureSnapshot() =>
                new BomReadinessSnapshot(
                    Ipn, InMemoryPartPk, StampedPkText, SwRevision, FetchedRevision, Mapping);

            public Task<PartSyncResult> EnsurePartPopulatedAsync()
            {
                FetchCalled = true;
                OnFetch?.Invoke();
                return Task.FromResult(EnsureResult);
            }

            public Task<PartSyncResult> PushRevisionAsync()
            {
                PushRevisionCalled = true;
                return Task.FromResult(new PartSyncResult(PartSyncOutcome.Success));
            }
        }

        // -- CheckAsync ---------------------------------------------------------

        [Test]
        public async Task CheckAsync_PkInMemory_DoesNotFetch()
        {
            var context = new StubContext { InMemoryPartPk = 42, StampedPkText = "42" };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            await check.CheckAsync();

            Assert.That(context.FetchCalled, Is.False);
        }

        [Test]
        public async Task CheckAsync_PkNotInMemory_AutoFetches()
        {
            var context = new StubContext { InMemoryPartPk = 0 };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            await check.CheckAsync();

            Assert.That(context.FetchCalled, Is.True);
        }

        [Test]
        public async Task CheckAsync_StillNoPkAfterFetch_ReturnsPkNotFound()
        {
            var context = new StubContext { InMemoryPartPk = 0 };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PkNotFound));
        }

        [Test]
        public async Task CheckAsync_FetchSucceeds_PkNotStampedInDocument_ReturnsPkNotStamped()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 0,
                StampedPkText = string.Empty,
            };
            context.OnFetch = () => context.InMemoryPartPk = 99;
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PkNotStamped));
        }

        [Test]
        public async Task CheckAsync_PkStamped_RevisionsEqual_ReturnsReady()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.Ready));
        }

        [Test]
        public async Task CheckAsync_ItIsNewer_ReturnsItIsNewer()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "B",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.ItIsNewer));
        }

        [Test]
        public async Task CheckAsync_SwIsNewer_ReturnsSwIsNewer()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "B",
                FetchedRevision = "A",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.SwIsNewer));
        }

        [Test]
        public async Task CheckAsync_AmbiguousRevisions_ReturnsAmbiguous()
        {
            // Non-comparable revision strings (e.g. "1.0" vs "A") produce Ambiguous.
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "1.0",
                FetchedRevision = "A",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.Ambiguous));
        }

        [Test]
        public async Task CheckAsync_PopulatesRevisionLabelsOnResult()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "B",
                FetchedRevision = "A",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.ItRevision, Is.EqualTo("A"));
        }

        [Test]
        public async Task CheckAsync_NoBomTable_ReturnsBomTableMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
        }

        [Test]
        public async Task CheckAsync_NoBomTable_PkNotInMemory_DoesNotFetch()
        {
            var context = new StubContext { InMemoryPartPk = 0 };
            context.OnFetch = () => context.InMemoryPartPk = 99;
            var check = new BomCompareReadinessCheck(context, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
            Assert.That(context.FetchCalled, Is.False);
            Assert.That(context.InMemoryPartPk, Is.EqualTo(0));
        }

        // -- Confirmation propagation -----------------------------------------

        [Test]
        public async Task CheckAsync_EnsureReturnsConfirmation_PropagatesFetchResult()
        {
            var context = new StubContext { InMemoryPartPk = 0 };
            context.EnsureResult = new PartSyncResult(PartSyncOutcome.LinkMismatchConfirmation);
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.FetchConfirmationRequired));
            Assert.That(result.FetchResult, Is.SameAs(context.EnsureResult));
        }

        // -- BOM column alias pre-flight ----------------------------------------

        [Test]
        public async Task CheckAsync_BomColumnIpnBlank_ReturnsBomColumnAliasesMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                Mapping = CreateMapping(ipnAlias: "", qtyAlias: "Qty"),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomColumnAliasesMissing));
        }

        [Test]
        public async Task CheckAsync_BomColumnQtyBlank_ReturnsBomColumnAliasesMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                Mapping = CreateMapping(ipnAlias: "IPN", qtyAlias: ""),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomColumnAliasesMissing));
        }

        [Test]
        public async Task CheckAsync_BomColumnIpnAndQtyBlank_ReturnsBomColumnAliasesMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                Mapping = CreateMapping(ipnAlias: "", qtyAlias: ""),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomColumnAliasesMissing));
        }

        [Test]
        public async Task CheckAsync_BomColumnAliasesPresent_ReturnsReady()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                Mapping = CreateMapping(),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.Ready));
        }

        [Test]
        public async Task CheckAsync_NoBomTableAndMissingAliases_ReturnsBomTableMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                Mapping = CreateMapping(ipnAlias: "", qtyAlias: ""),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
        }

        [Test]
        public async Task CheckAsync_ItIsNewer_WithMissingAliases_ReturnsItIsNewer()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "B",
                Mapping = CreateMapping(ipnAlias: "", qtyAlias: ""),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.ItIsNewer));
        }

        [Test]
        public async Task CheckAsync_SwIsNewer_AfterPush_StillMissingAliases_ReturnsBomColumnAliasesMissing()
        {
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "B",
                FetchedRevision = "A",
                Mapping = CreateMapping(ipnAlias: "", qtyAlias: ""),
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var beforePush = await check.CheckAsync();
            Assert.That(beforePush.Outcome, Is.EqualTo(BomCompareOutcome.SwIsNewer));

            await check.PushRevisionAsync();
            context.FetchedRevision = "B"; // simulate the pre-flight update the UI would see

            var afterPush = await check.CheckAsync();
            Assert.That(afterPush.Outcome, Is.EqualTo(BomCompareOutcome.BomColumnAliasesMissing));
        }

        // -- PushRevisionAsync --------------------------------------------------

        [Test]
        public async Task PushRevisionAsync_DelegatesToContext()
        {
            var context = new StubContext();
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            await check.PushRevisionAsync();

            Assert.That(context.PushRevisionCalled, Is.True);
        }

        // -- Constructor guard --------------------------------------------------

        [Test]
        public void Constructor_NullContext_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(null!, CreateBomService(), DefaultBomKeyword),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_NullBomService_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(new StubContext(), null!, DefaultBomKeyword),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Constructor_NullBomKeyword_Throws()
        {
            Assert.That(() => new BomCompareReadinessCheck(new StubContext(), CreateBomService(), null!),
                Throws.ArgumentNullException);
        }
    }
}
