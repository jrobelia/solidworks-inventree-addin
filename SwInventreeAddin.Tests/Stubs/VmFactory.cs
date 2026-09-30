using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// Builds the coordinator + ViewModel pair the way the production host
    /// does: one shared dispatcher, a coordinator owning the property service
    /// and Part Sync workflow, a readiness context over the coordinator, and
    /// a ViewModel projecting it. Centralising the wiring keeps the 13
    /// task-pane fixtures on the production constructor shape without each
    /// repeating it.
    /// </summary>
    internal static class VmFactory
    {
        public static PartSyncCoordinator Coordinator(
            IDocumentPropertyService propertyService,
            IInventreeClient? client = null,
            IPropertyMappingProvider? mappingProvider = null,
            IHostStaDispatcher? dispatcher = null) =>
            new PartSyncCoordinator(
                propertyService,
                dispatcher ?? new SynchronizationContextStaDispatcher(),
                client,
                mappingProvider);

        /// <summary>
        /// Builds the wired pair and runs the host's initial evaluation —
        /// <see cref="IPartSyncCoordinator.UpdateDocument"/> then
        /// <see cref="TaskPaneViewModel.ProjectDocumentUpdate"/> — matching the
        /// sequence <see cref="TaskPaneControl"/> runs at construction.
        /// </summary>
        public static TaskPanePair Create(
            IInventreeClient? client,
            IDocumentPropertyService propertyService,
            IPropertyMappingProvider? mappingProvider = null,
            IConfigProvider? configProvider = null,
            ICreatePartValidationErrorService? createPartValidator = null,
            IHostStaDispatcher? dispatcher = null)
        {
            // Mirror production wiring: the dispatcher captures the ambient
            // SynchronizationContext so off-thread commits marshal through
            // Send; with no ambient context it runs inline, like the stub.
            var d = dispatcher ?? new SynchronizationContextStaDispatcher();
            var coordinator = Coordinator(propertyService, client, mappingProvider, d);
            var vm = new TaskPaneViewModel(
                coordinator,
                new Bom.CoordinatorBomReadinessContext(coordinator, d),
                d,
                client,
                mappingProvider,
                configProvider,
                createPartValidator);
            var pair = new TaskPanePair(coordinator, vm);
            pair.UpdateDocument();
            return pair;
        }
    }

    /// <summary>
    /// The coordinator + ViewModel pair under one routing surface that mirrors
    /// <see cref="TaskPaneControl"/>: every host callback drives the
    /// coordinator lifecycle member first, then the ViewModel's projection
    /// entries — exactly the sequence production runs.
    /// </summary>
    internal sealed class TaskPanePair
    {
        public TaskPanePair(PartSyncCoordinator coordinator, TaskPaneViewModel viewModel)
        {
            Coordinator = coordinator;
            ViewModel = viewModel;
        }

        public PartSyncCoordinator Coordinator { get; }

        public TaskPaneViewModel ViewModel { get; }

        /// <summary>Active-document changed/loaded/refreshed.</summary>
        public void UpdateDocument()
        {
            Coordinator.UpdateDocument();
            ViewModel.ProjectDocumentUpdate();
        }

        /// <summary>Last document closed.</summary>
        public void CloseDocument()
        {
            Coordinator.NotifyDocumentClosed();
            ViewModel.ProjectDocumentClosed();
        }

        /// <summary>
        /// Mapped Document Property changed. A Reevaluated classification runs
        /// the full document update — the sequence TaskPaneControl performs.
        /// </summary>
        public void PropertyChanged(string name, string value)
        {
            var change = Coordinator.NotifyDocumentPropertyChanged(name, value);
            if (change == PartSyncPropertyChange.Reevaluated)
                Coordinator.UpdateDocument();
            ViewModel.ProjectPropertyChange(change);
        }

        /// <summary>Settings applied — client replaced then document re-evaluated.</summary>
        public void UpdateClient(IInventreeClient? client)
        {
            Coordinator.UpdateClient(client);
            Coordinator.UpdateDocument();
            ViewModel.UpdateClient(client);
        }

        /// <summary>Property Mapping provider replaced.</summary>
        public void UpdateMapping(IPropertyMappingProvider? provider)
        {
            Coordinator.UpdateMapping(provider);
            ViewModel.UpdateMapping(provider);
        }
    }
}
