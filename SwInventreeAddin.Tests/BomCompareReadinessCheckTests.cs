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
        /// capture plus recording for the push command and the capture count.
        /// </summary>
        private sealed class StubContext : IBomReadinessContext
        {
            public string Ipn { get; set; } = "PART-001";
            public int InMemoryPartPk { get; set; }
            public string StampedPkText { get; set; } = string.Empty;
            public string SwRevision { get; set; } = string.Empty;
            public string FetchedRevision { get; set; } = string.Empty;

            /// <summary>
            /// The fetched part's Assembly flag — defaults true so existing
            /// populated-session fixtures stay valid.
            /// </summary>
            public bool FetchedPartIsAssembly { get; set; } = true;

            public PropertyMappingConfig Mapping { get; set; } = CreateMapping();

            /// <summary>How many times the check captured a snapshot — the contract is exactly once.</summary>
            public int SnapshotCount { get; private set; }

            public bool PushRevisionCalled { get; private set; }

            public BomReadinessSnapshot CaptureSnapshot()
            {
                SnapshotCount++;
                return new BomReadinessSnapshot(
                    Ipn, InMemoryPartPk, StampedPkText, SwRevision, FetchedRevision,
                    FetchedPartIsAssembly, Mapping);
            }

            public Task<PartSyncResult> PushRevisionAsync()
            {
                PushRevisionCalled = true;
                return Task.FromResult(new PartSyncResult(PartSyncOutcome.Success));
            }
        }

        // -- CheckAsync ---------------------------------------------------------

        [Test]
        public async Task CheckAsync_NoSession_ReturnsSessionNotPopulated()
        {
            // Compare BOM is session-gated: a session-absent snapshot is the
            // dropped-session race, never a fetch trigger.
            var context = new StubContext { InMemoryPartPk = 0, StampedPkText = "42" };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.SessionNotPopulated));
        }

        [Test]
        public async Task CheckAsync_NoSessionUnstamped_ReturnsSessionNotPopulated()
        {
            // A session-absent, PK-unstamped snapshot must still hit the no-session
            // outcome — discriminates the SessionNotPopulated-before-PkNotStamped order.
            var context = new StubContext { InMemoryPartPk = 0 };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.SessionNotPopulated));
        }

        [Test]
        public async Task CheckAsync_Always_CapturesExactlyOneSnapshot()
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
            Assert.That(context.SnapshotCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CheckAsync_SessionWithoutStampedPk_ReturnsPkNotStamped()
        {
            // An IPN-fetched session on an unstamped document: the check
            // evaluates the existing session and finds no stamped PK.
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = string.Empty,
            };
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
        public async Task CheckAsync_NoBomTable_NoSession_ReturnsBomTableMissing()
        {
            // The BOM-table probe runs before the session check — no fetch and
            // no session outcome for an assembly with nothing to compare.
            var context = new StubContext { InMemoryPartPk = 0 };
            var check = new BomCompareReadinessCheck(context, CreateBomService(false), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.BomTableMissing));
            Assert.That(context.SnapshotCount, Is.EqualTo(1));
        }

        // -- Assembly-flag pre-flight --------------------------------------------

        [Test]
        public async Task CheckAsync_FetchedPartNotAssembly_ReturnsPartNotAssembly()
        {
            // A part that cannot hold a BOM fails before any revision question —
            // compare-open against it is meaningless and the push is refused anyway.
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "A",
                FetchedPartIsAssembly = false,
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PartNotAssembly));
        }

        [Test]
        public async Task CheckAsync_FetchedPartNotAssembly_ItIsNewer_ReturnsPartNotAssembly()
        {
            // The Assembly-flag gate sits before the revision comparison — a
            // flag-unchecked session can never surface a revision outcome.
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = "42",
                SwRevision = "A",
                FetchedRevision = "B",
                FetchedPartIsAssembly = false,
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PartNotAssembly));
        }

        [Test]
        public async Task CheckAsync_FetchedPartNotAssembly_PkUnstamped_ReturnsPkNotStamped()
        {
            // PK presence precedes the Assembly flag — discriminates the
            // PkNotStamped-before-PartNotAssembly order.
            var context = new StubContext
            {
                InMemoryPartPk = 42,
                StampedPkText = string.Empty,
                FetchedPartIsAssembly = false,
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.PkNotStamped));
        }

        [Test]
        public async Task CheckAsync_NoSession_FetchedPartNotAssembly_ReturnsSessionNotPopulated()
        {
            // An unpopulated session hits SessionNotPopulated — the flag
            // defaults false without a session but is never read that early.
            var context = new StubContext
            {
                InMemoryPartPk = 0,
                StampedPkText = "42",
                FetchedPartIsAssembly = false,
            };
            var check = new BomCompareReadinessCheck(context, CreateBomService(), DefaultBomKeyword);

            var result = await check.CheckAsync();

            Assert.That(result.Outcome, Is.EqualTo(BomCompareOutcome.SessionNotPopulated));
        }

        // -- Snapshot immutability ------------------------------------------------

        [Test]
        public void BomReadinessSnapshot_Mapping_IsDefensivelyCopiedInAndOut()
        {
            var mapping = CreateMapping();
            var snapshot = new BomReadinessSnapshot("PART-001", 0, string.Empty, "A", "A", true, mapping);

            mapping.BomColumnIpn = "POISONED";
            Assert.That(snapshot.Mapping.BomColumnIpn, Is.EqualTo("IPN"),
                "mutating the source config must not change the snapshot");

            var read = snapshot.Mapping;
            read.BomColumnIpn = "POISONED";
            Assert.That(snapshot.Mapping.BomColumnIpn, Is.EqualTo("IPN"),
                "mutating a returned Mapping must not change the snapshot");
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
