using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;
using SwInventreeAddin.UI;

namespace SwInventreeAddin
{
    /// <summary>
    /// The deep Part Sync workflow module: privately owns the current
    /// <see cref="PartSyncSession"/>, the owned <see cref="TaskPaneState"/>,
    /// the pending-write echo set, the operation-token machinery, and the
    /// single pending-confirmation slot. Coordinates Fetch, Create Part
    /// completion, Apply, Push, thumbnail upload, and BOM populate behind one
    /// narrow interface — rejecting any async completion whose captured token
    /// is no longer current.
    /// </summary>
    /// <remarks>
    /// <para><b>Operation tokens.</b> A token captures {document generation,
    /// lifecycle revision, session-family order}. Document transitions advance
    /// the generation; client replacement, mapping replacement, and disposal
    /// advance the revision; Fetch and BeginCreatePart mint a new family
    /// order. Scoped operations (Push, image, confirmations) capture the
    /// current order without bumping it, so a later family mint stales them
    /// while sequential pushes never stale each other.</para>
    /// <para><b>Commit discipline.</b> STA capture → network/image work
    /// off-thread via <c>ConfigureAwait(false)</c> → commit marshalled through
    /// <see cref="IHostStaDispatcher"/> with token validation <em>inside</em>
    /// the commit (plus a document recapture that catches a switch whose
    /// host notification has not been delivered yet). No SolidWorks Document
    /// Property is ever written and no session is ever installed by a stale
    /// completion.</para>
    /// <para><b>Immutable surface.</b> <see cref="InventreePart"/> never
    /// escapes: the read surface and result payloads expose
    /// <see cref="PartSnapshot"/> projections, incoming parts are copied on
    /// install, and the session itself stays internal.</para>
    /// </remarks>
    public sealed class PartSyncCoordinator : IPartSyncCoordinator, IDisposable
    {
        private readonly IDocumentPropertyService _propertyService;
        private readonly IHostStaDispatcher _dispatcher;
        private IInventreeClient? _client;
        private IPropertyMappingProvider? _mappingProvider;

        private readonly TaskPaneState _state = new TaskPaneState();
        private PartSyncSession? _session;

        /// <summary>
        /// Add-in-originated Document Property writes awaiting their echo:
        /// mapped property name → written value. SolidWorks reports our own
        /// writes back through the change-notification callback — possibly
        /// synchronously during the write — and a follow-up re-read can still
        /// return the pre-write value, so a matching notification is consumed
        /// without re-reading. Entries persist until their echo arrives or the
        /// document generation advances — echoes are keyed to the document
        /// that generated them, and notifications carry no identity.
        /// Shared with the installed session so every coordinator-originated
        /// write registers in one place.
        /// </summary>
        private readonly Dictionary<string, string> _pendingDocumentWrites =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private int _lifecycleRevision;
        private int _familyOrder;
        private long _nextConfirmationId;
        private PendingConfirmation? _pendingConfirmation;
        private bool _disposed;

        public PartSyncCoordinator(
            IDocumentPropertyService propertyService,
            IHostStaDispatcher dispatcher,
            IInventreeClient? client = null,
            IPropertyMappingProvider? mappingProvider = null)
        {
            _propertyService = propertyService ?? throw new ArgumentNullException(nameof(propertyService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _client = client;
            _mappingProvider = mappingProvider;
        }

        // ── Read-only state surface ──────────────────────────────────────────

        /// <inheritdoc/>
        public TaskPaneStateKind Kind => _state.Kind;

        /// <inheritdoc/>
        public TaskPaneDocumentSnapshot? Document => _state.Document;

        /// <inheritdoc/>
        public int Generation => _state.Generation;

        /// <inheritdoc/>
        public PartSnapshot? FetchedPart =>
            _session == null ? null : PartSnapshot.FromPart(_session.Part);

        /// <inheritdoc/>
        public byte[]? ThumbnailBytes
        {
            get
            {
                var bytes = _session?.ThumbnailBytes;
                return bytes == null ? null : (byte[])bytes.Clone();
            }
        }

        /// <inheritdoc/>
        public PropertyMappingConfig CurrentMapping => ResolveMapping().Clone();

        /// <inheritdoc/>
        public Uri? GetPartWebUrl() =>
            _session == null || _client == null
                ? null
                : _client.GetPartWebUrl(_session.PartPk);

        /// <inheritdoc/>
        public event EventHandler? Changed;

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        // ── Lifecycle ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public TaskPaneDocumentTransition UpdateDocument()
        {
            if (_disposed)
                return TaskPaneDocumentTransition.Activated;

            var transition = LightCaptureInstall();
            if (transition == TaskPaneDocumentTransition.Refreshed)
                RevalidateSessionAgainstDocument();
            RaiseChanged();
            return transition;
        }

        /// <summary>
        /// The light counterpart of <see cref="UpdateDocument"/>: STA
        /// capture → install with the same Activated semantics (drops the
        /// session, clears pending writes) but <em>no</em> same-document
        /// session revalidation — today's <c>RefreshCurrentProperties</c>
        /// path. Internal: consumed by <see cref="Bom.CoordinatorBomReadinessContext"/>
        /// and the ViewModel's light refresh paths; deliberately off
        /// <see cref="IPartSyncCoordinator"/>, whose document-lifecycle
        /// surface is the approved full evaluation only.
        /// </summary>
        internal TaskPaneDocumentTransition RefreshDocument()
        {
            if (_disposed)
                return TaskPaneDocumentTransition.Activated;

            var transition = LightCaptureInstall();
            RaiseChanged();
            return transition;
        }

        /// <inheritdoc/>
        public void NotifyDocumentClosed()
        {
            if (_disposed) return;

            _state.ClearDocument();
            _session = null;
            _state.ClearPopulated();
            _pendingConfirmation = null;
            _pendingDocumentWrites.Clear();
            RaiseChanged();
        }

        /// <inheritdoc/>
        public PartSyncPropertyChange NotifyDocumentPropertyChanged(string propertyName, string newValue)
        {
            if (_disposed) return PartSyncPropertyChange.Ignored;

            // An echo of our own pending write carries no new information —
            // the substitute-refresh already installed the announced value —
            // and a re-read could return a stale pre-write value.
            if (TryConsumePendingWrite(propertyName, newValue))
                return PartSyncPropertyChange.EchoConsumed;

            var mappingResult = ResolveMappingResult();
            if (_session == null || mappingResult.Health != MappingHealth.Healthy)
                return Reevaluate();

            var config = mappingResult.Config;

            // IPN and PK are identity properties: a divergence means the
            // document now refers to a different InvenTree part.
            if (PropertyNameEquals(config.IpnProperty, propertyName))
                return ClassifyIdentityChange(newValue, _session.Part.Ipn);

            if (PropertyNameEquals(config.PkProperty, propertyName))
                return ClassifyIdentityChange(newValue, _session.Part.Pk.ToString());

            // Mapped non-identity properties: refresh from the document.
            // A divergence from the session is a user edit — the caller clears
            // any stale success status.
            if (TryGetSessionValueFor(propertyName, config, out var expectedValue))
            {
                var diverged = !ValuesMatch(newValue, expectedValue);
                if (LightCaptureInstall() == TaskPaneDocumentTransition.Activated)
                    return PartSyncPropertyChange.Reevaluated;
                RaiseChanged();
                return diverged
                    ? PartSyncPropertyChange.RefreshedDivergent
                    : PartSyncPropertyChange.Refreshed;
            }

            return PartSyncPropertyChange.Ignored;
        }

        /// <summary>
        /// Full-eval path: light capture + install, then report Reevaluated —
        /// the caller runs the full evaluation (<see cref="UpdateDocument"/>)
        /// that revalidates and drops a divergent session.
        /// </summary>
        private PartSyncPropertyChange Reevaluate()
        {
            LightCaptureInstall();
            RaiseChanged();
            return PartSyncPropertyChange.Reevaluated;
        }

        private PartSyncPropertyChange ClassifyIdentityChange(string newValue, string? sessionValue)
        {
            if (!ValuesMatch(newValue, sessionValue))
                return Reevaluate();

            if (LightCaptureInstall() == TaskPaneDocumentTransition.Activated)
                return PartSyncPropertyChange.Reevaluated;
            RaiseChanged();
            return PartSyncPropertyChange.Refreshed;
        }

        /// <inheritdoc/>
        public void UpdateClient(IInventreeClient? client)
        {
            if (_disposed) return;

            _client = client;
            _lifecycleRevision++;
            _pendingConfirmation = null;
            DropSession();
            RaiseChanged();
        }

        /// <inheritdoc/>
        public void UpdateMapping(IPropertyMappingProvider? provider)
        {
            if (_disposed) return;

            _mappingProvider = provider;
            _lifecycleRevision++;
            _pendingConfirmation = null;

            // Light recapture under the new mapping — the preserving refresh
            // today's ViewModel ran: the session is never dropped by a mapping
            // change, but it must be rebound because PartSyncSession captures
            // the mapping at construction.
            LightCaptureInstall();
            if (_session != null && _client != null)
            {
                _session = new PartSyncSession(
                    CopyPart(_session.Part),
                    _client,
                    _propertyService,
                    ResolveMapping(),
                    _pendingDocumentWrites,
                    _session.ThumbnailBytes);
            }
            RaiseChanged();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _lifecycleRevision++;
            _session = null;
            _pendingConfirmation = null;
            _state.ClearPopulated();
        }

        // ── Fetch ────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<PartSyncResult> FetchAsync(string ipn)
        {
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");

            var mappingResult = ResolveMappingResult();
            if (mappingResult.CanFetch != true)
                return InvalidOp(mappingResult.FullStatusMessage);

            // Re-capture before choosing the path so a PK stamped mid-session
            // (Apply or a manual edit that only got a light refresh) is honored.
            LightCaptureInstall();

            // Session-family mint at capture: this fetch supersedes in-flight
            // fetches and create completions, and clears the pending
            // confirmation. The preview clears now — a Stale completion must
            // never resurrect it.
            var token = MintSessionFamilyToken();
            DropSession();
            RaiseChanged();

            if (_client == null)
                return InvalidOp("No server configured.");

            var document = _state.Document;
            if (document == null)
                return InvalidOp("No active document.");
            if (document.DocumentType == DocumentType.Drawing)
                return InvalidOp("Drawings are not supported.");

            var stampedPk = document.StampedPartPk;
            if (stampedPk > 0)
                return await FetchByPkAsync(token, stampedPk).ConfigureAwait(false);

            if (string.IsNullOrEmpty(ipn))
                return InvalidOp("No IPN to fetch.");

            return await FetchByIpnAsync(token, ipn).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task<PartSyncResult> EnsurePartPopulatedAsync()
        {
            if (_disposed)
                return Task.FromResult(InvalidOp("The coordinator is disposed."));
            if (_session != null)
                return Task.FromResult(new PartSyncResult(PartSyncOutcome.Success));

            // Addressed by document identity — never the textbox.
            return FetchAsync(_state.Document?.Ipn ?? string.Empty);
        }

        private async Task<PartSyncResult> FetchByPkAsync(PartSyncOperationToken token, int stampedPk)
        {
            InventreePart? part = null;
            byte[]? thumb = null;
            Exception? error = null;

            try
            {
                part = await _client!.GetPartByPkAsync(stampedPk).ConfigureAwait(false);
                if (part != null && !string.IsNullOrEmpty(part.ThumbnailUrl))
                {
                    try { thumb = await _client.DownloadImageAsync(part.ThumbnailUrl!).ConfigureAwait(false); }
                    catch { /* silent — placeholder will show */ }
                }
            }
            catch (Exception ex) { error = ex; }

            return await CommitOnStaAsync(
                () => CommitPkFetch(token, stampedPk, part, thumb, error)).ConfigureAwait(false);
        }

        private PartSyncResult CommitPkFetch(
            PartSyncOperationToken token, int stampedPk,
            InventreePart? part, byte[]? thumb, Exception? error)
        {
            if (!IsCommitCurrent(token))
                return Stale();

            if (error != null)
                return Failed(error.Message);
            if (part == null)
                return new PartSyncResult(PartSyncOutcome.PartNotFound) { PartPk = stampedPk };

            var doc = _state.Document!;
            var docIpn = doc.Ipn;
            var docRev = doc.Revision.Trim();

            // Link Mismatch: a stamped field counts only when both sides
            // carry values that disagree — blank on either side means
            // "can't verify" and stays silent.
            var ipnMismatch = !string.IsNullOrWhiteSpace(docIpn)
                && !string.IsNullOrWhiteSpace(part.Ipn)
                && !string.Equals(docIpn.Trim(), part.Ipn!.Trim(), StringComparison.OrdinalIgnoreCase);
            var revMismatch = !string.IsNullOrWhiteSpace(docRev)
                && !string.IsNullOrWhiteSpace(part.Revision)
                && RevisionComparer.Compare(docRev, part.Revision!.Trim()) != RevisionOrder.Equal;

            if (ipnMismatch || revMismatch)
            {
                var fetched = PartSnapshot.FromPart(part);
                var handle = StorePendingConfirmation(new PendingConfirmation
                {
                    Token = token,
                    Kind = PendingConfirmationKind.LinkMismatch,
                    Part = fetched,
                    ThumbnailBytes = thumb,
                });
                return new PartSyncResult(PartSyncOutcome.LinkMismatchConfirmation)
                {
                    Confirmation = handle,
                    DocumentIpn = docIpn,
                    DocumentRevision = docRev,
                    FetchedPart = fetched,
                    PartPk = stampedPk,
                };
            }

            var stampedIpn = WriteBackIpnIfNeeded(part, docIpn);
            // The substitute refresh may have found the document gone.
            if (_state.Document == null)
                return Stale();
            InstallSession(part, thumb);
            RaiseChanged();
            return new PartSyncResult(PartSyncOutcome.Success)
            {
                FetchedPart = PartSnapshot.FromPart(part),
                PartPk = stampedPk,
                Ipn = stampedIpn,
            };
        }

        private async Task<PartSyncResult> FetchByIpnAsync(PartSyncOperationToken token, string ipn)
        {
            IReadOnlyList<InventreePart>? parts = null;
            byte[]? thumb = null;
            Exception? error = null;

            try { parts = await _client!.GetPartsByIpnAsync(ipn).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }

            // Only pre-fetch the thumbnail when there is exactly one
            // unambiguous result.
            if (parts?.Count == 1 && !string.IsNullOrEmpty(parts[0].ThumbnailUrl))
            {
                try { thumb = await _client.DownloadImageAsync(parts[0].ThumbnailUrl!).ConfigureAwait(false); }
                catch { /* silent — placeholder will show */ }
            }

            return await CommitOnStaAsync(
                () => CommitIpnFetch(token, ipn, parts, thumb, error)).ConfigureAwait(false);
        }

        private PartSyncResult CommitIpnFetch(
            PartSyncOperationToken token, string ipn,
            IReadOnlyList<InventreePart>? parts, byte[]? thumb, Exception? error)
        {
            if (!IsCommitCurrent(token))
                return Stale();

            if (error != null)
                return Failed(error.Message);
            if (parts == null || parts.Count == 0)
                return new PartSyncResult(PartSyncOutcome.PartNotFound) { Ipn = ipn };

            if (parts.Count == 1)
            {
                InstallSession(parts[0], thumb);
                RaiseChanged();
                return new PartSyncResult(PartSyncOutcome.Success)
                {
                    FetchedPart = PartSnapshot.FromPart(parts[0]),
                    PartPk = parts[0].Pk,
                    Ipn = ipn,
                };
            }

            // Multiple parts share this IPN — resolve by the document revision.
            var swRev = _state.Document?.Revision?.Trim() ?? string.Empty;
            var matches = parts
                .Where(p => RevisionComparer.Compare(swRev, p.Revision?.Trim() ?? string.Empty)
                            == RevisionOrder.Equal)
                .ToList();
            // Frozen set: the same ReadOnlyCollection backs the pending
            // confirmation and the public result — a consumer cannot cast it
            // back to List and inject a fabricated candidate.
            var candidates = new ReadOnlyCollection<PartSnapshot>(
                parts.Select(PartSnapshot.FromPart).ToList());

            if (matches.Count == 0)
                return new PartSyncResult(PartSyncOutcome.DuplicateNoRevisionMatch)
                {
                    Ipn = ipn,
                    SwRevision = swRev,
                    Candidates = candidates,
                };

            if (matches.Count > 1)
                return new PartSyncResult(PartSyncOutcome.DuplicateAmbiguous)
                {
                    Ipn = ipn,
                    SwRevision = swRev,
                    Candidates = candidates,
                };

            var matched = matches[0];
            var matchedSnapshot = PartSnapshot.FromPart(matched);
            var handle = StorePendingConfirmation(new PendingConfirmation
            {
                Token = token,
                Kind = PendingConfirmationKind.DuplicateIpn,
                Part = matchedSnapshot,
                Candidates = candidates,
                MatchedCandidatePk = matched.Pk,
            });
            return new PartSyncResult(PartSyncOutcome.DuplicateIpnConfirmation)
            {
                Confirmation = handle,
                Candidates = candidates,
                MatchedCandidate = matchedSnapshot,
                Ipn = ipn,
                SwRevision = swRev,
            };
        }

        // ── Create Part completion ───────────────────────────────────────────

        /// <inheritdoc/>
        public PartSyncOperationToken BeginCreatePart() =>
            MintSessionFamilyToken();

        /// <inheritdoc/>
        public PartSyncResult CompleteCreatePart(PartSyncOperationToken token, InventreePart part)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");
            // Validate + recapture BEFORE any write — the completion may
            // outrun an undelivered document switch, and production writes
            // always target ISldWorks.ActiveDoc.
            if (!IsCommitCurrent(token))
                return Stale();

            var mapping = ResolveMapping();
            var doc = _state.Document;
            if (doc == null)
                return Stale();

            // A successful create always links the document by PK; IPN and Name
            // stamp alongside. Each write registers its pending echo first —
            // SolidWorks can raise it synchronously during the write.
            var wrotePk = part.Pk > 0 && !string.IsNullOrEmpty(mapping.PkProperty);
            if (wrotePk)
            {
                RegisterPendingWrite(mapping.PkProperty, part.Pk.ToString());
                _propertyService.SetCustomProperty(mapping.PkProperty!, part.Pk.ToString());
            }

            // Only write IPN when one was assigned — avoid blanking the
            // property when a server plugin has not generated it yet.
            var wroteIpn = !string.IsNullOrEmpty(part.Ipn) && !string.IsNullOrEmpty(mapping.IpnProperty);
            if (wroteIpn)
            {
                RegisterPendingWrite(mapping.IpnProperty, part.Ipn);
                _propertyService.SetCustomProperty(mapping.IpnProperty!, part.Ipn);
            }

            var wroteName = !string.IsNullOrEmpty(mapping.NameProperty);
            if (wroteName)
            {
                RegisterPendingWrite(mapping.NameProperty, part.Name);
                _propertyService.SetCustomProperty(mapping.NameProperty!, part.Name);
            }

            SubstituteRefresh(
                ipn: wroteIpn ? part.Ipn : null,
                pkText: wrotePk ? part.Pk.ToString() : null,
                name: wroteName ? part.Name : null);

            // The substitute refresh may have found the document gone.
            if (_state.Document == null)
                return Stale();
            InstallSession(part, thumbnail: null);
            RaiseChanged();
            return new PartSyncResult(PartSyncOutcome.Success)
            {
                FetchedPart = PartSnapshot.FromPart(part),
                PartPk = part.Pk,
                Ipn = wroteIpn ? part.Ipn : null,
            };
        }

        // ── Apply ────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public PartSyncResult Apply(ApplyField field)
        {
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");
            if (_session == null)
                return InvalidOp("No Part Sync session.");

            var mappingResult = ResolveMappingResult();
            if (mappingResult.CanUseForPartSync != true)
                return InvalidOp(mappingResult.FullStatusMessage);

            // Same commit discipline as the async paths: the session was
            // validated against the last *delivered* document state — an
            // ActiveDoc switch whose host notification has not run yet would
            // direct the old part's value into the new document. Validate +
            // recapture BEFORE any property-existence check or write, and
            // reuse the validated capture for a stored missing-property
            // confirmation so its resume correlates to this exact point.
            var session = _session;
            var token = CaptureScopedToken();
            if (!IsCommitCurrent(token) || !ReferenceEquals(_session, session))
                return Stale();

            var propertyName = session.ApplyPropertyName(field);
            var missing = session.GetMissingApplyProperties(propertyName);
            if (missing.Count > 0)
            {
                var handle = StorePendingConfirmation(new PendingConfirmation
                {
                    Token = token,
                    Kind = PendingConfirmationKind.MissingProperty,
                    ApplyField = field,
                });
                return new PartSyncResult(PartSyncOutcome.MissingPropertyConfirmation)
                {
                    Confirmation = handle,
                    MissingProperties = new ReadOnlyCollection<string>(missing.ToList()),
                };
            }

            var written = session.Apply(field);
            SubstituteRefreshFor(field, written);
            RaiseChanged();
            return new PartSyncResult(PartSyncOutcome.Success);
        }

        // ── Push ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<PartSyncResult> PushAsync(PushField field)
        {
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");
            if (_session == null || _client == null)
                return InvalidOp("No Part Sync session or client.");

            var mappingResult = ResolveMappingResult();
            if (mappingResult.CanUseForPartSync != true)
                return InvalidOp(mappingResult.FullStatusMessage);

            if (field == PushField.Revision && _session.Part.Pk == 0)
                return InvalidOp("cannot push revision \u2014 InvenTree part ID is missing.");

            var session = _session;
            var token = CaptureScopedToken();
            var value = session.CapturePushValue(field);
            if (value == null)
                return InvalidOp("The mapped property is not configured.");

            Exception? error = null;
            try { await session.PushValueAsync(field, value).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }

            return await CommitOnStaAsync(() =>
            {
                // Validate BEFORE examining the network outcome — a stale
                // failure must never reach the pane as a status write.
                if (!IsCommitCurrent(token) || !ReferenceEquals(_session, session))
                    return Stale();
                if (error != null)
                    return Failed(error.Message);
                session.CommitPushedValue(field, value);
                RaiseChanged();
                return new PartSyncResult(PartSyncOutcome.Success);
            }).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<PartSyncResult> PushImageAsync(Image image, Rectangle cropRect)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");
            if (_session == null || _client == null)
                return InvalidOp("No Part Sync session or client.");

            var mappingResult = ResolveMappingResult();
            if (mappingResult.CanUseForPartSync != true)
                return InvalidOp(mappingResult.FullStatusMessage);

            var session = _session;
            var token = CaptureScopedToken();
            var partPk = session.PartPk;

            byte[] pngData;
            try { pngData = ImagePipeline.Process(image, cropRect); }
            catch (Exception ex) { return Failed(ex.Message); }

            Exception? error = null;
            try { await _client.UploadPartImageAsync(partPk, pngData).ConfigureAwait(false); }
            catch (Exception ex) { error = ex; }

            string? warning = null;
            byte[]? newThumb = null;
            if (error == null)
            {
                try
                {
                    var refreshed = await _client.GetPartByPkAsync(partPk).ConfigureAwait(false);
                    if (refreshed == null)
                        warning = "Image pushed, but the part could not be re-fetched for a preview.";
                    else if (string.IsNullOrEmpty(refreshed.ThumbnailUrl))
                        warning = "Image pushed, but InvenTree did not return a thumbnail URL.";
                    else
                    {
                        newThumb = await _client.DownloadImageAsync(refreshed.ThumbnailUrl!).ConfigureAwait(false);
                        if (newThumb == null)
                            warning = "Image pushed, but the thumbnail could not be downloaded.";
                    }
                }
                catch { warning = "Image pushed, but the thumbnail preview could not be refreshed."; }
            }

            return await CommitOnStaAsync(() =>
            {
                // Validate BEFORE examining the upload/preview outcome.
                if (!IsCommitCurrent(token) || !ReferenceEquals(_session, session))
                    return Stale();
                if (error != null)
                    return Failed(error.Message);
                if (warning != null)
                    return new PartSyncResult(PartSyncOutcome.SucceededWithWarning)
                    {
                        Diagnostic = warning,
                    };
                session.SetThumbnail(newThumb!);
                RaiseChanged();
                return new PartSyncResult(PartSyncOutcome.Success);
            }).ConfigureAwait(false);
        }

        // ── Confirmation resume ──────────────────────────────────────────────

        /// <inheritdoc/>
        public async Task<PartSyncResult> ResumeConfirmationAsync(
            PartSyncConfirmationHandle handle, bool approved, int? selectedCandidatePk = null)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (_disposed)
                return InvalidOp("The coordinator is disposed.");

            var pending = _pendingConfirmation;
            if (pending == null || pending.Handle!.Id != handle.Id)
                return InvalidOp("No pending confirmation matches this handle.");

            if (!approved)
            {
                _pendingConfirmation = null;
                return new PartSyncResult(PartSyncOutcome.Cancelled);
            }

            // Validate on resume, BEFORE dispatching any download or commit
            // work — the in-commit validation then re-checks after the
            // download window. A stale pending can never succeed; drop it.
            if (!IsTokenCurrent(pending.Token))
            {
                _pendingConfirmation = null;
                return Stale();
            }

            switch (pending.Kind)
            {
                case PendingConfirmationKind.DuplicateIpn:
                    return await ResumeDuplicateIpnAsync(pending, selectedCandidatePk)
                        .ConfigureAwait(false);
                case PendingConfirmationKind.LinkMismatch:
                    return await CommitOnStaAsync(() => CommitLinkMismatchResume(pending))
                        .ConfigureAwait(false);
                case PendingConfirmationKind.MissingProperty:
                    return await CommitOnStaAsync(() => CommitMissingPropertyResume(pending))
                        .ConfigureAwait(false);
                default:
                    _pendingConfirmation = null;
                    return InvalidOp("Unknown pending confirmation.");
            }
        }

        private async Task<PartSyncResult> ResumeDuplicateIpnAsync(
            PendingConfirmation pending, int? selectedCandidatePk)
        {
            // Candidate selection by immutable key, validated against the
            // captured set — an approval can never land on a part the user
            // was not shown.
            var pk = selectedCandidatePk ?? pending.MatchedCandidatePk;
            var chosen = pending.Candidates?.FirstOrDefault(c => c.Pk == pk);
            if (chosen == null)
            {
                _pendingConfirmation = null;
                return InvalidOp("The selected candidate is not part of the captured set.");
            }

            // Validate on resume — marshalled, WITH document recapture —
            // before any download: an undelivered switch discovered here
            // stales the resume before a single byte is fetched.
            var stillCurrent = await RunOnStaAsync(() =>
            {
                if (IsCommitCurrent(pending.Token))
                    return true;
                _pendingConfirmation = null;
                return false;
            }).ConfigureAwait(false);
            if (!stillCurrent)
                return Stale();

            byte[]? thumb = null;
            if (!string.IsNullOrEmpty(chosen.ThumbnailUrl))
            {
                try { thumb = await _client!.DownloadImageAsync(chosen.ThumbnailUrl!).ConfigureAwait(false); }
                catch { /* silent — placeholder will show */ }
            }

            return await CommitOnStaAsync(() =>
            {
                _pendingConfirmation = null;
                // Second validation inside the commit — the download gave a
                // switch/close/replacement time to land.
                if (!IsCommitCurrent(pending.Token))
                    return Stale();
                InstallSession(chosen.ToPart(), thumb);
                RaiseChanged();
                return new PartSyncResult(PartSyncOutcome.Success)
                {
                    FetchedPart = chosen,
                    PartPk = chosen.Pk,
                    Ipn = chosen.Ipn,
                };
            }).ConfigureAwait(false);
        }

        private PartSyncResult CommitLinkMismatchResume(PendingConfirmation pending)
        {
            _pendingConfirmation = null;
            if (!IsCommitCurrent(pending.Token))
                return Stale();

            var part = pending.Part!.ToPart();
            var stampedIpn = WriteBackIpnIfNeeded(part, _state.Document!.Ipn);
            if (_state.Document == null)
                return Stale();
            InstallSession(part, pending.ThumbnailBytes);
            RaiseChanged();
            return new PartSyncResult(PartSyncOutcome.Success)
            {
                FetchedPart = pending.Part,
                PartPk = part.Pk,
                Ipn = stampedIpn,
            };
        }

        private PartSyncResult CommitMissingPropertyResume(PendingConfirmation pending)
        {
            _pendingConfirmation = null;
            // Validate + recapture BEFORE _session.Apply writes — same
            // discipline as the fetch/duplicate resumes.
            if (!IsCommitCurrent(pending.Token))
                return Stale();
            if (_session == null)
                return InvalidOp("No Part Sync session.");

            var written = _session.Apply(pending.ApplyField);
            SubstituteRefreshFor(pending.ApplyField, written);
            RaiseChanged();
            return new PartSyncResult(PartSyncOutcome.Success);
        }

        // ── Session install / drop ───────────────────────────────────────────

        private void InstallSession(InventreePart part, byte[]? thumbnail)
        {
            _session = new PartSyncSession(
                CopyPart(part),
                _client!,
                _propertyService,
                ResolveMapping(),
                _pendingDocumentWrites,
                thumbnail);
            _state.MarkPopulated();
        }

        private void DropSession()
        {
            _session = null;
            _state.ClearPopulated();
            _pendingConfirmation = null;
        }

        /// <summary>
        /// Same-document revalidation, ported verbatim from the ViewModel —
        /// an Activated transition drops the session before this can run.
        /// Drops the session unless it still describes the active document's
        /// identity stamps: a positive stamped PK must match the session part
        /// (PK 0 never matches), and a stamped IPN must match when present.
        /// Evaluates the underlying document kind — <see cref="TaskPaneState.Kind"/>
        /// reports Populated once the signal is bound, so the ported
        /// "Linked" check is re-expressed from document facts.
        /// </summary>
        private void RevalidateSessionAgainstDocument()
        {
            if (_session == null) return;
            var doc = _state.Document;

            var pkMatches = doc != null
                && doc.StampedPartPk > 0
                && _session.Part.Pk == doc.StampedPartPk;

            var underlyingLinked = doc != null
                && doc.DocumentType != DocumentType.Drawing
                && (!string.IsNullOrEmpty(doc.Ipn) || doc.StampedPartPk > 0);

            var keep = underlyingLinked && pkMatches
                && (string.IsNullOrEmpty(doc!.Ipn)
                    || string.Equals(_session.Part.Ipn, doc.Ipn, StringComparison.Ordinal));

            if (!keep)
                DropSession();
        }

        // ── Document capture / install ───────────────────────────────────────

        /// <summary>
        /// The light capture+install: no session revalidation — the
        /// same-document-refresh semantics pinned by the light-path
        /// characterization tests. An Activated transition still drops the
        /// session and clears pending writes unconditionally.
        /// </summary>
        private TaskPaneDocumentTransition LightCaptureInstall()
        {
            var token = _propertyService.GetActiveDocumentToken();
            var snapshot = CaptureDocumentSnapshot();

            if (token == null || snapshot.DocumentType == DocumentType.Unknown)
            {
                // ClearDocument exactly once — a second clear would
                // double-advance the generation and poison in-flight tokens.
                if (_state.Kind != TaskPaneStateKind.Empty)
                {
                    _state.ClearDocument();
                    _session = null;
                    _pendingConfirmation = null;
                }
                _state.ClearPopulated();
                _pendingDocumentWrites.Clear();
                return TaskPaneDocumentTransition.Activated;
            }

            var transition = _state.ApplyDocumentUpdate(token, snapshot);
            if (transition == TaskPaneDocumentTransition.Activated)
            {
                // A new generation: pending echoes were keyed to the previous
                // document — notifications carry no identity, so a stale
                // entry could swallow a real edit on the new document.
                _pendingDocumentWrites.Clear();

                // A document switch drops the session unconditionally (#292):
                // identical IPN + stamped PK on the new document is a copied
                // file with stale stamps, not a state to adopt.
                DropSession();
            }
            return transition;
        }

        /// <summary>
        /// The single stale-commit guard every completion path runs BEFORE
        /// examining the network result or performing any write: validates
        /// the captured token against current coordinator state, then
        /// recaptures the active document — catching a switch/close whose
        /// host notification has not been delivered yet — and validates the
        /// token again. A stale outcome must never become a status write or
        /// a SolidWorks property write.
        /// </summary>
        private bool IsCommitCurrent(PartSyncOperationToken token) =>
            IsTokenCurrent(token) && RecaptureAndRevalidate(token);

        /// <summary>
        /// Re-reads document values inside a validated commit and confirms the
        /// token is still current — catches a document switch whose host
        /// notification has not been delivered yet.
        /// </summary>
        private bool RecaptureAndRevalidate(PartSyncOperationToken token)
        {
            LightCaptureInstall();
            return IsTokenCurrent(token);
        }

        private TaskPaneDocumentSnapshot CaptureDocumentSnapshot()
        {
            var mapping = ResolveMapping();
            return new TaskPaneDocumentSnapshot(
                _propertyService.GetDocumentType(),
                GetCustomPropertyOrEmpty(mapping.IpnProperty),
                GetCustomPropertyOrEmpty(mapping.PkProperty),
                GetCustomPropertyOrEmpty(mapping.NameProperty),
                GetCustomPropertyOrEmpty(mapping.NotesProperty),
                GetCustomPropertyOrEmpty(mapping.RevisionProperty),
                GetCustomPropertyOrEmpty(mapping.DescriptionProperty));
        }

        /// <summary>
        /// Refresh transition after an add-in write: the just-written value is
        /// substituted over a possibly-stale re-read — SolidWorks caches
        /// custom property reads on assemblies — while other fields carry
        /// over from the current snapshot.
        /// </summary>
        private void SubstituteRefresh(
            string? ipn = null, string? pkText = null, string? name = null,
            string? notes = null, string? revision = null, string? description = null)
        {
            var doc = _state.Document;
            if (doc == null) return;

            var token = _propertyService.GetActiveDocumentToken();
            if (token == null)
            {
                if (_state.Kind != TaskPaneStateKind.Empty)
                {
                    _state.ClearDocument();
                    _session = null;
                    _pendingConfirmation = null;
                }
                _state.ClearPopulated();
                _pendingDocumentWrites.Clear();
                return;
            }

            var transition = _state.ApplyDocumentUpdate(
                token,
                new TaskPaneDocumentSnapshot(
                    doc.DocumentType,
                    ipn ?? doc.Ipn,
                    pkText ?? doc.PkText,
                    name ?? doc.Name,
                    notes ?? doc.Notes,
                    revision ?? doc.Revision,
                    description ?? doc.Description));
            if (transition == TaskPaneDocumentTransition.Activated)
            {
                _pendingDocumentWrites.Clear();
                DropSession();
            }
        }

        private void SubstituteRefreshFor(ApplyField field, string value)
        {
            switch (field)
            {
                case ApplyField.Name: SubstituteRefresh(name: value); break;
                case ApplyField.Notes: SubstituteRefresh(notes: value); break;
                case ApplyField.Description: SubstituteRefresh(description: value); break;
                case ApplyField.Pk: SubstituteRefresh(pkText: value); break;
            }
        }

        // ── Operation tokens ─────────────────────────────────────────────────

        private bool IsTokenCurrent(PartSyncOperationToken token) =>
            !_disposed
            && token.DocumentGeneration == _state.Generation
            && token.LifecycleRevision == _lifecycleRevision
            && token.FamilyOrder == _familyOrder;

        private PartSyncOperationToken CaptureScopedToken() =>
            new PartSyncOperationToken(_state.Generation, _lifecycleRevision, _familyOrder);

        /// <summary>
        /// Mints the next session-family token: bumps the family order so an
        /// older in-flight operation can never overwrite this one, and clears
        /// the pending confirmation — it belonged to a superseded family.
        /// </summary>
        private PartSyncOperationToken MintSessionFamilyToken()
        {
            _familyOrder++;
            _pendingConfirmation = null;
            return CaptureScopedToken();
        }

        private Task<PartSyncResult> CommitOnStaAsync(Func<PartSyncResult> commit) =>
            RunOnStaAsync(commit);

        /// <summary>
        /// Marshals <paramref name="work"/> onto the host STA thread through
        /// the injected dispatcher — the single boundary every completion
        /// crosses before touching document state.
        /// </summary>
        private Task<TResult> RunOnStaAsync<TResult>(Func<TResult> work)
        {
            var tcs = new TaskCompletionSource<TResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.Run(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        // ── Pending writes ───────────────────────────────────────────────────

        private void RegisterPendingWrite(string? propertyName, string? value)
        {
            if (string.IsNullOrEmpty(propertyName) || value == null) return;
            _pendingDocumentWrites[propertyName!] = value;
        }

        private bool TryConsumePendingWrite(string propertyName, string newValue)
        {
            if (!_pendingDocumentWrites.TryGetValue(propertyName, out var expected))
                return false;
            if (!ValuesMatch(expected, newValue))
                return false;
            _pendingDocumentWrites.Remove(propertyName);
            return true;
        }

        // ── Pending confirmations ────────────────────────────────────────────

        private PartSyncConfirmationHandle StorePendingConfirmation(PendingConfirmation pending)
        {
            var handle = new PartSyncConfirmationHandle(++_nextConfirmationId);
            pending.Handle = handle;
            _pendingConfirmation = pending;
            return handle;
        }

        private enum PendingConfirmationKind { DuplicateIpn, LinkMismatch, MissingProperty }

        private sealed class PendingConfirmation
        {
            public PartSyncConfirmationHandle? Handle { get; set; }
            public PartSyncOperationToken Token { get; set; } = null!;
            public PendingConfirmationKind Kind { get; set; }
            public PartSnapshot? Part { get; set; }
            public IReadOnlyList<PartSnapshot>? Candidates { get; set; }
            public int MatchedCandidatePk { get; set; }
            public byte[]? ThumbnailBytes { get; set; }
            public ApplyField ApplyField { get; set; }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the fetched part's IPN back to the document when the
        /// document IPN is blank — links the document by IPN going forward
        /// without an explicit Apply. Returns the stamped value, or null.
        /// </summary>
        private string? WriteBackIpnIfNeeded(InventreePart part, string docIpn)
        {
            var mapping = ResolveMapping();
            if (string.IsNullOrWhiteSpace(part.Ipn)
                || !string.IsNullOrWhiteSpace(docIpn)
                || string.IsNullOrWhiteSpace(mapping.IpnProperty))
                return null;

            RegisterPendingWrite(mapping.IpnProperty, part.Ipn);
            _propertyService.SetCustomProperty(mapping.IpnProperty!, part.Ipn);
            SubstituteRefresh(ipn: part.Ipn);
            return part.Ipn;
        }

        private bool TryGetSessionValueFor(
            string propertyName, PropertyMappingConfig config, out string? value)
        {
            if (PropertyNameEquals(config.NameProperty, propertyName)) { value = _session!.Part.Name; return true; }
            if (PropertyNameEquals(config.NotesProperty, propertyName)) { value = _session!.Part.Notes; return true; }
            if (PropertyNameEquals(config.RevisionProperty, propertyName)) { value = _session!.Part.Revision; return true; }
            if (PropertyNameEquals(config.DescriptionProperty, propertyName)) { value = _session!.Part.Description; return true; }

            value = null;
            return false;
        }

        private static bool PropertyNameEquals(string? left, string? right)
            => !string.IsNullOrEmpty(left)
               && !string.IsNullOrEmpty(right)
               && string.Equals(left, right, StringComparison.Ordinal);

        private static bool ValuesMatch(string? left, string? right)
            => string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

        private string GetCustomPropertyOrEmpty(string? propertyName) =>
            string.IsNullOrEmpty(propertyName)
                ? string.Empty
                : _propertyService.GetCustomProperty(propertyName!);

        /// <summary>
        /// Mapped Document Property names absent from the active document —
        /// the missing-property check behind Apply's confirmation flow,
        /// exposed for tests.
        /// </summary>
        internal List<string> FindMissingProperties(IEnumerable<string?> names)
        {
            var missing = new List<string>();
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && !_propertyService.PropertyExists(n!))
                    missing.Add(n!);
            return missing;
        }

        private MappingResult ResolveMappingResult() =>
            _mappingProvider?.GetMappingResult()
            ?? new MappingResult(MappingHealth.Healthy, PropertyMappingConfig.WithDefaults());

        private PropertyMappingConfig ResolveMapping() =>
            _mappingProvider?.GetMappingResult().Config ?? PropertyMappingConfig.WithDefaults();

        /// <summary>
        /// Copy-on-install: an <see cref="InventreePart"/> handed in by the
        /// client or the Create Part dialog is caller-owned — the session
        /// keeps a private copy so later caller mutation cannot reach it.
        /// </summary>
        internal static InventreePart CopyPart(InventreePart part) =>
            new InventreePart
            {
                Pk = part.Pk,
                Ipn = part.Ipn,
                Name = part.Name,
                Notes = part.Notes,
                Revision = part.Revision,
                Description = part.Description,
                ThumbnailUrl = part.ThumbnailUrl,
                InStock = part.InStock,
                Ordering = part.Ordering,
                Active = part.Active,
                Assembly = part.Assembly,
                Component = part.Component,
                Purchaseable = part.Purchaseable,
                Salable = part.Salable,
                Trackable = part.Trackable,
                Testable = part.Testable,
            };

        private static PartSyncResult InvalidOp(string? diagnostic = null) =>
            new PartSyncResult(PartSyncOutcome.InvalidOperation) { Diagnostic = diagnostic };

        private static PartSyncResult Stale() =>
            new PartSyncResult(PartSyncOutcome.Stale);

        private static PartSyncResult Failed(string? diagnostic) =>
            new PartSyncResult(PartSyncOutcome.Failed) { Diagnostic = diagnostic };
    }
}
