namespace SwInventreeAddin
{
    /// <summary>
    /// An opaque stamp capturing the coordinator's state at one point in time:
    /// the document generation, the lifecycle revision (client/mapping/dispose),
    /// and the session-family request order. Public type, internal payload —
    /// callers hold it, never inspect it.
    /// </summary>
    /// <remarks>
    /// Two token kinds share this shape: session-family tokens (minted by
    /// Fetch and BeginCreatePart, each bumping the family order so an older
    /// completion can never overwrite a newer operation) and scoped tokens
    /// (captured by Push/image/confirmation operations at the current order —
    /// a later family mint stales them without them superseding each other).
    /// </remarks>
    public sealed class PartSyncOperationToken
    {
        internal PartSyncOperationToken(int documentGeneration, int lifecycleRevision, int familyOrder)
        {
            DocumentGeneration = documentGeneration;
            LifecycleRevision = lifecycleRevision;
            FamilyOrder = familyOrder;
        }

        internal int DocumentGeneration { get; }
        internal int LifecycleRevision { get; }
        internal int FamilyOrder { get; }
    }

    /// <summary>
    /// Opaque correlation token carried by every confirmation
    /// <see cref="PartSyncResult"/> and required by
    /// <c>IPartSyncCoordinator.ResumeConfirmationAsync</c>, so an approval can
    /// never land on a different pending operation than the one the user saw.
    /// </summary>
    public sealed class PartSyncConfirmationHandle
    {
        internal PartSyncConfirmationHandle(long id) => Id = id;

        internal long Id { get; }
    }
}
