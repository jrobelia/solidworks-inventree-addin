using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// Builds the coordinator + ViewModel pair the way the production host
    /// does: one shared dispatcher, a coordinator owning the property service
    /// and Part Sync workflow, and a ViewModel projecting it. Centralising the
    /// wiring keeps the 13 task-pane fixtures on the production constructor
    /// shape without each repeating it.
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

        public static TaskPaneViewModel Create(
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
            return new TaskPaneViewModel(
                Coordinator(propertyService, client, mappingProvider, d),
                d,
                client,
                mappingProvider,
                configProvider,
                createPartValidator);
        }
    }
}
