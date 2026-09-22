using System;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Coordinator-level tests for <see cref="PartSyncCoordinator"/>: session
    /// lifecycle, async-operation staleness, pending-write echoes, confirmation
    /// correlation, and the immutable projection surface.
    /// </summary>
    [TestFixture]
    public class PartSyncCoordinatorTests
    {
        private StubInventreeClient _client = null!;
        private StubDocumentPropertyService _propertyService = null!;
        private StubHostStaDispatcher _dispatcher = null!;
        private PartSyncCoordinator _coordinator = null!;

        private static readonly PropertyMappingConfig Mapping = PropertyMappingConfig.WithDefaults();

        private static readonly InventreePart SamplePart = new InventreePart
        {
            Pk = 42,
            Ipn = "R-10K-0402",
            Name = "Resistor 10k",
            Notes = "SMD 0402",
            Revision = "A",
            Description = "10k ohm 1% 0402",
        };

        [SetUp]
        public void SetUp()
        {
            _client = new StubInventreeClient();
            _propertyService = new StubDocumentPropertyService();
            _dispatcher = new StubHostStaDispatcher();
            _coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, _client);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Seeds an IPN-linked part document and installs it.</summary>
        private void SeedIpnDocument(string ipn = "R-10K-0402", string revision = "A")
        {
            _propertyService.Seed(Mapping.IpnProperty!, ipn);
            _propertyService.Seed(Mapping.RevisionProperty!, revision);
        }

        private void SeedPkDocument(int pk = 42)
        {
            _propertyService.Seed(Mapping.PkProperty!, pk.ToString());
        }

        /// <summary>Runs the full document evaluation and installs a fetched session.</summary>
        private async Task<PartSyncResult> InstallSessionViaFetch(string ipn = "R-10K-0402")
        {
            _propertyService.Seed(Mapping.IpnProperty!, ipn);
            _coordinator.UpdateDocument();
            return await _coordinator.FetchAsync(ipn);
        }

        /// <summary>
        /// Spins until the coordinator has parked <paramref name="expected"/>
        /// commits on the deferred dispatcher. Deferred client calls run their
        /// continuations asynchronously, so the commit lands in the queue a
        /// beat after <c>PendingCall.Complete</c> returns — asserting or
        /// draining the queue without this wait races the continuation.
        /// </summary>
        private void WaitForQueuedCommits(int expected = 1)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (_dispatcher.QueuedCount < expected)
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail(
                        $"Timed out waiting for {expected} parked commit(s); queue holds {_dispatcher.QueuedCount}.");
                System.Threading.Thread.Sleep(5);
            }
        }

        // ── Document lifecycle ───────────────────────────────────────────────

        [Test]
        public void UpdateDocument_FirstDocument_ReturnsActivatedAndInstallsSnapshot()
        {
            SeedIpnDocument();

            var transition = _coordinator.UpdateDocument();

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
            Assert.That(_coordinator.Document!.Ipn, Is.EqualTo("R-10K-0402"));
        }

        [Test]
        public void UpdateDocument_SameDocumentTwice_SecondIsRefreshed()
        {
            SeedIpnDocument();
            _coordinator.UpdateDocument();
            var generation = _coordinator.Generation;

            var transition = _coordinator.UpdateDocument();

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Refreshed));
            Assert.That(_coordinator.Generation, Is.EqualTo(generation));
        }

        [Test]
        public void UpdateDocument_NoActiveDocument_ReturnsActivatedAndClears()
        {
            _propertyService.DocumentTypeToReturn = DocumentType.Unknown;

            var transition = _coordinator.UpdateDocument();

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Empty));
            Assert.That(_coordinator.Document, Is.Null);
        }

        [Test]
        public async Task UpdateDocument_NewDocumentToken_DropsInstalledSession()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            Assert.That(_coordinator.FetchedPart, Is.Not.Null);

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();

            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [Test]
        public async Task UpdateDocument_NewDocumentWithIdenticalStamps_DropsSession_NoAdoption()
        {
            // #292: a copied file carries the same stamps — a document switch
            // must never adopt the old document's session.
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            _coordinator.UpdateDocument();
            await _coordinator.FetchAsync(string.Empty);
            Assert.That(_coordinator.FetchedPart, Is.Not.Null);

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();

            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task NotifyDocumentClosed_ClearsDocumentAndSession()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            _coordinator.NotifyDocumentClosed();

            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Empty));
            Assert.That(_coordinator.Document, Is.Null);
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        // ── Property-change classification ───────────────────────────────────

        [Test]
        public async Task NotifyDocumentPropertyChanged_EchoOfOwnWrite_IsConsumed()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();
            _coordinator.Apply(ApplyField.Name);

            // SolidWorks echoes our own write back — it must be swallowed.
            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, SamplePart.Name);

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.EchoConsumed));
        }

        [Test]
        public async Task NotifyDocumentPropertyChanged_UnmappedProperty_IsIgnored()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var change = _coordinator.NotifyDocumentPropertyChanged("Some Other Prop", "x");

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.Ignored));
        }

        [Test]
        public async Task NotifyDocumentPropertyChanged_MappedSameValue_Refreshes()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, SamplePart.Name);
            await InstallSessionViaFetch();

            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, SamplePart.Name);

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.Refreshed));
        }

        [Test]
        public async Task NotifyDocumentPropertyChanged_MappedDivergent_RefreshesDivergent()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, "user-edited name");

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.RefreshedDivergent));
        }

        [Test]
        public async Task NotifyDocumentPropertyChanged_IdentityMismatch_Reevaluates()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.IpnProperty!, "DIFFERENT-999");

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.Reevaluated));
        }

        [Test]
        public async Task NotifyDocumentPropertyChanged_MismatchedEcho_StaysPending_AndClassifies()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();
            _coordinator.Apply(ApplyField.Name);

            // A notification carrying a DIFFERENT value than the pending echo —
            // a real user edit racing our write — must not consume the echo.
            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, "user edit");

            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.RefreshedDivergent));

            // The pending echo survives for the real echo that follows.
            var echo = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, SamplePart.Name);
            Assert.That(echo, Is.EqualTo(PartSyncPropertyChange.EchoConsumed));
        }

        // ── Fetch — IPN path ─────────────────────────────────────────────────

        [Test]
        public async Task FetchAsync_Ipn_Success_InstallsSessionAndPopulates()
        {
            _client.PartToReturn = SamplePart;
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var result = await _coordinator.FetchAsync("R-10K-0402");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(result.PartPk, Is.EqualTo(42));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
            Assert.That(changed, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task FetchAsync_Ipn_PartNotFound_ReturnsPartNotFound()
        {
            _client.PartToReturn = null;
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("MISSING-001");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.PartNotFound));
            Assert.That(result.Ipn, Is.EqualTo("MISSING-001"));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_Ipn_NetworkError_ReturnsFailed()
        {
            _client.ThrowOnGetPartsByIpn = new HttpRequestException("boom");
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("R-10K-0402");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Failed));
            Assert.That(result.Diagnostic, Does.Contain("boom"));
        }

        [Test]
        public async Task FetchAsync_NoClient_ReturnsInvalidOperation()
        {
            var coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, client: null);
            SeedIpnDocument();
            coordinator.UpdateDocument();

            var result = await coordinator.FetchAsync("R-10K-0402");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        [Test]
        public async Task FetchAsync_UnhealthyMapping_ReturnsInvalidOperation()
        {
            var provider = new StubPropertyMappingProvider { Health = MappingHealth.Invalid };
            var coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, _client, provider);
            _propertyService.Seed("PartNo", "R-10K-0402");
            coordinator.UpdateDocument();

            var result = await coordinator.FetchAsync("R-10K-0402");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        // ── Fetch — PK path and link mismatch ────────────────────────────────

        [Test]
        public async Task FetchAsync_StampedPk_UsesPkPath()
        {
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastGetPartByPkPk, Is.EqualTo(42));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
        }

        [Test]
        public async Task FetchAsync_StampedPk_BlankDocIpn_WritesIpnBack()
        {
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(result.Ipn, Is.EqualTo("R-10K-0402"));
            Assert.That(_propertyService.DidWrite(Mapping.IpnProperty!, "R-10K-0402"), Is.True);
            Assert.That(_coordinator.Document!.Ipn, Is.EqualTo("R-10K-0402"));
        }

        [Test]
        public async Task FetchAsync_StampedPk_PartNotFound_ReturnsPartNotFound()
        {
            _client.PartByPkToReturn = null;
            SeedPkDocument(77);
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.PartNotFound));
            Assert.That(result.PartPk, Is.EqualTo(77));
        }

        [Test]
        public async Task FetchAsync_StampedPk_IpnMismatch_ReturnsConfirmation()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.LinkMismatchConfirmation));
            Assert.That(result.Confirmation, Is.Not.Null);
            Assert.That(result.DocumentIpn, Is.EqualTo("DOC-001"));
            Assert.That(result.FetchedPart!.Pk, Is.EqualTo(42));
            Assert.That(_coordinator.FetchedPart, Is.Null, "no session before confirmation");
        }

        [Test]
        public async Task FetchAsync_LinkMismatch_Approved_InstallsSession()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync(string.Empty);
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));
        }

        [Test]
        public async Task FetchAsync_LinkMismatch_Declined_ReturnsCancelledNoSession()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync(string.Empty);
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: false);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Cancelled));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [Test]
        public async Task FetchAsync_StampedPk_MatchingStamps_NoConfirmation()
        {
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "R-10K-0402");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(result.Confirmation, Is.Null);
        }

        // ── Fetch — duplicate IPN ────────────────────────────────────────────

        [Test]
        public async Task FetchAsync_DuplicateIpn_OneRevisionMatch_ReturnsConfirmation()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("PART-001");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateIpnConfirmation));
            Assert.That(result.MatchedCandidate!.Pk, Is.EqualTo(11));
            Assert.That(result.Candidates!.Count, Is.EqualTo(2));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_DuplicateIpn_Approved_InstallsMatchedCandidate()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync("PART-001");
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(11));
        }

        [Test]
        public async Task FetchAsync_DuplicateIpn_NoRevisionMatch_ReturnsNoRevisionMatch()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "C" },
            };
            SeedIpnDocument("PART-001", "Z");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("PART-001");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateNoRevisionMatch));
            Assert.That(result.Candidates!.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task FetchAsync_DuplicateIpn_TwoRevisionMatches_ReturnsAmbiguous()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "B" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("PART-001");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateAmbiguous));
        }

        [Test]
        public async Task ResumeConfirmationAsync_DuplicateIpn_InvalidCandidatePk_ReturnsInvalidOperation()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync("PART-001");
            var resumed = await _coordinator.ResumeConfirmationAsync(
                confirm.Confirmation!, approved: true, selectedCandidatePk: 999);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task ResumeConfirmationAsync_UnknownHandle_ReturnsInvalidOperation()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();
            var confirm = await _coordinator.FetchAsync("PART-001");
            Assert.That(confirm.Confirmation, Is.Not.Null);

            // A handle minted by a different (or no) pending operation must
            // never apply.
            var foreign = new PartSyncConfirmationHandle(9999);
            var resumed = await _coordinator.ResumeConfirmationAsync(foreign, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task ResumeConfirmationAsync_StaleOperation_ReturnsStale()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            var confirm = await _coordinator.FetchAsync("PART-001");
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateIpnConfirmation));

            // A document switch the host has not notified yet — the commit's
            // recapture discovers it and the parked approval lands as Stale.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);
            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
        }

        // ── Stale overlapping fetches ────────────────────────────────────────

        [Test]
        public async Task FetchAsync_OverlappingFetch_OlderCompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var first = _coordinator.FetchAsync("PART-001");
            var second = _coordinator.FetchAsync("PART-001");
            Assert.That(_client.PendingGetPartsByIpnCalls.Count, Is.EqualTo(2));

            // Complete the second fetch first — a fresh session installs.
            _client.PendingGetPartsByIpnCalls[1].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });
            var secondResult = await second;
            Assert.That(secondResult.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));

            // The older fetch now completes — it must not overwrite the newer session.
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart>
                {
                    new InventreePart { Pk = 99, Ipn = "PART-001", Name = "stale part" },
                });
            var firstResult = await first;

            Assert.That(firstResult.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task FetchAsync_DocumentSwitchMidFlight_CompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");

            // The host delivers a document switch before the fetch completes.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();

            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });
            var result = await fetch;

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_DocumentCloseMidFlight_CompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _coordinator.NotifyDocumentClosed();

            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });
            var result = await fetch;

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_ClientReplacedMidFlight_CompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _coordinator.UpdateClient(new StubInventreeClient());

            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });
            var result = await fetch;

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        // ── Queued dispatcher commits ────────────────────────────────────────

        [Test]
        public async Task FetchAsync_CommitParkedOnDispatcher_DocumentChange_IsStaleWithoutWrites()
        {
            // The pinned queued-commit case: the network result is back but its
            // commit is parked on the STA queue when the document changes —
            // the commit must revalidate inside the marshalled callback and
            // drop the result without installing anything.
            _dispatcher.DeferRun = true;
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });

            // The commit is parked — the session is not installed yet.
            WaitForQueuedCommits();
            Assert.That(_coordinator.FetchedPart, Is.Null);

            // Document switch arrives before the parked commit runs.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await fetch;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [Test]
        public async Task FetchAsync_CommitParkedOnDispatcher_ClientChange_IsStaleWithoutWrites()
        {
            _dispatcher.DeferRun = true;
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });

            WaitForQueuedCommits();
            _coordinator.UpdateClient(new StubInventreeClient());
            _dispatcher.RunAll();

            var result = await fetch;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_CommitParkedOnDispatcher_NoChange_CommitsNormally()
        {
            _dispatcher.DeferRun = true;
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });

            WaitForQueuedCommits();
            _dispatcher.RunAll();

            var result = await fetch;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
        }

        // ── EnsurePartPopulatedAsync ─────────────────────────────────────────

        [Test]
        public async Task EnsurePartPopulatedAsync_SessionCurrent_ReturnsSuccessImmediately()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            _client.DeferGetPartsByIpn = true;
            var result = await _coordinator.EnsurePartPopulatedAsync();

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.PendingGetPartsByIpnCalls, Is.Empty,
                "no fetch may run while a session is current");
        }

        [Test]
        public async Task EnsurePartPopulatedAsync_NoSession_FetchesByDocumentIdentity()
        {
            // Document identity addresses the fetch — never a typed IPN.
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            _coordinator.UpdateDocument();

            var result = await _coordinator.EnsurePartPopulatedAsync();

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastGetPartByPkPk, Is.EqualTo(42));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
        }

        [Test]
        public async Task EnsurePartPopulatedAsync_Confirmation_PropagatesForResume()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var result = await _coordinator.EnsurePartPopulatedAsync();

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.LinkMismatchConfirmation));
            Assert.That(result.Confirmation, Is.Not.Null);
        }

        // ── Create Part ──────────────────────────────────────────────────────

        [Test]
        public void CompleteCreatePart_ValidToken_WritesStampsAndInstallsSession()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();
            var created = new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" };

            var result = _coordinator.CompleteCreatePart(token, created);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.PkProperty!, "99"), Is.True);
            Assert.That(_propertyService.DidWrite(Mapping.IpnProperty!, "NEW-001"), Is.True);
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "New Part"), Is.True);
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(99));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));
            Assert.That(_coordinator.Document!.StampedPartPk, Is.EqualTo(99));
            Assert.That(_coordinator.Document!.Ipn, Is.EqualTo("NEW-001"));
        }

        [Test]
        public void CompleteCreatePart_StampedEchoes_AreRegisteredPending()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            _coordinator.CompleteCreatePart(token, new InventreePart { Pk = 99, Ipn = "NEW-001" });

            // The echo of our own PK write is consumed — not classified as a user edit.
            var change = _coordinator.NotifyDocumentPropertyChanged(Mapping.PkProperty!, "99");
            Assert.That(change, Is.EqualTo(PartSyncPropertyChange.EchoConsumed));
        }

        [Test]
        public void CompleteCreatePart_BlankIpn_DoesNotWriteIpn()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Name = "New Part" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.IpnProperty!), Is.False);
            Assert.That(_coordinator.Document!.StampedPartPk, Is.EqualTo(99));
        }

        [Test]
        public async Task CompleteCreatePart_SupersededByFetch_ReturnsStaleNoWrites()
        {
            _client.PartToReturn = SamplePart;
            SeedIpnDocument();
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            // A fetch mints a new family token — the parked create token stales.
            await _coordinator.FetchAsync("R-10K-0402");

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.DidWrite(Mapping.PkProperty!), Is.False);
        }

        [Test]
        public void CompleteCreatePart_DocumentSwitched_ReturnsStaleNoWrites()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.WriteLog, Is.Empty);
        }

        // ── Apply ────────────────────────────────────────────────────────────

        [Test]
        public async Task Apply_Name_WritesPropertyAndUpdatesSnapshot()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "Resistor 10k"), Is.True);
            Assert.That(_coordinator.Document!.Name, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task Apply_Pk_WritesStampAsString()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            // The stamp slot exists on the document so Apply needs no confirmation.
            _propertyService.Seed(Mapping.PkProperty!, string.Empty);

            var result = _coordinator.Apply(ApplyField.Pk);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.PkProperty!, "42"), Is.True);
            Assert.That(_coordinator.Document!.StampedPartPk, Is.EqualTo(42));
        }

        [Test]
        public void Apply_NoSession_ReturnsInvalidOperation()
        {
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        [Test]
        public async Task Apply_MissingProperty_ReturnsConfirmation()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            // The mapped Name property does not exist on the document.
            Assert.That(_propertyService.PropertyExists(Mapping.NameProperty!), Is.False);

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.MissingPropertyConfirmation));
            Assert.That(result.Confirmation, Is.Not.Null);
            Assert.That(result.MissingProperties, Contains.Item(Mapping.NameProperty!));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
        }

        [Test]
        public async Task Apply_MissingProperty_Approved_Writes()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var confirm = _coordinator.Apply(ApplyField.Name);
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "Resistor 10k"), Is.True);
        }

        [Test]
        public async Task Apply_MissingProperty_Declined_NoWrite()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var confirm = _coordinator.Apply(ApplyField.Name);
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: false);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Cancelled));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
        }

        [Test]
        public async Task Apply_ExistingProperty_NoConfirmationNeeded()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(result.Confirmation, Is.Null);
        }

        // ── Push ─────────────────────────────────────────────────────────────

        [Test]
        public async Task PushAsync_Name_UpdatesClientAndSessionPart()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            var result = await _coordinator.PushAsync(PushField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastPushedPk, Is.EqualTo(42));
            Assert.That(_client.LastPushedName, Is.EqualTo("New Name"));
            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo("New Name"));
        }

        [Test]
        public async Task PushAsync_Revision_UpdatesClientAndSessionPart()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.RevisionProperty!, "C");
            await InstallSessionViaFetch();

            var result = await _coordinator.PushAsync(PushField.Revision);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastPushedRevision, Is.EqualTo("C"));
            Assert.That(_coordinator.FetchedPart!.Revision, Is.EqualTo("C"));
        }

        [Test]
        public async Task PushAsync_Revision_ZeroPk_ReturnsInvalidOperation()
        {
            _client.PartToReturn = new InventreePart { Pk = 0, Ipn = "R-10K-0402" };
            await InstallSessionViaFetch();

            var result = await _coordinator.PushAsync(PushField.Revision);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(_client.LastPushedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushAsync_ServerError_ReturnsFailed_PartUnchanged()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();
            _client.ThrowOnUpdate = new HttpRequestException("500");

            var result = await _coordinator.PushAsync(PushField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Failed));
            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo("Resistor 10k"));
        }

        [Test]
        public async Task PushAsync_DocumentSwitchBeforeCommit_IsStale_PartUnchanged()
        {
            // The server was updated, but the session the push belongs to is
            // gone — the commit must not mutate a stale session.
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            _dispatcher.DeferRun = true;
            var push = _coordinator.PushAsync(PushField.Name);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_client.LastPushedName, Is.EqualTo("New Name"),
                "the server update already ran — only the session commit is stale");
        }

        // ── Push image / thumbnail ───────────────────────────────────────────

        [Test]
        public async Task PushImageAsync_Success_UploadsAndInstallsThumbnail()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, ThumbnailUrl = "/media/test.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 4, 5, 6 };

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(42));
            Assert.That(_client.LastUploadedImageData, Is.Not.Null.And.Not.Empty);
            Assert.That(_coordinator.ThumbnailBytes, Is.EqualTo(new byte[] { 4, 5, 6 }));
        }

        [Test]
        public async Task PushImageAsync_UploadFails_ReturnsFailed()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.ThrowOnUpload = new HttpRequestException("upload failed");

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Failed));
            Assert.That(_coordinator.ThumbnailBytes, Is.Null);
        }

        [Test]
        public async Task PushImageAsync_ThumbnailRefreshFails_ReturnsWarning()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart
            { Pk = 42, ThumbnailUrl = "/media/test.png" };
            _client.ThrowOnDownload = new Exception("download failed");

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.SucceededWithWarning));
            Assert.That(result.Diagnostic, Does.Contain("could not be refreshed"));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(42));
        }

        [Test]
        public async Task PushImageAsync_NoThumbnailUrl_ReturnsWarning()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = 42 };

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.SucceededWithWarning));
            Assert.That(result.Diagnostic, Does.Contain("thumbnail URL"));
        }

        [Test]
        public async Task PushImageAsync_NoSession_ReturnsInvalidOperation()
        {
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushImageAsync_Disposed_ReturnsInvalidOperation()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _coordinator.Dispose();

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(0));
        }

        // ── Mapping / client invalidation ────────────────────────────────────

        [Test]
        public async Task UpdateMapping_RebindsSessionUnderNewProvider()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            var provider = new StubPropertyMappingProvider();
            provider.Config.NameProperty = "OtherName";
            _coordinator.UpdateMapping(provider);

            // A mapping change rebuilds — never drops — the installed session.
            Assert.That(_coordinator.FetchedPart, Is.Not.Null);
            Assert.That(_coordinator.CurrentMapping.NameProperty, Is.EqualTo("OtherName"));
        }

        [Test]
        public async Task UpdateMapping_NullProvider_FallsBackToDefaultsAndKeepsSession()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            _coordinator.UpdateMapping(null);

            Assert.That(_coordinator.CurrentMapping.IpnProperty,
                Is.EqualTo(PropertyMappingConfig.WithDefaults().IpnProperty));
            Assert.That(_coordinator.FetchedPart, Is.Not.Null);
        }

        [Test]
        public async Task FetchAsync_MappingReplacedMidFlight_CompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument("PART-001", "A");
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("PART-001");
            _coordinator.UpdateMapping(new StubPropertyMappingProvider());

            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });
            var result = await fetch;

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task UpdateClient_DropsSessionAndClearsThumbnail()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            _coordinator.UpdateClient(new StubInventreeClient());

            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.ThumbnailBytes, Is.Null);
        }

        // ── Immutability ─────────────────────────────────────────────────────

        [Test]
        public void CompleteCreatePart_CopiesIncomingPart_LaterMutationCannotReachSession()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();
            var created = new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" };

            _coordinator.CompleteCreatePart(token, created);
            // The dialog-owned part is caller state — mutating it must not reach the session.
            created.Name = "MUTATED";
            created.Pk = 1;

            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo("New Part"));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(99));
        }

        [Test]
        public async Task FetchedPart_ClientPartMutatedAfterFetch_SessionIsolated()
        {
            var livePart = new InventreePart { Pk = 42, Ipn = "R-10K-0402", Name = "original" };
            _client.PartToReturn = livePart;
            await InstallSessionViaFetch();

            // Someone mutates the object the client handed back.
            livePart.Name = "changed underneath";
            livePart.Pk = 777;

            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo("original"));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(42));
        }

        [Test]
        public async Task ThumbnailBytes_ReturnedCopy_NotLiveArray()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = 42, ThumbnailUrl = "/t.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 1, 2, 3 };
            using var image = new Bitmap(10, 10);
            await _coordinator.PushImageAsync(image, Rectangle.Empty);

            var first = _coordinator.ThumbnailBytes!;
            first[0] = 99;

            Assert.That(_coordinator.ThumbnailBytes![0], Is.EqualTo(1));
        }

        [Test]
        public void CurrentMapping_MutatingReturnedConfig_DoesNotCorruptCoordinator()
        {
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var mapping = _coordinator.CurrentMapping!;
            mapping.NameProperty = "POISONED";

            Assert.That(_coordinator.CurrentMapping!.NameProperty, Is.EqualTo(Mapping.NameProperty));
        }

        // ── Dispose ──────────────────────────────────────────────────────────

        [Test]
        public void Dispose_ClearsSessionAndStopsChangedEvents()
        {
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var fired = 0;
            _coordinator.Changed += (_, __) => fired++;

            _coordinator.Dispose();

            Assert.That(_coordinator.FetchedPart, Is.Null);
            _coordinator.UpdateDocument();
            _coordinator.UpdateClient(_client);
            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public async Task Dispose_MidFlightFetch_CompletionIsStale()
        {
            _client.DeferGetPartsByIpn = true;
            SeedIpnDocument();
            _coordinator.UpdateDocument();

            var fetch = _coordinator.FetchAsync("R-10K-0402");
            _coordinator.Dispose();
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart> { SamplePart });

            var result = await fetch;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
        }
    }
}
