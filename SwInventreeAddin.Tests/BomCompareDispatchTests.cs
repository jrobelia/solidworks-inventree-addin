using System;
using NUnit.Framework;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BomCompareDispatchTests
    {
        // -- Helpers ------------------------------------------------------------

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

        private static BomCompareReadiness Readiness(
            BomCompareOutcome outcome,
            string swRevision = "A",
            string itRevision = "A") =>
            new BomCompareReadiness(outcome, "PART-001", swRevision, itRevision);

        private static BomCompareAction Next(
            BomCompareOutcome outcome,
            bool revisionPushAttempted = false,
            PropertyMappingConfig? mapping = null,
            string swRevision = "A",
            string itRevision = "A") =>
            BomCompareDispatch.Next(
                Readiness(outcome, swRevision, itRevision),
                revisionPushAttempted,
                mapping ?? CreateMapping());

        // -- Terminal outcomes --------------------------------------------------

        [Test]
        public void Next_Ready_ReturnsProceed()
        {
            var action = Next(BomCompareOutcome.Ready);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.Proceed));
        }

        [Test]
        public void Next_PkNotStamped_ReturnsPkMissingWarning()
        {
            var action = Next(BomCompareOutcome.PkNotStamped);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — PK Missing"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Warning));
            Assert.That(action.Message, Is.EqualTo(
                "No InvenTree Part PK is stored in this assembly’s Document Properties.\n\n"
                + "Apply the InvenTree PK to the document first, then try again."));
        }

        [Test]
        public void Next_ItIsNewer_ReturnsOldRevisionStop()
        {
            var action = Next(BomCompareOutcome.ItIsNewer, swRevision: "A", itRevision: "B");

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Old Revision"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Stop));
            Assert.That(action.Message, Is.EqualTo(
                "InvenTree is at revision “B” but this file is revision “A”.\n\n"
                + "You have an older file open. Close it — do not push its BOM to InvenTree."));
        }

        [Test]
        public void Next_Ambiguous_ReturnsRevisionAmbiguousWarning()
        {
            var action = Next(BomCompareOutcome.Ambiguous, swRevision: "1.0", itRevision: "A");

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Revision Ambiguous"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Warning));
            Assert.That(action.Message, Is.EqualTo(
                "Revision mismatch (SolidWorks: 1.0 / InvenTree: A).\n\n"
                + "The order cannot be determined automatically. Resolve the revision manually before comparing the BOM."));
        }

        [Test]
        public void Next_Ambiguous_BlankRevisions_UsesBlankLabels()
        {
            var action = Next(BomCompareOutcome.Ambiguous, swRevision: "", itRevision: "");

            Assert.That(action.Message, Does.StartWith(
                "Revision mismatch (SolidWorks: (blank) / InvenTree: (blank))."));
        }

        [Test]
        public void Next_BomTableMissing_ReturnsShowBomTableMissing()
        {
            var action = Next(BomCompareOutcome.BomTableMissing);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowBomTableMissing));
        }

        [Test]
        public void Next_SessionNotPopulated_ReturnsStopSilently()
        {
            var action = Next(BomCompareOutcome.SessionNotPopulated);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.StopSilently));
        }

        // -- BOM column alias warning -------------------------------------------

        [Test]
        public void Next_BomColumnAliasesMissing_OneMissing_ReturnsSingularWarning()
        {
            var action = Next(
                BomCompareOutcome.BomColumnAliasesMissing,
                mapping: CreateMapping(ipnAlias: "", qtyAlias: "Qty"));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.WarnAndProceed));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Missing Alias"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Warning));
            Assert.That(action.Message, Is.EqualTo(
                "The IPN BOM Column Alias is blank.\n\n"
                + "BOM Compare will not find IPN values until it is set "
                + "in Settings > Property Mappings.\n\n"
                + "Click OK to open the comparison anyway."));
        }

        [Test]
        public void Next_BomColumnAliasesMissing_BothMissing_ReturnsPluralWarning()
        {
            var action = Next(
                BomCompareOutcome.BomColumnAliasesMissing,
                mapping: CreateMapping(ipnAlias: "", qtyAlias: ""));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.WarnAndProceed));
            Assert.That(action.Message, Is.EqualTo(
                "The IPN and Qty BOM Column Aliases are blank.\n\n"
                + "BOM Compare will not find IPN or Qty values until they are set "
                + "in Settings > Property Mappings.\n\n"
                + "Click OK to open the comparison anyway."));
        }

        // -- SwIsNewer push-recheck loop ----------------------------------------

        [Test]
        public void Next_SwIsNewer_FirstAttempt_ReturnsRevisionPushOffer()
        {
            var action = Next(BomCompareOutcome.SwIsNewer, swRevision: "B", itRevision: "A");

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.OfferRevisionPush));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Revision Mismatch"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Question));
            Assert.That(action.Message, Is.EqualTo(
                "Revision mismatch:\n  SolidWorks:  B\n  InvenTree:   A\n\n"
                + "Update InvenTree to revision “B” and proceed?"));
        }

        [Test]
        public void Next_SwIsNewer_BlankRevisions_OfferUsesBlankLabels()
        {
            var action = Next(BomCompareOutcome.SwIsNewer, swRevision: "", itRevision: "");

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.OfferRevisionPush));
            Assert.That(action.Message, Is.EqualTo(
                "Revision mismatch:\n  SolidWorks:  (blank)\n  InvenTree:   (blank)\n\n"
                + "Update InvenTree to revision “(blank)” and proceed?"));
        }

        [Test]
        public void Next_SwIsNewer_AfterPushAttempted_ReturnsStillNewerError()
        {
            var action = Next(BomCompareOutcome.SwIsNewer, revisionPushAttempted: true);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Error));
            Assert.That(action.Message, Is.EqualTo(
                "The SolidWorks revision is still newer after the update. "
                + "Close this file and pull the latest revision from InvenTree."));
        }

        // -- Fail-fast ----------------------------------------------------------

        [Test]
        public void Next_UnrecognizedOutcome_ReturnsUnrecognized()
        {
            var action = Next((BomCompareOutcome)(-1));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.Unrecognized));
            Assert.That(action.Message, Does.Contain("-1"));
        }

        [Test]
        public void Next_EveryDeclaredOutcome_MapsToARecognizedAction()
        {
            foreach (BomCompareOutcome outcome in Enum.GetValues(typeof(BomCompareOutcome)))
            {
                Assert.That(Next(outcome).Kind,
                    Is.Not.EqualTo(BomCompareActionKind.Unrecognized),
                    $"{outcome} must map to an explicit action");
                Assert.That(Next(outcome, revisionPushAttempted: true).Kind,
                    Is.Not.EqualTo(BomCompareActionKind.Unrecognized),
                    $"{outcome} must map to an explicit action");
            }
        }

        // -- AfterRevisionPush --------------------------------------------------

        [Test]
        public void AfterRevisionPush_NullResult_ReturnsStopSilently()
        {
            var action = BomCompareDispatch.AfterRevisionPush(null);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.StopSilently));
        }

        [TestCase(PartSyncOutcome.Success)]
        [TestCase(PartSyncOutcome.SucceededWithWarning)]
        public void AfterRevisionPush_SuccessFamily_ReturnsRecheck(PartSyncOutcome outcome)
        {
            var action = BomCompareDispatch.AfterRevisionPush(new PartSyncResult(outcome));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.Recheck));
        }

        [TestCase(PartSyncOutcome.Stale)]
        [TestCase(PartSyncOutcome.Cancelled)]
        public void AfterRevisionPush_StaleOrCancelled_ReturnsStopSilently(PartSyncOutcome outcome)
        {
            var action = BomCompareDispatch.AfterRevisionPush(new PartSyncResult(outcome));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.StopSilently));
        }

        [Test]
        public void AfterRevisionPush_FailedWithDiagnostic_ReturnsPushFailedMessage()
        {
            var result = new PartSyncResult(PartSyncOutcome.Failed) { Diagnostic = "server exploded" };

            var action = BomCompareDispatch.AfterRevisionPush(result);

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Revision Update Failed"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Error));
            Assert.That(action.Message, Is.EqualTo(
                $"Failed to update revision in InvenTree:{Environment.NewLine}server exploded"));
        }

        [TestCase(PartSyncOutcome.Failed, "Failed")]
        [TestCase(PartSyncOutcome.PartNotFound, "PartNotFound")]
        [TestCase(PartSyncOutcome.InvalidOperation, "InvalidOperation")]
        public void AfterRevisionPush_OtherOutcomes_ReturnOutcomeName(
            PartSyncOutcome outcome, string expectedDetail)
        {
            var action = BomCompareDispatch.AfterRevisionPush(new PartSyncResult(outcome));

            Assert.That(action.Kind, Is.EqualTo(BomCompareActionKind.ShowMessage));
            Assert.That(action.Title, Is.EqualTo("BOM Compare — Revision Update Failed"));
            Assert.That(action.Icon, Is.EqualTo(BomCompareDialogIcon.Error));
            Assert.That(action.Message, Is.EqualTo(
                $"Failed to update revision in InvenTree:{Environment.NewLine}{expectedDetail}"));
        }
    }
}
