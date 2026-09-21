using System;
using System.Collections.Generic;
using NUnit.Framework;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// Tests for <see cref="TaskPaneState"/> — the UI-free owner of the Task
    /// Pane's active-document generation, identity, and SolidWorks Document
    /// Property snapshot (issue #91, lifecycle contract in
    /// docs/agents/task-pane-lifecycle.md).
    /// </summary>
    [TestFixture]
    public class TaskPaneStateTests
    {
        private TaskPaneState _state = null!;

        [SetUp]
        public void SetUp() => _state = new TaskPaneState();

        private static TaskPaneDocumentSnapshot Snapshot(
            DocumentType documentType = DocumentType.Part,
            string ipn = "",
            string pkText = "",
            string name = "SW Name",
            string notes = "SW Notes",
            string revision = "A",
            string description = "SW Description") =>
            new TaskPaneDocumentSnapshot(
                documentType, ipn, pkText, name, notes, revision, description);

        // ── Initial state ────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsEmptyWithNoDocument()
        {
            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Empty));
            Assert.That(_state.Document, Is.Null);
        }

        [Test]
        public void InitialState_GenerationIsZero()
        {
            Assert.That(_state.Generation, Is.EqualTo(0));
        }

        // ── Activation versus refresh ────────────────────────────────────────

        [Test]
        public void ApplyDocumentUpdate_FirstDocument_ActivatesAndAdvancesGeneration()
        {
            var transition = _state.ApplyDocumentUpdate("doc-1", Snapshot());

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(_state.Generation, Is.EqualTo(1));
        }

        [Test]
        public void ApplyDocumentUpdate_SameToken_RefreshesWithoutAdvancingGeneration()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(revision: "A"));

            var transition = _state.ApplyDocumentUpdate("doc-1", Snapshot(revision: "B"));

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Refreshed));
            Assert.That(_state.Generation, Is.EqualTo(1));
        }

        [Test]
        public void ApplyDocumentUpdate_SameToken_InstallsTheNewSnapshot()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(revision: "A"));

            _state.ApplyDocumentUpdate("doc-1", Snapshot(revision: "B"));

            Assert.That(_state.Document!.Revision, Is.EqualTo("B"));
        }

        [Test]
        public void ApplyDocumentUpdate_DifferentToken_ActivatesAndAdvancesGeneration()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot());

            var transition = _state.ApplyDocumentUpdate("doc-2", Snapshot());

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
            Assert.That(_state.Generation, Is.EqualTo(2));
        }

        [Test]
        public void ApplyDocumentUpdate_ReusedTokenAfterClear_IsNewActivation()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot());
            _state.ClearDocument();

            var transition = _state.ApplyDocumentUpdate("doc-1", Snapshot());

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
        }

        [Test]
        public void ApplyDocumentUpdate_TokenComparison_IsOrdinal()
        {
            _state.ApplyDocumentUpdate("Doc-1", Snapshot());

            var transition = _state.ApplyDocumentUpdate("doc-1", Snapshot());

            Assert.That(transition, Is.EqualTo(TaskPaneDocumentTransition.Activated));
        }

        // ── ClearDocument ────────────────────────────────────────────────────

        [Test]
        public void ClearDocument_AfterActivation_ProducesEmptyAndAdvancesGeneration()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402"));

            _state.ClearDocument();

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Empty));
            Assert.That(_state.Document, Is.Null);
            Assert.That(_state.Generation, Is.EqualTo(2));
        }

        [Test]
        public void ClearDocument_WhenAlreadyEmpty_StillAdvancesGeneration()
        {
            // A close is an invalidation event — generation is monotonic, never reset.
            _state.ClearDocument();

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Empty));
            Assert.That(_state.Generation, Is.EqualTo(1));
        }

        // ── Kind derivation ──────────────────────────────────────────────────

        [Test]
        public void Kind_Drawing_IsUnsupported()
        {
            _state.ApplyDocumentUpdate(
                "doc-1", Snapshot(documentType: DocumentType.Drawing, ipn: "DRW-1", pkText: "42"));

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Unsupported));
            Assert.That(_state.Document, Is.Not.Null);
        }

        [Test]
        public void Kind_NoIpnNoPk_IsUnlinked()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot());

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Unlinked));
        }

        [Test]
        public void Kind_IpnOnly_IsLinked()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402"));

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [Test]
        public void Kind_PkOnly_IsLinked()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(pkText: "42"));

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [Test]
        public void Kind_IpnAndPk_IsLinked()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402", pkText: "42"));

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Linked));
        }

        [TestCase("0")]
        [TestCase("-7")]
        [TestCase("TBD")]
        public void Kind_NonPositivePkWithoutIpn_IsUnlinked(string pkText)
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(pkText: pkText));

            Assert.That(_state.Kind, Is.EqualTo(TaskPaneStateKind.Unlinked));
        }

        // ── StampedPartPk parsing (the single TryReadDocumentPk rule) ───────

        [TestCase("42", 42)]
        [TestCase("0", 0)]
        [TestCase("-7", 0)]
        [TestCase("TBD", 0)]
        [TestCase("", 0)]
        public void StampedPartPk_ParsesOnlyPositiveIntegers(string pkText, int expected)
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(pkText: pkText));

            Assert.That(_state.Document!.StampedPartPk, Is.EqualTo(expected));
            Assert.That(_state.Document!.PkText, Is.EqualTo(pkText));
        }

        [Test]
        public void Snapshot_NullValues_BecomeEmptyStrings()
        {
            var snapshot = new TaskPaneDocumentSnapshot(
                DocumentType.Part, null!, null!, null!, null!, null!, null!);

            Assert.That(snapshot.Ipn, Is.EqualTo(string.Empty));
            Assert.That(snapshot.PkText, Is.EqualTo(string.Empty));
            Assert.That(snapshot.Name, Is.EqualTo(string.Empty));
            Assert.That(snapshot.StampedPartPk, Is.EqualTo(0));
        }

        // ── Changed notification contract ────────────────────────────────────

        [Test]
        public void Changed_FiresOnceAfterTheSnapshotIsInstalled()
        {
            var count = 0;
            TaskPaneStateKind? kindInHandler = null;
            TaskPaneDocumentSnapshot? documentInHandler = null;
            _state.Changed += (_, __) =>
            {
                count++;
                kindInHandler = _state.Kind;
                documentInHandler = _state.Document;
            };

            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402"));

            Assert.That(count, Is.EqualTo(1));
            Assert.That(kindInHandler, Is.EqualTo(TaskPaneStateKind.Linked));
            Assert.That(documentInHandler!.Ipn, Is.EqualTo("R-10K-0402"));
        }

        [Test]
        public void Changed_FiresOnceOnClear()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot());
            var count = 0;
            TaskPaneStateKind? kindInHandler = null;
            _state.Changed += (_, __) => { count++; kindInHandler = _state.Kind; };

            _state.ClearDocument();

            Assert.That(count, Is.EqualTo(1));
            Assert.That(kindInHandler, Is.EqualTo(TaskPaneStateKind.Empty));
        }

        // ── Guards ───────────────────────────────────────────────────────────

        [TestCase(null)]
        [TestCase("")]
        public void ApplyDocumentUpdate_MissingToken_Throws(string? token)
        {
            Assert.Throws<ArgumentException>(
                () => _state.ApplyDocumentUpdate(token!, Snapshot()));
        }

        [Test]
        public void ApplyDocumentUpdate_UnknownDocumentType_Throws()
        {
            // Unknown means "no document" — the host routes to ClearDocument instead.
            Assert.Throws<ArgumentException>(
                () => _state.ApplyDocumentUpdate("doc-1", Snapshot(documentType: DocumentType.Unknown)));
        }

        [Test]
        public void ApplyDocumentUpdate_NullSnapshot_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _state.ApplyDocumentUpdate("doc-1", null!));
        }

        // ── Invariants ───────────────────────────────────────────────────────

        [Test]
        public void DocumentTransitions_NeverProducePopulated()
        {
            // POPULATED ownership belongs to #92's session coordinator — the
            // member exists but no document transition may produce it.
            var kinds = new List<TaskPaneStateKind>();
            _state.Changed += (_, __) => kinds.Add(_state.Kind);

            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402"));
            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "R-10K-0402", pkText: "42"));
            _state.ApplyDocumentUpdate("doc-2", Snapshot(documentType: DocumentType.Drawing));
            _state.ClearDocument();

            Assert.That(kinds, Is.EqualTo(new[]
            {
                TaskPaneStateKind.Linked,
                TaskPaneStateKind.Linked,
                TaskPaneStateKind.Unsupported,
                TaskPaneStateKind.Empty,
            }));
            Assert.That(kinds, Has.None.EqualTo(TaskPaneStateKind.Populated));
        }

        [Test]
        public void ApplyDocumentUpdate_ReplacesTheWholeSnapshot()
        {
            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "A", name: "n1"));
            var first = _state.Document;

            _state.ApplyDocumentUpdate("doc-1", Snapshot(ipn: "B", name: "n2"));

            Assert.That(_state.Document, Is.Not.SameAs(first));
            Assert.That(_state.Document!.Ipn, Is.EqualTo("B"));
            Assert.That(_state.Document!.Name, Is.EqualTo("n2"));
        }
    }
}
