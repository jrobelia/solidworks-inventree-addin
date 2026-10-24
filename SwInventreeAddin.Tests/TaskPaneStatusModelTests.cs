using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class TaskPaneStatusModelTests
    {
        private TaskPaneStatusModel _model = null!;

        [SetUp]
        public void SetUp() => _model = new TaskPaneStatusModel();

        [Test]
        public void Publish_PersistentEntry_ProjectsEntry()
        {
            var entry = new StatusEntry("Review Property Mapping", StatusSeverity.Warning,
                StatusPersistence.Persistent, "MappingHealth");

            _model.Publish(entry);

            Assert.That(_model.Current, Is.SameAs(entry));
        }

        [TestCase(StatusSeverity.Error, StatusSeverity.Warning)]
        [TestCase(StatusSeverity.Warning, StatusSeverity.None)]
        [TestCase(StatusSeverity.Success, StatusSeverity.None)]
        public void Publish_LowerSeverityPersistentEntry_DoesNotDisplaceHigherSeverity(
            StatusSeverity higherSeverity, StatusSeverity lowerSeverity)
        {
            var higher = new StatusEntry("Needs attention", higherSeverity,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(higher);

            _model.Publish(new StatusEntry("Document guidance", lowerSeverity,
                StatusPersistence.Persistent, "Document"));

            Assert.That(_model.Current, Is.SameAs(higher));
        }

        [TestCase(StatusSeverity.None, false)]
        [TestCase(StatusSeverity.Success, false)]
        [TestCase(StatusSeverity.Warning, false)]
        [TestCase(StatusSeverity.Error, false)]
        [TestCase(StatusSeverity.None, true)]
        [TestCase(StatusSeverity.Success, true)]
        [TestCase(StatusSeverity.Warning, true)]
        [TestCase(StatusSeverity.Error, true)]
        public void Publish_TransientAtAnySeverity_ReignsOverPersistentError(
            StatusSeverity severity, bool persistentPublishedLast)
        {
            var persistent = new StatusEntry("Invalid Property Mapping", StatusSeverity.Error,
                StatusPersistence.Persistent, "MappingHealth");
            var transient = new StatusEntry("Action feedback", severity,
                StatusPersistence.Transient, "Fetch");

            _model.Publish(persistentPublishedLast ? transient : persistent);
            _model.Publish(persistentPublishedLast ? persistent : transient);

            Assert.That(_model.Current, Is.SameAs(transient));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Success)]
        [TestCase(StatusSeverity.Warning)]
        public void InvalidateTransients_ReigningFeedbackEnds_RestoresPersistentEntry(StatusSeverity severity)
        {
            var persistent = new StatusEntry("Invalid Property Mapping", StatusSeverity.Error,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(persistent);
            _model.Publish(new StatusEntry("Action feedback", severity,
                StatusPersistence.Transient, "Fetch"));

            _model.InvalidateTransients();

            Assert.That(_model.Current, Is.SameAs(persistent));
        }

        [Test]
        public void Publish_TransientSuppressesMultipleFacts_ToolTipListsSuppressedEntries()
        {
            _model.Publish(new StatusEntry("Review Property Mapping", StatusSeverity.Warning,
                StatusPersistence.Persistent, "MappingHealth"));
            _model.Publish(new StatusEntry("Connection failed", StatusSeverity.Error,
                StatusPersistence.Persistent, "Connection"));

            _model.Publish(new StatusEntry("Fetching from InvenTree", StatusSeverity.None,
                StatusPersistence.Transient, "Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo("Connection: Connection failed" + System.Environment.NewLine
                + "Property Mapping: Review Property Mapping"));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Success)]
        [TestCase(StatusSeverity.Warning)]
        public void Publish_NextActionFeedback_DecaysEarlierNonErrorTransient(StatusSeverity severity)
        {
            _model.Publish(new StatusEntry("Earlier action", severity,
                StatusPersistence.Transient, "Fetch"));

            _model.Publish(new StatusEntry("Name applied", StatusSeverity.Success,
                StatusPersistence.Transient, "Apply.Name"));

            Assert.That(_model.ToolTip, Is.Null);
        }

        [TestCase(StatusPersistence.Transient)]
        [TestCase(StatusPersistence.Persistent)]
        public void Resolve_ErrorConditionRemedied_RevealsUnrelatedFact(StatusPersistence persistence)
        {
            var guidance = new StatusEntry("Review Property Mapping", StatusSeverity.Warning,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(guidance);
            _model.Publish(new StatusEntry("Fetch failed", StatusSeverity.Error, persistence, "Fetch"));
            _model.Publish(new StatusEntry("Name applied", StatusSeverity.Success,
                StatusPersistence.Transient, "Apply.Name"));
            _model.InvalidateTransients();

            _model.Resolve("Fetch");

            Assert.That(_model.Current, Is.SameAs(guidance));
        }

        [TestCase(StatusPersistence.Persistent, StatusSeverity.Warning)]
        [TestCase(StatusPersistence.Persistent, StatusSeverity.Error)]
        [TestCase(StatusPersistence.Transient, StatusSeverity.Error)]
        public void Publish_SameDomainReportsUpdatedCondition_RetiresItsPreviousEntry(
            StatusPersistence persistence, StatusSeverity severity)
        {
            _model.Publish(new StatusEntry("Earlier condition", severity, persistence, "MappingHealth"));

            _model.Publish(new StatusEntry("Current condition", severity, persistence, "MappingHealth"));

            Assert.That(_model.ToolTip, Is.Null);
        }

        [Test]
        public void Publish_SuccessfulRetry_ResolvesOnlyItsOwnError()
        {
            var unrelated = new StatusEntry("Notes Push failed", StatusSeverity.Error,
                StatusPersistence.Transient, "Push.Notes");
            _model.Publish(unrelated);
            _model.Publish(new StatusEntry("Name Push failed", StatusSeverity.Error,
                StatusPersistence.Transient, "Push.Name"));

            _model.Publish(new StatusEntry("Name pushed", StatusSeverity.Success,
                StatusPersistence.Transient, "Push.Name"));
            _model.InvalidateTransients();

            Assert.That(_model.Current, Is.SameAs(unrelated));
        }

        [Test]
        public void Resolve_DocumentDomain_ResolvesOnlyDocumentEntries()
        {
            var mapping = new StatusEntry("Invalid Property Mapping", StatusSeverity.Error,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(mapping);
            _model.Publish(new StatusEntry("Fetch failed", StatusSeverity.Error,
                StatusPersistence.Transient, "Document.Fetch"));
            _model.Publish(new StatusEntry("Name Push failed", StatusSeverity.Error,
                StatusPersistence.Transient, "Document.Push.Name"));

            _model.Resolve("Document");

            Assert.That(_model.Current, Is.SameAs(mapping));
        }

        [Test]
        public void Publish_EntryWithDetail_ToolTipIncludesDetailAndSuppressedEntries()
        {
            _model.Publish(new StatusEntry("Connection needs attention", StatusSeverity.Warning,
                StatusPersistence.Persistent, "Connection"));

            _model.Publish(new StatusEntry("Invalid Property Mapping", StatusSeverity.Error,
                StatusPersistence.Persistent, "MappingHealth", "Expanded mapping explanation"));

            Assert.That(_model.ToolTip, Is.EqualTo("Expanded mapping explanation"
                + System.Environment.NewLine + "Connection: Connection needs attention"));
        }

        [Test]
        public void Publish_SameDomainWithDifferentPersistence_RetainsFactThroughTransientReign()
        {
            var persistent = new StatusEntry("Review Property Mapping", StatusSeverity.Warning,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(persistent);
            _model.Publish(new StatusEntry("Review started", StatusSeverity.None,
                StatusPersistence.Transient, "MappingHealth"));
            var suppressed = _model.ToolTip;

            _model.InvalidateTransients();

            Assert.That((suppressed, _model.Current),
                Is.EqualTo(("Property Mapping: " + persistent.Text, persistent)));
        }

        [TestCase(StatusPersistence.Transient)]
        [TestCase(StatusPersistence.Persistent)]
        public void InvalidateTransients_SuppressedError_ReemergesUntilRemedied(StatusPersistence persistence)
        {
            var error = new StatusEntry("Condition remains", StatusSeverity.Error, persistence, "Fetch");
            _model.Publish(error);
            _model.Publish(new StatusEntry("Name applied", StatusSeverity.Success,
                StatusPersistence.Transient, "Apply.Name"));

            _model.InvalidateTransients();

            Assert.That(_model.Current, Is.SameAs(error));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Success)]
        [TestCase(StatusSeverity.Warning)]
        [TestCase(StatusSeverity.Error)]
        public void Publish_EqualSeverityPersistentEntries_NewestWins(StatusSeverity severity)
        {
            _model.Publish(new StatusEntry("Earlier fact", severity, StatusPersistence.Persistent, "Document"));
            var newer = new StatusEntry("Newer fact", severity, StatusPersistence.Persistent, "MappingHealth");

            _model.Publish(newer);

            Assert.That(_model.Current, Is.SameAs(newer));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Warning)]
        public void Publish_RetryWithoutSuccess_DoesNotResolveItsError(StatusSeverity severity)
        {
            _model.Publish(new StatusEntry("Fetch failed", StatusSeverity.Error,
                StatusPersistence.Transient, "Fetch"));

            _model.Publish(new StatusEntry("Retry feedback", severity, StatusPersistence.Transient, "Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo("Fetch: Fetch failed"));
        }

        [TestCase("Documentary")]
        [TestCase("document.Fetch")]
        [TestCase("OtherDocument.Fetch")]
        public void Resolve_DifferentDomain_DoesNotResolveUnrelatedEntry(string unrelatedDomain)
        {
            var unrelated = new StatusEntry("Condition remains", StatusSeverity.Error,
                StatusPersistence.Transient, unrelatedDomain);
            _model.Publish(unrelated);

            _model.Resolve("Document");

            Assert.That(_model.Current, Is.SameAs(unrelated));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Success)]
        [TestCase(StatusSeverity.Warning)]
        public void InvalidateTransients_NonErrorPersistentEntry_RetainsFact(StatusSeverity severity)
        {
            var persistent = new StatusEntry("Fact remains", severity, StatusPersistence.Persistent, "Document");
            _model.Publish(persistent);
            _model.Publish(new StatusEntry("Fetching", StatusSeverity.None, StatusPersistence.Transient, "Fetch"));

            _model.InvalidateTransients();

            Assert.That(_model.Current, Is.SameAs(persistent));
        }

        [Test]
        public void InvalidateTransients_NoRemainingEntry_ProjectsBlankState()
        {
            _model.Publish(new StatusEntry("Action complete", StatusSeverity.Success,
                StatusPersistence.Transient, "Fetch"));

            _model.InvalidateTransients();

            Assert.That((_model.Current, _model.ToolTip), Is.EqualTo(((StatusEntry?)null, (string?)null)));
        }

        [Test]
        public void Resolve_ReigningTransient_RemainingFactReemerges()
        {
            var persistent = new StatusEntry("Review Property Mapping", StatusSeverity.Warning,
                StatusPersistence.Persistent, "MappingHealth");
            _model.Publish(persistent);
            _model.Publish(new StatusEntry("Fetching", StatusSeverity.None, StatusPersistence.Transient, "Fetch"));

            _model.Resolve("Fetch");

            Assert.That(_model.Current, Is.SameAs(persistent));
        }

        [Test]
        public void Publish_TwoSuppressedPushFailures_ToolTipLabelsEachByAction()
        {
            const string transport = "Error: An error occurred while sending the request.";
            _model.Publish(new StatusEntry(transport, StatusSeverity.Error,
                StatusPersistence.Transient, "Document.Push.Name"));
            _model.Publish(new StatusEntry(transport, StatusSeverity.Error,
                StatusPersistence.Transient, "Document.Push.Notes"));

            _model.Publish(new StatusEntry("Fetching from InvenTree", StatusSeverity.None,
                StatusPersistence.Transient, "Document.Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo("Notes Push: " + transport
                + System.Environment.NewLine + "Name Push: " + transport));
        }

        [TestCase("Document.Fetch", "Fetch")]
        [TestCase("Document.Apply.Name", "Name Apply")]
        [TestCase("Document.Apply.Notes", "Notes Apply")]
        [TestCase("Document.Apply.Description", "Description Apply")]
        [TestCase("Document.Apply.Pk", "PK Apply")]
        [TestCase("Document.Push.Name", "Name Push")]
        [TestCase("Document.Push.Notes", "Notes Push")]
        [TestCase("Document.Push.Description", "Description Push")]
        [TestCase("Document.Push.Revision", "Revision Push")]
        [TestCase("Document.PushImage", "Image Push")]
        [TestCase("Document.CreatePart", "Create Part")]
        [TestCase("Configuration.Save", "Settings Save")]
        [TestCase("MappingHealth", "Property Mapping")]
        [TestCase("Configuration", "Settings")]
        [TestCase("Document.Guidance", "Document")]
        public void Publish_SuppressedEntryWithKnownDomain_ToolTipUsesVocabularyLabel(
            string domain, string label)
        {
            _model.Publish(new StatusEntry("Needs attention", StatusSeverity.Warning,
                StatusPersistence.Persistent, domain));

            _model.Publish(new StatusEntry("Working", StatusSeverity.None,
                StatusPersistence.Transient, "Probe"));

            Assert.That(_model.ToolTip, Is.EqualTo(label + ": Needs attention"));
        }

        [TestCase(StatusSeverity.None)]
        [TestCase(StatusSeverity.Success)]
        [TestCase(StatusSeverity.Warning)]
        [TestCase(StatusSeverity.Error)]
        public void Publish_SuppressedEntryAtAnySeverity_ToolTipCarriesLabel(StatusSeverity severity)
        {
            _model.Publish(new StatusEntry("Fact text", severity,
                StatusPersistence.Persistent, "Document.PushImage"));

            _model.Publish(new StatusEntry("Working", StatusSeverity.None,
                StatusPersistence.Transient, "Document.Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo("Image Push: Fact text"));
        }

        [Test]
        public void Publish_SuppressedEntryWithUnknownDomain_ToolTipLabelsByLastSegment()
        {
            _model.Publish(new StatusEntry("Obscure fact", StatusSeverity.Warning,
                StatusPersistence.Persistent, "Bom.Compare.Diff"));

            _model.Publish(new StatusEntry("Working", StatusSeverity.None,
                StatusPersistence.Transient, "Document.Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo("Diff: Obscure fact"));
        }

        [TestCase("Bom.Compare.Diff.", "Diff")]
        [TestCase("Bom.Compare..", "Compare")]
        [TestCase(".", ".")]
        [TestCase("..", "..")]
        public void Publish_SuppressedEntryWithDegenerateDomain_ToolTipLabelsByLastNonEmptySegment(
            string domain, string label)
        {
            _model.Publish(new StatusEntry("Obscure fact", StatusSeverity.Warning,
                StatusPersistence.Persistent, domain));

            _model.Publish(new StatusEntry("Working", StatusSeverity.None,
                StatusPersistence.Transient, "Document.Fetch"));

            Assert.That(_model.ToolTip, Is.EqualTo(label + ": Obscure fact"));
        }
    }
}
