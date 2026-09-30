using System.Threading.Tasks;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Bom
{
    /// <summary>
    /// The narrow workflow/context seam <see cref="BomCompareReadinessCheck"/>
    /// consumes: one coherent readiness snapshot plus the two explicit
    /// coordinator operations the pre-flight may invoke. Replaces
    /// IBomReadinessSource's property-and-command mixture.
    /// </summary>
    /// <remarks>
    /// The production adapter (<see cref="CoordinatorBomReadinessContext"/>)
    /// refreshes document values through the coordinator's light document
    /// refresh and marshals onto the host STA thread internally, so every
    /// member is callable from a thread-pool continuation — the check never
    /// touches SolidWorks COM itself.
    /// </remarks>
    internal interface IBomReadinessContext
    {
        /// <summary>
        /// Captures one coherent readiness snapshot: document identity and
        /// mapped values refreshed from SolidWorks plus the current session
        /// projection, as of a single STA read.
        /// </summary>
        BomReadinessSnapshot CaptureSnapshot();

        /// <summary>
        /// Ensures a Part Sync session is populated for the active document —
        /// a no-op when one is current, otherwise the coordinator's
        /// document-identity-addressed fetch. Confirmation outcomes propagate
        /// so the caller can prompt and resume.
        /// </summary>
        Task<PartSyncResult> EnsurePartPopulatedAsync();

        /// <summary>
        /// Pushes the SolidWorks revision to InvenTree — an explicit command
        /// invoked only after the caller has confirmed a
        /// <see cref="BomCompareOutcome.SwIsNewer"/> readiness result.
        /// </summary>
        Task<PartSyncResult> PushRevisionAsync();
    }
}
