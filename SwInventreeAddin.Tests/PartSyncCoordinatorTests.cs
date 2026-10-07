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
        private IPartSyncCoordinator _coordinator = null!;

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

        private void SeedPkDocument(int? pk = null)
        {
            _propertyService.Seed(Mapping.PkProperty!, (pk ?? SamplePart.Pk).ToString());
        }

        /// <summary>Runs the full document evaluation and installs a fetched session.</summary>
        private async Task<PartSyncResult> InstallSessionViaFetch(string ipn = "R-10K-0402")
        {
            _propertyService.Seed(Mapping.IpnProperty!, ipn);
            _coordinator.UpdateDocument();
            return await _coordinator.FetchAsync(ipn);
        }

        /// <summary>
        /// Spins until <paramref name="until"/> holds or the 10 s deadline
        /// expires; <paramref name="describe"/> names the awaited condition in
        /// the failure message.
        /// </summary>
        private void WaitFor(Func<bool> until, Func<string> describe)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!until())
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail($"Timed out waiting for {describe()}.");
                System.Threading.Thread.Sleep(5);
            }
        }

        /// <summary>
        /// Spins until the coordinator has parked <paramref name="expected"/>
        /// commits on the deferred dispatcher. Deferred client calls run their
        /// continuations asynchronously, so the commit lands in the queue a
        /// beat after <c>PendingCall.Complete</c> returns — asserting or
        /// draining the queue without this wait races the continuation.
        /// </summary>
        private void WaitForQueuedCommits(int expected = 1) =>
            WaitFor(
                () => _dispatcher.QueuedCount >= expected,
                () => $"{expected} parked commit(s); queue holds {_dispatcher.QueuedCount}");

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
        public async Task NotifyDocumentPropertyChanged_PkIdentityMismatch_Reevaluates()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            // PK is identity-classified before the mapped-value lookup — a
            // divergent stamp means the document now points at a different
            // part, not a user edit of a mapped field.
            var change = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.PkProperty!, "999");

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
            Assert.That(result.PartPk, Is.EqualTo(SamplePart.Pk));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
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
            IPartSyncCoordinator coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, client: null);
            SeedIpnDocument();
            coordinator.UpdateDocument();

            var result = await coordinator.FetchAsync("R-10K-0402");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        [Test]
        public async Task FetchAsync_UnhealthyMapping_ReturnsInvalidOperation()
        {
            var provider = new StubPropertyMappingProvider { Health = MappingHealth.Invalid };
            IPartSyncCoordinator coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, _client, provider);
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
            Assert.That(_client.LastGetPartByPkPk, Is.EqualTo(SamplePart.Pk));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
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
            { Pk = SamplePart.Pk, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.LinkMismatchConfirmation));
            Assert.That(result.Confirmation, Is.Not.Null);
            Assert.That(result.DocumentIpn, Is.EqualTo("DOC-001"));
            Assert.That(result.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
            Assert.That(_coordinator.FetchedPart, Is.Null, "no session before confirmation");
        }

        [Test]
        public async Task FetchAsync_LinkMismatch_Approved_InstallsSession()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = SamplePart.Pk, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync(string.Empty);
            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));
        }

        [Test]
        public async Task FetchAsync_LinkMismatch_Declined_ReturnsCancelledNoSession()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = SamplePart.Pk, Ipn = "RENAMED-001", Revision = "A" };
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
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));

            // The older fetch now completes — it must not overwrite the newer session.
            _client.PendingGetPartsByIpnCalls[0].Complete(
                new System.Collections.Generic.List<InventreePart>
                {
                    new InventreePart { Pk = 99, Ipn = "PART-001", Name = "stale part" },
                });
            var firstResult = await first;

            Assert.That(firstResult.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
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
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
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
        public void CompleteCreatePart_ZeroPk_ReturnsInvalidOperationNoWritesNoSession()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            // A create that came back without a server-assigned InvenTree
            // Part PK can never satisfy the session validity rules.
            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 0, Ipn = "NEW-001", Name = "New Part" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(result.Diagnostic, Is.Not.Null.And.Not.Empty);
            Assert.That(_propertyService.WriteLog, Is.Empty);
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.Kind, Is.Not.EqualTo(TaskPaneStateKind.Populated));
        }

        [Test]
        public void CompleteCreatePart_NoClient_ReturnsInvalidOperationNoWritesNoSession()
        {
            IPartSyncCoordinator coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, client: null);
            SeedIpnDocument(string.Empty);
            coordinator.UpdateDocument();
            var token = coordinator.BeginCreatePart();

            var result = coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
            Assert.That(result.Diagnostic, Is.Not.Null.And.Not.Empty);
            Assert.That(_propertyService.WriteLog, Is.Empty);
            Assert.That(coordinator.FetchedPart, Is.Null);
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
            Assert.That(_propertyService.DidWrite(Mapping.PkProperty!, SamplePart.Pk.ToString()), Is.True);
            Assert.That(_coordinator.Document!.StampedPartPk, Is.EqualTo(SamplePart.Pk));
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
        public async Task Apply_ExistingProperty_RegistersPendingEcho()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();

            _coordinator.Apply(ApplyField.Name);

            // The add-in-originated write registered its echo — SolidWorks
            // reporting our own value back is consumed without a re-read.
            var echo = _coordinator.NotifyDocumentPropertyChanged(
                Mapping.NameProperty!, "Resistor 10k");
            Assert.That(echo, Is.EqualTo(PartSyncPropertyChange.EchoConsumed));
        }

        [Test]
        public async Task Apply_ExistingProperty_UndeliveredSwitch_IsStaleNoWrite()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "old");
            await InstallSessionViaFetch();

            // ActiveDoc changed but the host notification has not run — the
            // commit's recapture must catch it before the old part's value
            // is written into the new document.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "Resistor 10k"), Is.False);
        }

        [Test]
        public async Task Apply_MissingProperty_UndeliveredSwitch_IsStaleNoPendingStored()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            Assert.That(_propertyService.PropertyExists(Mapping.NameProperty!), Is.False);

            // Undelivered switch — the guard runs before the missing-property
            // check, so no confirmation is parked for a superseded document.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var result = _coordinator.Apply(ApplyField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(result.Confirmation, Is.Null);
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);

            var bogus = await _coordinator.ResumeConfirmationAsync(
                new PartSyncConfirmationHandle(777), approved: true);
            Assert.That(bogus.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
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
            Assert.That(_client.LastPushedPk, Is.EqualTo(SamplePart.Pk));
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

        [Test]
        public async Task PushAsync_UndeliveredSwitchAtEntry_Stale_NoMappedRead_NoClientCall()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            // ActiveDoc switched but the host notification has not run — the
            // entry validation must discover it before the mapped-property
            // read and before any remote write.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            var readsBefore = _propertyService.ReadsOf(Mapping.NameProperty!);

            var result = await _coordinator.PushAsync(PushField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.ReadsOf(Mapping.NameProperty!),
                Is.EqualTo(readsBefore + 1),
                "the recapture's snapshot read is the only mapped-property read — the push value read never runs");
            Assert.That(_client.LastPushedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushImageAsync_UndeliveredSwitchAtEntry_Stale_NoUpload()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(0));
        }

        [Test]
        public async Task PushAsync_EntryRecaptureDiscoversSwitch_RaisesChangedOnce()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            // The entry recapture discovers the undelivered switch and drops
            // the session — the pane must refresh now, not when the delayed
            // host notification arrives.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var result = await _coordinator.PushAsync(PushField.Name);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public async Task PushAsync_CommitRecaptureDiscoversSwitch_RaisesChangedOnce()
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            _dispatcher.DeferRun = true;
            var push = _coordinator.PushAsync(PushField.Name);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public async Task ResumeConfirmationAsync_CommitRecaptureDiscoversSwitch_RaisesChangedOnce()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            var confirm = _coordinator.Apply(ApplyField.Name);
            Assert.That(confirm.Confirmation, Is.Not.Null);

            // The resume commit's recapture discovers the undelivered switch,
            // dropping both the session and the pending confirmation.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(changed, Is.EqualTo(1));
        }

        // ── Push image / thumbnail ───────────────────────────────────────────

        [Test]
        public async Task PushImageAsync_Success_UploadsAndInstallsThumbnail()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart
            { Pk = SamplePart.Pk, ThumbnailUrl = "/media/test.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 4, 5, 6 };

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(SamplePart.Pk));
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
            { Pk = SamplePart.Pk, ThumbnailUrl = "/media/test.png" };
            _client.ThrowOnDownload = new Exception("download failed");

            using var image = new Bitmap(10, 10);
            var result = await _coordinator.PushImageAsync(image, Rectangle.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.SucceededWithWarning));
            Assert.That(result.Diagnostic, Does.Contain("could not be refreshed"));
            Assert.That(_client.LastUploadedPk, Is.EqualTo(SamplePart.Pk));
        }

        [Test]
        public async Task PushImageAsync_NoThumbnailUrl_ReturnsWarning()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk };

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
            var livePart = new InventreePart { Pk = 55, Ipn = "R-10K-0402", Name = "original" };
            _client.PartToReturn = livePart;
            await InstallSessionViaFetch();
            var originalPk = livePart.Pk;
            var originalName = livePart.Name;

            // Someone mutates the object the client handed back.
            livePart.Name = "changed underneath";
            livePart.Pk = 777;

            Assert.That(_coordinator.FetchedPart!.Name, Is.EqualTo(originalName));
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(originalPk));
        }

        [Test]
        public async Task ThumbnailBytes_ReturnedCopy_NotLiveArray()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk, ThumbnailUrl = "/t.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 1, 2, 3 };
            using var image = new Bitmap(10, 10);
            await _coordinator.PushImageAsync(image, Rectangle.Empty);

            var first = _coordinator.ThumbnailBytes!;
            first[0] = 99;

            Assert.That(_coordinator.ThumbnailBytes![0], Is.EqualTo(1));
        }

        [Test]
        public async Task SessionMapping_ProviderConfigMutatedAfterInstall_SessionIsolated()
        {
            // A provider that mutates its own config instance post-install
            // must not alter a live session — the lifecycle revision does not
            // advance for an in-place mutation.
            var provider = new StubPropertyMappingProvider();
            IPartSyncCoordinator coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, _client, provider);
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.IpnProperty!, "R-10K-0402");
            _propertyService.Seed(Mapping.NameProperty!, "old");
            coordinator.UpdateDocument();
            await coordinator.FetchAsync("R-10K-0402");

            provider.Config.NameProperty = "POISONED";

            var result = coordinator.Apply(ApplyField.Name);
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "Resistor 10k"), Is.True);
            Assert.That(_propertyService.DidWrite("POISONED"), Is.False);
        }

        [Test]
        public async Task ThumbnailBytes_InstallFromFetch_CallerArrayMutationCannotReachSession()
        {
            var thumb = new byte[] { 1, 2, 3 };
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk, ThumbnailUrl = "/t.png" };
            _client.ThumbnailBytesToReturn = thumb;
            SeedPkDocument();
            _coordinator.UpdateDocument();

            await _coordinator.FetchAsync(string.Empty);
            thumb[0] = 99;

            Assert.That(_coordinator.ThumbnailBytes![0], Is.EqualTo(1));
        }

        [Test]
        public async Task ThumbnailBytes_PushImage_CallerArrayMutationCannotReachSession()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk, ThumbnailUrl = "/t.png" };
            var thumb = new byte[] { 4, 5, 6 };
            _client.ThumbnailBytesToReturn = thumb;
            using var image = new Bitmap(10, 10);

            await _coordinator.PushImageAsync(image, Rectangle.Empty);
            thumb[0] = 99;

            Assert.That(_coordinator.ThumbnailBytes![0], Is.EqualTo(4));
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

        [Test]
        public async Task Dispose_WithPopulatedSession_KindIsNotPopulated()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            Assert.That(_coordinator.Kind, Is.EqualTo(TaskPaneStateKind.Populated));

            _coordinator.Dispose();

            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(_coordinator.Kind, Is.Not.EqualTo(TaskPaneStateKind.Populated),
                "a populated marker must never survive disposal while no part is fetched");
        }

        // ── Stale commit discipline — validate + recapture before anything else ──

        [Test]
        public void CompleteCreatePart_UndeliveredDocumentSwitch_IsStaleNoWrites()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            // The host notification has not run yet — the commit's recapture
            // must discover the new active document BEFORE writing PK/IPN/Name:
            // production writes always target ISldWorks.ActiveDoc.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.DidWrite(Mapping.PkProperty!), Is.False);
            Assert.That(_propertyService.DidWrite(Mapping.IpnProperty!), Is.False);
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task PushAsync_ServerError_ParkedCommit_DocumentSwitch_IsStale()
        {
            // A stale FAILED must never leave the commit — the ViewModel would
            // turn it into an error status on the new document's pane.
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();
            _client.ThrowOnUpdate = new HttpRequestException("500");

            _dispatcher.DeferRun = true;
            var push = _coordinator.PushAsync(PushField.Name);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task PushImageAsync_UploadError_ParkedCommit_DocumentSwitch_IsStale()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.ThrowOnUpload = new HttpRequestException("500");

            _dispatcher.DeferRun = true;
            using var image = new Bitmap(10, 10);
            var push = _coordinator.PushImageAsync(image, Rectangle.Empty);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task ResumeConfirmationAsync_MissingProperty_UndeliveredSwitch_IsStaleNoWrite()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            var confirm = _coordinator.Apply(ApplyField.Name);
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.MissingPropertyConfirmation));

            // Undelivered switch — the resume commit's recapture must catch it
            // before _session.Apply writes the approved property.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
        }

        [Test]
        public async Task ResumeConfirmationAsync_DuplicateIpn_UndeliveredSwitch_IsStaleBeforeDownload()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B", ThumbnailUrl = "/t-11.png" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A", ThumbnailUrl = "/t-12.png" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync("PART-001");
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateIpnConfirmation));
            Assert.That(_client.DownloadImageCallCount, Is.EqualTo(0),
                "a multi-candidate fetch downloads no thumbnail");

            // Undelivered switch — the resume's marshalled validation +
            // recapture must discover it BEFORE the candidate thumbnail
            // download is issued.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_client.DownloadImageCallCount, Is.EqualTo(0),
                "a stale resume must be rejected before any download call is issued");
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task ResumeConfirmationAsync_Decline_AllStateAccessIsMarshalled()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            var confirm = _coordinator.Apply(ApplyField.Name);
            Assert.That(confirm.Confirmation, Is.Not.Null);

            _dispatcher.DeferRun = true;
            var resumed = _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: false);

            // Even the cheap decline path crosses the dispatcher — nothing
            // reads or clears the pending slot on the caller's thread.
            WaitForQueuedCommits();
            _dispatcher.RunAll();

            var result = await resumed;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Cancelled));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
        }

        [Test]
        public async Task ResumeConfirmationAsync_ForeignHandle_AllStateAccessIsMarshalled()
        {
            _dispatcher.DeferRun = true;
            var resumed = _coordinator.ResumeConfirmationAsync(
                new PartSyncConfirmationHandle(9999), approved: true);

            WaitForQueuedCommits();
            _dispatcher.RunAll();

            var result = await resumed;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        [Test]
        public async Task ResumeConfirmationAsync_PoolContinuationCaller_SameResultAsStaCaller()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            var confirm = _coordinator.Apply(ApplyField.Name);

            var result = await Task.Run(() =>
                _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true));

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!, "Resistor 10k"), Is.True);
        }

        [Test]
        public async Task FetchAsync_DuplicateIpn_Candidates_AreNotCastableToMutableList()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("PART-001");

            Assert.That(result.Candidates, Is.Not.Null);
            Assert.That(result.Candidates as System.Collections.Generic.List<PartSnapshot>, Is.Null,
                "the public candidate set must not be castable back to the mutable List " +
                "the pending confirmation validates against");
        }

        [Test]
        public void UpdateDocument_AlreadyEmpty_DoesNotAdvanceGeneration()
        {
            // Ruling pin: an already-empty light capture clears zero times —
            // a second ClearDocument would double-advance the generation and
            // poison every token minted against the current one.
            _propertyService.DocumentTypeToReturn = DocumentType.Unknown;

            var first = _coordinator.UpdateDocument();
            var generation = _coordinator.Generation;
            var second = _coordinator.UpdateDocument();

            Assert.That(first, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(second, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(_coordinator.Generation, Is.EqualTo(generation));

            // The next real activation still lands exactly one generation on.
            _propertyService.DocumentTypeToReturn = DocumentType.Part;
            SeedIpnDocument();
            _coordinator.UpdateDocument();
            Assert.That(_coordinator.Generation, Is.EqualTo(generation + 1));
        }

        // ── Stale-matrix coverage ────────────────────────────────────────────

        public enum LifecycleInvalidation { ClientReplaced, MappingReplaced, Disposed }

        private void Invalidate(LifecycleInvalidation kind)
        {
            switch (kind)
            {
                case LifecycleInvalidation.ClientReplaced:
                    _coordinator.UpdateClient(new StubInventreeClient());
                    break;
                case LifecycleInvalidation.MappingReplaced:
                    _coordinator.UpdateMapping(new StubPropertyMappingProvider());
                    break;
                case LifecycleInvalidation.Disposed:
                    _coordinator.Dispose();
                    break;
            }
        }

        [Test]
        public async Task PushAsync_ParkedCommit_LifecycleInvalidation_IsStale(
            [Values] LifecycleInvalidation kind)
        {
            _client.PartToReturn = SamplePart;
            _propertyService.Seed(Mapping.NameProperty!, "New Name");
            await InstallSessionViaFetch();

            _dispatcher.DeferRun = true;
            var push = _coordinator.PushAsync(PushField.Name);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            Invalidate(kind);
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart?.Name ?? "Resistor 10k",
                Is.EqualTo("Resistor 10k"), "the pushed value is never committed");
        }

        [Test]
        public async Task PushImageAsync_ParkedCommit_LifecycleInvalidation_IsStale(
            [Values] LifecycleInvalidation kind)
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk, ThumbnailUrl = "/t.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 1, 2, 3 };

            _dispatcher.DeferRun = true;
            using var image = new Bitmap(10, 10);
            var push = _coordinator.PushImageAsync(image, Rectangle.Empty);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            Invalidate(kind);
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.ThumbnailBytes, Is.Null,
                "the downloaded thumbnail is never installed");
        }

        [Test]
        public void CompleteCreatePart_LifecycleInvalidation_RejectsWithoutWrites(
            [Values] LifecycleInvalidation kind)
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            Invalidate(kind);

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" });

            var expected = kind == LifecycleInvalidation.Disposed
                ? PartSyncOutcome.InvalidOperation
                : PartSyncOutcome.Stale;
            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(_propertyService.WriteLog, Is.Empty);
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task ResumeConfirmationAsync_ParkedCommit_LifecycleInvalidation_IsStale(
            [Values] LifecycleInvalidation kind)
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            var confirm = _coordinator.Apply(ApplyField.Name);
            Assert.That(confirm.Confirmation, Is.Not.Null);

            _dispatcher.DeferRun = true;
            var resumed = _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            // Gate hop parks; the resume commit parks behind it.
            WaitForQueuedCommits();
            _dispatcher.RunAll();
            WaitForQueuedCommits();

            Invalidate(kind);
            _dispatcher.RunAll();

            var result = await resumed;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_propertyService.DidWrite(Mapping.NameProperty!), Is.False);
        }

        [Test]
        public async Task ResumeConfirmationAsync_LinkMismatch_UndeliveredSwitch_IsStaleNoSession()
        {
            _client.PartByPkToReturn = new InventreePart
            { Pk = SamplePart.Pk, Ipn = "RENAMED-001", Revision = "A" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.IpnProperty!, "DOC-001");
            _coordinator.UpdateDocument();
            var confirm = await _coordinator.FetchAsync(string.Empty);
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.LinkMismatchConfirmation));

            // Undelivered switch — the resume's recapture must catch it
            // before the link is rewritten and the session installed.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var resumed = await _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task PushImageAsync_Success_ParkedCommit_DocumentSwitch_IsStaleNoThumbnail()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.PartByPkToReturn = new InventreePart { Pk = SamplePart.Pk, ThumbnailUrl = "/t.png" };
            _client.ThumbnailBytesToReturn = new byte[] { 4, 5, 6 };

            _dispatcher.DeferRun = true;
            using var image = new Bitmap(10, 10);
            var push = _coordinator.PushImageAsync(image, Rectangle.Empty);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.ThumbnailBytes, Is.Null,
                "commit validation precedes the thumbnail install — the download is dropped");
        }

        [Test]
        public async Task PushImageAsync_Warning_ParkedCommit_DocumentSwitch_IsStaleNoWarning()
        {
            _client.PartToReturn = SamplePart;
            await InstallSessionViaFetch();
            _client.ThrowOnDownload = new Exception("download failed");

            _dispatcher.DeferRun = true;
            using var image = new Bitmap(10, 10);
            var push = _coordinator.PushImageAsync(image, Rectangle.Empty);
            Assert.That(_dispatcher.QueuedCount, Is.EqualTo(1));

            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _coordinator.UpdateDocument();
            _dispatcher.RunAll();

            var result = await push;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale),
                "a stale warning must never surface on the new document's pane");
        }

        [Test]
        public async Task ResumeConfirmationAsync_DuplicateIpn_SwitchInsideDownloadWindow_IsStale()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B", ThumbnailUrl = "/t-11.png" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A", ThumbnailUrl = "/t-12.png" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();
            var confirm = await _coordinator.FetchAsync("PART-001");
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.DuplicateIpnConfirmation));

            _client.DeferDownloadImage = true;
            var resumed = _coordinator.ResumeConfirmationAsync(confirm.Confirmation!, approved: true);

            // The pre-download validation already passed; a switch landing
            // inside the download window is caught by the post-download commit.
            WaitFor(
                () => _client.PendingDownloadImageCalls.Count > 0,
                () => "the candidate thumbnail download");
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";
            _client.PendingDownloadImageCalls[0].Complete(new byte[] { 1, 2, 3 });

            var result = await resumed;
            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
        }

        [Test]
        public async Task FetchAsync_DuplicateIpn_Candidates_IListMutationIsRejected()
        {
            _client.PartsByIpnToReturn = new System.Collections.Generic.List<InventreePart>
            {
                new InventreePart { Pk = 11, Ipn = "PART-001", Revision = "B" },
                new InventreePart { Pk = 12, Ipn = "PART-001", Revision = "A" },
            };
            SeedIpnDocument("PART-001", "B");
            _coordinator.UpdateDocument();

            var result = await _coordinator.FetchAsync("PART-001");
            var mutable = result.Candidates as System.Collections.Generic.IList<PartSnapshot>;

            Assert.That(mutable, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => mutable!.Add(result.MatchedCandidate!));
            Assert.Throws<NotSupportedException>(() => mutable!.RemoveAt(0));
            Assert.That(result.Candidates!.Count, Is.EqualTo(2));
            Assert.That(result.Candidates![0].Pk, Is.EqualTo(11));
        }

        [Test]
        public async Task FetchAsync_StampedPk_NullIpnProperty_NoWriteBackFetchSucceeds()
        {
            var provider = new StubPropertyMappingProvider();
            provider.Config.IpnProperty = null;
            IPartSyncCoordinator coordinator = new PartSyncCoordinator(_propertyService, _dispatcher, _client, provider);
            _client.PartByPkToReturn = SamplePart;
            SeedPkDocument();
            coordinator.UpdateDocument();

            var result = await coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(result.Ipn, Is.Null);
            Assert.That(_propertyService.WriteLog, Is.Empty,
                "no IPN write-back is attempted when the mapping has no IPN property");
            Assert.That(coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
        }

        // ── Switch discovered at Fetch entry or inside a Document Property write (#317) ──

        [Test]
        public async Task FetchAsync_EntryRecaptureDiscoversSwitch_IpnPath_StaleNoFetchNoSession()
        {
            SeedIpnDocument("OLD-IPN");
            _coordinator.UpdateDocument();

            // A document switch is already in effect but its host notification
            // never ran — Fetch's own entry recapture discovers it, which makes
            // the caller-supplied IPN untrusted: it was captured against the
            // superseded document.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var result = await _coordinator.FetchAsync("OLD-IPN");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_client.LastIpnRequested, Is.Empty,
                "the stale IPN argument must never reach a fetch");
            Assert.That(_client.PendingGetPartsByIpnCalls, Is.Empty);
            Assert.That(_coordinator.FetchedPart, Is.Null);

            // No session was installed — no Apply or Push can ride it.
            Assert.That(_coordinator.Apply(ApplyField.Name).Outcome,
                Is.EqualTo(PartSyncOutcome.InvalidOperation));
        }

        [Test]
        public async Task FetchAsync_EntryRecaptureDiscoversSwitch_StampedPk_StillFetchesByPk()
        {
            SeedPkDocument();
            _coordinator.UpdateDocument();
            _client.PartByPkToReturn = SamplePart;

            // Same undelivered switch, but the new document carries a stamped
            // InvenTree Part PK — the PK path never reads the untrusted IPN
            // argument, so the fetch proceeds.
            _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var result = await _coordinator.FetchAsync("STALE-IPN");

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Success));
            Assert.That(_client.LastGetPartByPkPk, Is.EqualTo(SamplePart.Pk));
            Assert.That(_client.LastIpnRequested, Is.Empty);
            Assert.That(_coordinator.FetchedPart!.Pk, Is.EqualTo(SamplePart.Pk));
        }

        [Test]
        public void CompleteCreatePart_SwitchInsidePropertyWrite_StaleNoSession_RaisesChanged()
        {
            SeedIpnDocument(string.Empty);
            _coordinator.UpdateDocument();
            var token = _coordinator.BeginCreatePart();

            // COM reentrancy inside the synchronous Document Property write
            // flips the active document mid-commit — the post-write refresh is
            // the first place that can discover it.
            _propertyService.OnSetCustomProperty = (_, __) =>
                _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var result = _coordinator.CompleteCreatePart(
                token, new InventreePart { Pk = 99, Ipn = "NEW-001", Name = "New Part" });

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(changed, Is.EqualTo(1),
                "the abandoned commit must notify observers — not wait for the delayed host notification");
        }

        [Test]
        public async Task FetchAsync_StampedPk_IpnWriteBack_SwitchInsideWrite_StaleNoSession()
        {
            // Blank document IPN so the PK-path fetch runs the IPN write-back —
            // the write's COM reentrancy flips the active document.
            SeedPkDocument();
            _coordinator.UpdateDocument();
            _client.PartByPkToReturn = SamplePart;

            _propertyService.OnSetCustomProperty = (_, __) =>
                _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var result = await _coordinator.FetchAsync(string.Empty);

            Assert.That(result.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(changed, Is.EqualTo(2),
                "the family-mint raise plus the post-write abandon raise");
        }

        [Test]
        public async Task ResumeConfirmation_LinkMismatch_SwitchInsideWriteBack_StaleNoSession()
        {
            // Stamped PK, no document IPN, a revision divergence — the PK fetch
            // surfaces a Link Mismatch confirmation; approving it rewrites the
            // document link through WriteBackIpnIfNeeded.
            _client.PartByPkToReturn = new InventreePart
            { Pk = SamplePart.Pk, Ipn = "RENAMED-001", Revision = "B" };
            SeedPkDocument();
            _propertyService.Seed(Mapping.RevisionProperty!, "A");
            _coordinator.UpdateDocument();

            var confirm = await _coordinator.FetchAsync(string.Empty);
            Assert.That(confirm.Outcome, Is.EqualTo(PartSyncOutcome.LinkMismatchConfirmation));

            // A COM-reentrant switch inside the write-back must abandon the
            // commit under the new generation — no session, no Success.
            _propertyService.OnSetCustomProperty = (_, __) =>
                _propertyService.ActiveDocumentTokenToReturn = "doc-2";

            var changed = 0;
            _coordinator.Changed += (_, __) => changed++;

            var resumed = await _coordinator.ResumeConfirmationAsync(
                confirm.Confirmation!, approved: true);

            Assert.That(resumed.Outcome, Is.EqualTo(PartSyncOutcome.Stale));
            Assert.That(_coordinator.FetchedPart, Is.Null);
            Assert.That(changed, Is.EqualTo(1));
        }
    }
}
