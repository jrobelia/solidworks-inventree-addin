using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    public class BomDiffLineViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        { if (Equals(f, v)) return; f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

        public BomDiffLine DiffLine { get; }
        public BomDiffState State   => DiffLine.State;
        public bool CanCheck        { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (CanCheck) Set(ref _isChecked, value); }
        }

        public string StateLabel => State switch
        {
            BomDiffState.Match         => "Match",
            BomDiffState.Conflict      => "Conflict",
            BomDiffState.New           => "New",
            BomDiffState.InvenTreeOnly => "IT Only",
            BomDiffState.NoIpn         => "No IPN",
            BomDiffState.IpnNotFound   => "Not Found",
            BomDiffState.Ambiguous     => "Ambiguous",
            _                          => string.Empty,
        };

        public string DisplayIpn    => DiffLine.DisplayIpn;
        public string SwQty         => DiffLine.SwLine != null ? DiffLine.SwLine.Quantity.ToString("G29") : string.Empty;
        public string SwReference   => DiffLine.SwLine?.Reference ?? string.Empty;
        public string SwNote        => DiffLine.SwLine?.Note      ?? string.Empty;
        public string ItQty         => DiffLine.ItLine != null ? DiffLine.ItLine.Quantity.ToString("G29") : string.Empty;
        public string ItReference   => DiffLine.ItLine?.Reference      ?? string.Empty;
        public string ItNote        => DiffLine.ItLine?.Note           ?? string.Empty;
        public bool   ItConsumable  => DiffLine.ItLine?.Consumable     ?? false;
        public bool   ItOptional    => DiffLine.ItLine?.Optional       ?? false;
        public bool   ItValidated   => DiffLine.ItLine?.Validated      ?? false;
        public bool   HasSubstitutes => DiffLine.ItLine?.HasSubstitutes ?? false;

        public bool IsProblemState => State == BomDiffState.NoIpn
                                   || State == BomDiffState.IpnNotFound
                                   || State == BomDiffState.Ambiguous;

        public BomDiffLineViewModel(BomDiffLine diffLine)
        {
            DiffLine = diffLine;
            CanCheck = diffLine.State == BomDiffState.New
                    || diffLine.State == BomDiffState.Conflict;
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

        public event EventHandler<int>? BomSynced;

        private readonly IInventreeClient      _client;
        private readonly IAssemblyBomService   _bomService;
        private readonly PropertyMappingConfig _mapping;
        private readonly int                   _assemblyPk;
        private readonly string                _bomKeyword;

        public ObservableCollection<BomDiffLineViewModel> Lines { get; } =
            new ObservableCollection<BomDiffLineViewModel>();

        private string _statusText    = string.Empty;
        private bool   _isApplying;
        private string _sortColumn    = string.Empty;
        private bool   _sortAscending = true;

        public string StatusText    { get => _statusText;    set => Set(ref _statusText,    value); }
        public bool   IsApplying    { get => _isApplying;    set => Set(ref _isApplying,    value); }
        public string SortColumn    { get => _sortColumn;    set => Set(ref _sortColumn,    value); }
        public bool   SortAscending { get => _sortAscending; set => Set(ref _sortAscending, value); }

        public bool ApplyEnabled =>
            !IsApplying && Lines.Any(l => l.CanCheck && l.IsChecked);

        /// <summary>
        /// Inject to override confirmation logic in tests.
        /// Args: (newCount, updateCount). Return false to cancel.
        /// Default returns true (no dialog).
        /// </summary>
        public Func<int, int, bool> ConfirmPush { get; set; } = (_, __) => true;

        public BomCompareViewModel(
            IInventreeClient      client,
            IAssemblyBomService   bomService,
            PropertyMappingConfig mapping,
            int                   assemblyPk,
            string                bomKeyword = "inventree")
        {
            _client     = client;
            _bomService = bomService;
            _mapping    = mapping;
            _assemblyPk = assemblyPk;
            _bomKeyword = bomKeyword;
        }

        public async Task LoadAsync()
        {
            StatusText = "Loading…";
            var swLines = _bomService.GetBomLines(_bomKeyword, _mapping);
            var (itLines, lookup) = await Task.Run(async () =>
            {
                var it  = await _client.GetBomAsync(_assemblyPk).ConfigureAwait(false);
                var lup = await BuildIpnLookupAsync(swLines).ConfigureAwait(false);
                return (it, lup);
            });
            var diff = BomDiffEngine.Diff(swLines, itLines, lookup);
            RebindLines(diff);
            StatusText = string.Empty;
            if (!string.IsNullOrEmpty(_sortColumn)) ApplySort();
        }

        public async Task ApplyAsync()
        {
            var toProcess = Lines
                .Where(l => l.IsChecked && l.CanCheck)
                .Select(l => l.DiffLine)
                .ToList();

            int newCount    = toProcess.Count(l => l.State == BomDiffState.New);
            int updateCount = toProcess.Count(l => l.State == BomDiffState.Conflict);

            if (!ConfirmPush(newCount, updateCount)) return;

            // Verify the assembly part is flagged as Assembly in InvenTree before writing.
            var assemblyPart = await Task.Run(() => _client.GetPartByPkAsync(_assemblyPk));
            if (assemblyPart == null || !assemblyPart.IsAssembly)
            {
                StatusText = $"Cannot push — InvenTree part {_assemblyPk} is not flagged as Assembly. Edit it in InvenTree first.";
                return;
            }

            IsApplying = true;
            int created = 0, updated = 0, failed = 0;
            var failedIpns   = new List<string>();
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
                            false, false);
                        // Store the server-assigned PK so a subsequent update can reference it.
                        line.NewBomLinePk = newPk;
                        created++;
                    }
                    else if (line.State == BomDiffState.Conflict)
                    {
                        await _client.UpdateBomLineAsync(
                            line.ItLine!.Pk,
                            line.SwLine!.Quantity, line.SwLine.Reference, line.SwLine.Note,
                            line.ItLine.Consumable, line.ItLine.Optional);
                        updated++;
                    }
                    succeededVms.Add(vmByDiffLine[line]);
                }
                catch (Exception ex)
                {
                    failed++;
                    failedIpns.Add($"{line.DisplayIpn} ({ex.Message})");
                }
            }

            // Update pushed rows in-place — no full reload needed.
            foreach (var vm in succeededVms)
            {
                var sw = vm.DiffLine.SwLine!;

                if (vm.DiffLine.ItLine == null)
                {
                    // New line: create a stub ItLine from the values we just pushed.
                    vm.DiffLine.ItLine = new InventreeBomLine
                    {
                        Pk         = vm.DiffLine.NewBomLinePk,
                        SubPartPk  = vm.DiffLine.SubPartPk,
                        SubPartIpn = vm.DiffLine.DisplayIpn,
                        Quantity   = sw.Quantity,
                        Reference  = sw.Reference,
                        Note       = sw.Note,
                    };
                }
                else
                {
                    // Conflict: update the IT fields to match what we pushed.
                    vm.DiffLine.ItLine.Quantity  = sw.Quantity;
                    vm.DiffLine.ItLine.Reference = sw.Reference;
                    vm.DiffLine.ItLine.Note      = sw.Note;
                }

                vm.DiffLine.State = BomDiffState.Match;
                vm.IsChecked      = false;
                vm.NotifyStateChanged();
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyEnabled)));

            IsApplying = false;

            var parts = new List<string>();
            if (created > 0) parts.Add($"{created} created");
            if (updated > 0) parts.Add($"{updated} updated");
            if (failed  > 0) parts.Add($"{failed} failed ({string.Join(", ", failedIpns)})");
            StatusText = parts.Count > 0 ? string.Join(", ", parts) : "No changes applied";

            int diffCount = Lines.Count(l => l.State == BomDiffState.New || l.State == BomDiffState.Conflict);
            BomSynced?.Invoke(this, diffCount);
        }

        public void SortCommand(string columnName)
        {
            if (SortColumn == columnName)
                SortAscending = !SortAscending;
            else
            {
                SortColumn    = columnName;
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

            var tasks   = toFetch.Select(ipn => _client.GetPartsByIpnAsync(ipn).ContinueWith(
                t => (ipn, parts: t.Result), TaskContinuationOptions.ExecuteSynchronously));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var result = new Dictionary<string, IReadOnlyList<InventreePart>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (ipn, parts) in results)
                result[ipn] = parts;
            return result;
        }

        private void RebindLines(IReadOnlyList<BomDiffLine> diff)
        {
            Lines.Clear();
            foreach (var line in diff)
            {
                var vm = new BomDiffLineViewModel(line);
                vm.PropertyChanged += OnLineCheckedChanged;
                Lines.Add(vm);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyEnabled)));
        }

        private void OnLineCheckedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BomDiffLineViewModel.IsChecked))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApplyEnabled)));
        }

        private void ApplySort()
        {
            if (string.IsNullOrEmpty(SortColumn)) return;

            Func<BomDiffLineViewModel, string> key = SortColumn switch
            {
                "State"  => l => l.StateLabel,
                "IPN"    => l => l.DisplayIpn,
                "SwQty"  => l => l.SwQty,
                "SwRef"  => l => l.SwReference,
                "SwNote" => l => l.SwNote,
                "ItQty"  => l => l.ItQty,
                "ItRef"  => l => l.ItReference,
                "ItNote" => l => l.ItNote,
                "Cons"   => l => l.ItConsumable.ToString(),
                "Opt"    => l => l.ItOptional.ToString(),
                _        => l => l.DisplayIpn,
            };

            var normal   = Lines.Where(l => !l.IsProblemState).ToList();
            var problems = Lines.Where(l =>  l.IsProblemState).ToList();

            var sorted = SortAscending
                ? normal.OrderBy(key, StringComparer.OrdinalIgnoreCase).ToList()
                : normal.OrderByDescending(key, StringComparer.OrdinalIgnoreCase).ToList();

            sorted.AddRange(problems);

            Lines.Clear();
            foreach (var item in sorted) Lines.Add(item);
        }
    }
}
