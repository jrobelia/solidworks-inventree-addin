using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Logging;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    public class BomDiffLineViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        { if (Equals(f, v)) return; f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        public BomDiffLine DiffLine { get; }
        public BomDiffState State => DiffLine.State;
        // Computed from the current State so it stays correct after PushAsync mutates
        // DiffLine.State to Match. Select All / Select New / Select Conflicts and the
        // PushEnabled predicate all filter on this, so a pushed row drops out of every
        // selection path as soon as its state changes.
        public bool CanCheck => State == BomDiffState.New || State == BomDiffState.Conflict;

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (CanCheck) Set(ref _isChecked, value); }
        }

        public string StateLabel => State switch
        {
            BomDiffState.Match => "Match",
            BomDiffState.Conflict => "Conflict",
            BomDiffState.New => "New",
            BomDiffState.InvenTreeOnly => "Inv Only",
            BomDiffState.NoIpn => "No IPN",
            BomDiffState.IpnNotFound => "Not Found",
            BomDiffState.Ambiguous => "Ambiguous",
            _ => string.Empty,
        };

        public string DisplayIpn => DiffLine.DisplayIpn;
        // An unparseable quantity cell shows its raw text — a silent 0 would lie.
        public string SwQty => DiffLine.SwLine != null
            ? (DiffLine.SwLine.UnparsedQuantity ?? DiffLine.SwLine.Quantity.ToString("G29"))
            : string.Empty;
        public string SwReference => DiffLine.SwLine?.Reference ?? string.Empty;
        public string SwNote => DiffLine.SwLine?.Note ?? string.Empty;
        public string SwRevision => DiffLine.SwLine?.Revision ?? string.Empty;
        public bool SwRevisionUnreadable => DiffLine.SwLine?.RevisionUnreadable ?? false;
        public string ItRevision => DiffLine.ItLine?.SubPartRevision ?? string.Empty;
        public bool RevisionDifference =>
            DiffLine.SwLine != null
            && DiffLine.ItLine != null
            && !(SwRevision.Length == 0 && ItRevision.Length == 0)
            && !string.Equals(SwRevision.Trim(), ItRevision.Trim(), StringComparison.OrdinalIgnoreCase);
        public string ItQty => DiffLine.ItLine != null ? DiffLine.ItLine.Quantity.ToString("G29") : string.Empty;
        public string ItReference => DiffLine.ItLine?.Reference ?? string.Empty;
        public string ItNote => DiffLine.ItLine?.Note ?? string.Empty;
        public bool ItConsumable => DiffLine.ItLine?.Consumable ?? false;
        public bool ItOptional => DiffLine.ItLine?.Optional ?? false;
        public bool ItValidated => DiffLine.ItLine?.Validated ?? false;
        public bool HasSubstitutes => DiffLine.ItLine?.HasSubstitutes ?? false;
        public string HasSubstitutesLabel => HasSubstitutes ? "Yes" : "\u2014";
        // Per-cell conflict flags — true only on Conflict rows where that specific field differs.
        public bool QtyConflict => State == BomDiffState.Conflict && SwQty != ItQty;
        public bool ReferenceConflict => State == BomDiffState.Conflict && SwReference != ItReference;
        public bool NoteConflict => State == BomDiffState.Conflict && SwNote != ItNote;
        // GuidanceText's non-empty arms are the single source for the
        // problem-state set — the XAML, the attention count, and the sort
        // order all read it through here.
        public bool IsProblemState => GuidanceText.Length > 0;

        // The .pen problem rows replace their data cells with a one-line
        // explanation. Ambiguous has two causes the mock draws as one row —
        // an IPN that resolves to several InvenTree parts, and a quantity
        // cell that failed to parse — so the message picks by SwLine.
        public string GuidanceText => State switch
        {
            BomDiffState.NoIpn =>
                "Part has no IPN assigned in SolidWorks — edit custom property",
            BomDiffState.IpnNotFound =>
                "IPN not found in InvenTree — create the part first",
            BomDiffState.Ambiguous => DiffLine.SwLine?.UnparsedQuantity != null
                ? "Quantity could not be parsed — fix the BOM quantity value"
                : "IPN matches multiple InvenTree parts — set InvenTree Part PK or a unique Revision",
            _ => string.Empty,
        };

        // True when the row carries an IPN worth printing. The grid uses it to
        // place the guidance line: it starts in the IPN cell on a No-IPN row
        // and in the first data cell when the IPN stays visible.

        public BomDiffLineViewModel(BomDiffLine diffLine)
        {
            DiffLine = diffLine;
        }

        /// <summary>Called after DiffLine.State is mutated externally to refresh bound properties.</summary>
        public void NotifyStateChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public class BomCompareViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        { if (Equals(f, v)) return; f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        // ── Bindable collections ─────────────────────────────────────────────

        private readonly BatchObservableCollection<BomDiffLineViewModel> _lines = new BatchObservableCollection<BomDiffLineViewModel>();
        public ObservableCollection<BomDiffLineViewModel> Lines => _lines;

        // ── Dependencies ─────────────────────────────────────────────────────

        private readonly IInventreeClient _client;
        private readonly IAssemblyBomService _bomService;
        private readonly PropertyMappingConfig _mapping;
        private readonly int _assemblyPk;
        private readonly string _bomKeyword;
        private readonly IAddinLogger _logger;

        /// <summary>
        /// UI-thread synchronisation context captured at construction. Null when
        /// constructed on a thread-pool thread (unit tests) — in that case
        /// RunOnUiThread executes actions inline.
        /// </summary>
        private readonly SynchronizationContext? _uiContext;

        // Captured alongside _uiContext so "already on the UI thread" is a
        // thread check, not a context check — inside a Dispatcher.Invoke
        // callback SynchronizationContext.Current is a fresh
        // DispatcherSynchronizationContext wrapper that never reference-equals
        // the captured context, and Send would marshal into a context whose
        // pump is not running.
        private readonly int _uiThreadId;

        // ── State ────────────────────────────────────────────────────────────

        private string _statusText = string.Empty;
        private bool _isPushing;
        private string _sortColumn = string.Empty;
        private bool _sortAscending = true;

        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
        public bool IsPushing
        {
            get => _isPushing;
            set
            {
                Set(ref _isPushing, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushEnabled)));
            }
        }
        public string SortColumn { get => _sortColumn; set => Set(ref _sortColumn, value); }
        public bool SortAscending { get => _sortAscending; set => Set(ref _sortAscending, value); }

        public bool PushEnabled =>
            !IsPushing && Lines.Any(l => l.CanCheck && l.IsChecked);

        /// <summary>
        /// Inject to override confirmation logic in tests.
        /// Args: (newCount, updateCount). Return false to cancel.
        /// Default denies — a host that never wires the dialog must not push.
        /// </summary>
        public Func<int, int, bool> ConfirmPush { get; set; } = (_, __) => false;

        // ── Constructors ─────────────────────────────────────────────────────

        public BomCompareViewModel(
            IInventreeClient client,
            IAssemblyBomService bomService,
            PropertyMappingConfig mapping,
            int assemblyPk,
            string bomKeyword = "inventree",
            IAddinLogger? logger = null)
        {
            _client = client;
            _bomService = bomService;
            _mapping = mapping;
            _assemblyPk = assemblyPk;
            _bomKeyword = bomKeyword;
            _logger = logger ?? new NullAddinLogger();
            _uiContext = SynchronizationContext.Current;
            _uiThreadId = Environment.CurrentManagedThreadId;
        }

        // ── Behaviour ────────────────────────────────────────────────────────

        public async Task LoadAsync()
        {
            _logger.LogInformation($"BOM Compare load started (assembly PK {_assemblyPk}).");
            try
            {
                StatusText = "Loading…";
                var swLines = _bomService.GetBomLines(_bomKeyword, _mapping);
                var (itLines, lookup) = await Task.Run(async () =>
                {
                    var itTask = _client.GetBomAsync(_assemblyPk);
                    var lupTask = BuildIpnLookupAsync(swLines);
                    await Task.WhenAll(itTask, lupTask).ConfigureAwait(false);
                    return (itTask.Result, lupTask.Result);
                }).ConfigureAwait(false);

                // Diff is pure logic; the resulting UI-bound update must happen on the UI thread.
                var loaded = 0;
                RunOnUiThread(() =>
                {
                    var diff = BomDiffEngine.Diff(swLines, itLines, lookup);
                    RebindLines(diff);
                    loaded = diff.Count;
                    StatusText = BuildStateSummary();
                    if (!string.IsNullOrEmpty(_sortColumn)) ApplySort();
                });
                _logger.LogInformation($"BOM Compare load completed: {loaded} lines.");
            }
            catch (Exception ex)
            {
                _logger.LogError("BOM Compare load failed.", ex);
                throw;
            }
        }

        public async Task PushAsync()
        {
            var toProcess = Lines
                .Where(l => l.IsChecked && l.CanCheck)
                .Select(l => l.DiffLine)
                .ToList();

            int newCount = toProcess.Count(l => l.State == BomDiffState.New);
            int updateCount = toProcess.Count(l => l.State == BomDiffState.Conflict);

            _logger.LogInformation(
                $"BOM Compare push started ({toProcess.Count} selected: {newCount} new, {updateCount} updates).");
            try
            {
                await PushCoreAsync(toProcess, newCount, updateCount).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError("BOM Compare push failed.", ex);
                throw;
            }
        }

        private async Task PushCoreAsync(
            IReadOnlyList<BomDiffLine> toProcess, int newCount, int updateCount)
        {
            // Clear any stale result and let the user know the button was pressed.
            StatusText = "Pushing selected lines to InvenTree…";

            if (!ConfirmPush(newCount, updateCount))
            {
                StatusText = string.Empty;
                _logger.LogInformation("BOM Compare push cancelled by user.");
                return;
            }

            // Verify the assembly part is flagged as Assembly in InvenTree before writing.
            var assemblyPart = await Task.Run(() => _client.GetPartByPkAsync(_assemblyPk)).ConfigureAwait(false);
            if (assemblyPart == null || !assemblyPart.Assembly)
            {
                _logger.LogError(
                    $"BOM Compare push aborted: InvenTree part {_assemblyPk} is not flagged as Assembly.");
                RunOnUiThread(() =>
                    StatusText = $"Cannot push — InvenTree part {_assemblyPk} is not flagged as Assembly. Edit it in InvenTree first.");
                return;
            }

            RunOnUiThread(() => IsPushing = true);
            int created = 0, updated = 0, failed = 0;
            var failedIpns = new List<string>();
            var succeededVms = new List<BomDiffLineViewModel>();

            // Build a lookup so we can update the ViewModel in-place on success.
            var vmByDiffLine = Lines.ToDictionary(vm => vm.DiffLine);

            foreach (var line in toProcess)
            {
                try
                {
                    if (line.State == BomDiffState.New)
                    {
                        int newPk = await _client.CreateBomLineAsync(
                            _assemblyPk, line.SubPartPk,
                            line.SwLine!.Quantity, line.SwLine.Reference, line.SwLine.Note,
                            false, false).ConfigureAwait(false);
                        // Store the server-assigned PK so a subsequent update can reference it.
                        line.NewBomLinePk = newPk;
                        created++;
                    }
                    else if (line.State == BomDiffState.Conflict)
                    {
                        await _client.UpdateBomLineAsync(
                            line.ItLine!.Pk,
                            line.SwLine!.Quantity, line.SwLine.Reference, line.SwLine.Note,
                            line.ItLine.Consumable, line.ItLine.Optional).ConfigureAwait(false);
                        updated++;
                    }
                    succeededVms.Add(vmByDiffLine[line]);
                }
                catch (Exception ex)
                {
                    failed++;
                    failedIpns.Add($"{line.DisplayIpn} ({ExceptionText.ForUser(ex)})");
                    _logger.LogError($"BOM Compare push failed for {line.DisplayIpn}.", ex);
                }
            }

            IReadOnlyList<InventreeBomLine>? refreshedLines = null;
            if (succeededVms.Count > 0)
            {
                try
                {
                    refreshedLines = await _client.GetBomAsync(_assemblyPk).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError("BOM Compare post-push refresh failed; falling back to pushed values.", ex);
                }
            }

            // Update pushed rows in-place, preferring the server BOM pulled after Push.
            RunOnUiThread(() =>
            {
                foreach (var vm in succeededVms)
                {
                    var sw = vm.DiffLine.SwLine!;
                    int expectedLinePk = vm.DiffLine.ItLine?.Pk ?? vm.DiffLine.NewBomLinePk;
                    var serverLine = refreshedLines?.FirstOrDefault(l => l.Pk == expectedLinePk);

                    if (serverLine != null)
                    {
                        vm.DiffLine.ItLine = serverLine;
                        if (!string.IsNullOrEmpty(serverLine.SubPartIpn))
                            vm.DiffLine.DisplayIpn = serverLine.SubPartIpn;
                    }
                    else if (vm.DiffLine.ItLine == null)
                    {
                        // New line: create a stub ItLine from the values we just pushed.
                        vm.DiffLine.ItLine = new InventreeBomLine
                        {
                            Pk = vm.DiffLine.NewBomLinePk,
                            SubPartPk = vm.DiffLine.SubPartPk,
                            SubPartIpn = vm.DiffLine.DisplayIpn,
                            Quantity = sw.Quantity,
                            Reference = sw.Reference,
                            Note = sw.Note,
                        };
                    }
                    else
                    {
                        // Conflict: update the IT fields to match what we pushed.
                        vm.DiffLine.ItLine.Quantity = sw.Quantity;
                        vm.DiffLine.ItLine.Reference = sw.Reference;
                        vm.DiffLine.ItLine.Note = sw.Note;
                    }

                    // Clear the checkbox before flipping State to Match: the IsChecked setter
                    // guards on CanCheck, which is derived from State. Once State == Match the
                    // guard would silently swallow the clear and leave the row visibly checked.
                    vm.IsChecked = false;
                    vm.DiffLine.State = BomDiffState.Match;
                    vm.NotifyStateChanged();
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushEnabled)));

                IsPushing = false;

                var parts = new List<string>();
                if (created > 0) parts.Add($"{created} created");
                if (updated > 0) parts.Add($"{updated} updated");
                if (failed > 0) parts.Add($"{failed} failed ({string.Join(", ", failedIpns)})");
                StatusText = parts.Count > 0 ? string.Join(", ", parts) : "No changes pushed";
            });
            _logger.LogInformation(
                $"BOM Compare push completed: {created} created, {updated} updated, {failed} failed.");
        }

        public void SortCommand(string columnName)
        {
            if (SortColumn == columnName)
                SortAscending = !SortAscending;
            else
            {
                SortColumn = columnName;
                SortAscending = true;
            }
            ApplySort();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<IDictionary<string, IReadOnlyList<InventreePart>>> BuildIpnLookupAsync(
            IReadOnlyList<SwBomLine> swLines)
        {
            var toFetch = swLines
                .Where(l => l.SubPartPk == 0 && !string.IsNullOrWhiteSpace(l.Ipn))
                .Select(l => l.Ipn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tasks = toFetch.Select(ipn => _client.GetPartsByIpnAsync(ipn).ContinueWith(
                t => (ipn, parts: t.Result), TaskContinuationOptions.ExecuteSynchronously));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var result = new Dictionary<string, IReadOnlyList<InventreePart>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (ipn, parts) in results)
                result[ipn] = parts;
            return result;
        }

        // The .pen footer's post-load line: counts per BOM Diff State, with the
        // problem rows folded into one "need attention" figure.
        private string BuildStateSummary()
        {
            var parts = new List<string>();
            int matched = Lines.Count(l => l.State == BomDiffState.Match);
            int conflicts = Lines.Count(l => l.State == BomDiffState.Conflict);
            int newLines = Lines.Count(l => l.State == BomDiffState.New);
            int itOnly = Lines.Count(l => l.State == BomDiffState.InvenTreeOnly);
            int attention = Lines.Count(l => l.IsProblemState);

            if (matched > 0) parts.Add($"{matched} matched");
            if (conflicts > 0) parts.Add($"{conflicts} {(conflicts == 1 ? "conflict" : "conflicts")}");
            if (newLines > 0) parts.Add($"{newLines} new");
            if (itOnly > 0) parts.Add($"{itOnly} InvenTree-only");
            if (attention > 0) parts.Add($"{attention} need attention");

            return string.Join(" · ", parts);
        }

        private void RebindLines(IReadOnlyList<BomDiffLine> diff)
        {
            _lines.Reset(diff.Select(line =>
            {
                var vm = new BomDiffLineViewModel(line);
                vm.PropertyChanged += OnLineCheckedChanged;
                return vm;
            }));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushEnabled)));
        }

        private void OnLineCheckedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BomDiffLineViewModel.IsChecked))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PushEnabled)));
        }

        private void ApplySort()
        {
            if (string.IsNullOrEmpty(SortColumn)) return;

            Func<BomDiffLineViewModel, string> key = SortColumn switch
            {
                "State" => l => l.StateLabel,
                "IPN" => l => l.DisplayIpn,
                "SwRevision" => l => l.SwRevision,
                "SwQty" => l => l.SwQty,
                "SwRef" => l => l.SwReference,
                "SwNote" => l => l.SwNote,
                "ItRevision" => l => l.ItRevision,
                "ItQty" => l => l.ItQty,
                "ItRef" => l => l.ItReference,
                "ItNote" => l => l.ItNote,
                "Cons" => l => l.ItConsumable.ToString(),
                "Opt" => l => l.ItOptional.ToString(),
                _ => l => l.DisplayIpn,
            };

            var normal = Lines.Where(l => !l.IsProblemState).ToList();
            var problems = Lines.Where(l => l.IsProblemState).ToList();

            var sorted = SortAscending
                ? normal.OrderBy(key, StringComparer.OrdinalIgnoreCase).ToList()
                : normal.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ToList();

            sorted.AddRange(problems);

            _lines.Reset(sorted);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread. Same-thread callers
        /// execute inline; off-thread callers marshal through Send (synchronous)
        /// so they see property updates immediately. Falls back to inline
        /// execution when no context was captured (unit tests).
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (Environment.CurrentManagedThreadId == _uiThreadId || _uiContext == null)
                action();
            else
                _uiContext.Send(_ => action(), null);
        }
    }
}
