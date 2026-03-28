using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SwInventreeAddin.Config;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Business logic for the Create Part dialog.
    /// Pure C# — no WPF types.
    /// </summary>
    public class CreatePartViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised with the new part after it is created and IPN+Name are written to the SW document.</summary>
        public event EventHandler<InventreePart>? PartCreated;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly IInventreeClient          _client;
        private readonly IDocumentPropertyService  _propertyService;
        private readonly IPropertyMappingProvider? _mappingProvider;
        private readonly int                       _ipnPollDelayMs;

        // ── Bindable properties ───────────────────────────────────────────────

        private string _partName = string.Empty;
        public string PartName
        {
            get => _partName;
            set
            {
                Set(ref _partName, value);
                Set(ref _createEnabled, CanCreate(), nameof(CreateEnabled));
            }
        }

        private CategoryNode? _selectedCategory;
        public CategoryNode? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                Set(ref _selectedCategory, value);
                Set(ref _createEnabled, CanCreate(), nameof(CreateEnabled));
            }
        }

        private bool _createEnabled;
        public bool CreateEnabled
        {
            get => _createEnabled;
            private set => Set(ref _createEnabled, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                Set(ref _isBusy, value);
                Set(ref _createEnabled, CanCreate(), nameof(CreateEnabled));
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public ObservableCollection<CategoryNode> RootCategories { get; }
            = new ObservableCollection<CategoryNode>();

        // ── Constructor ───────────────────────────────────────────────────────

        public CreatePartViewModel(
            IInventreeClient          client,
            IDocumentPropertyService  propertyService,
            string                    initialName,
            IPropertyMappingProvider? mappingProvider = null,
            int                       ipnPollDelayMs  = 500)
        {
            _client          = client;
            _propertyService = propertyService;
            _mappingProvider = mappingProvider;
            _ipnPollDelayMs  = ipnPollDelayMs;
            PartName         = initialName;
        }

        // ── Methods ───────────────────────────────────────────────────────────

        private bool CanCreate() =>
            !_isBusy
            && !string.IsNullOrWhiteSpace(_partName)
            && _selectedCategory != null;

        /// <summary>Loads top-level categories into RootCategories.</summary>
        public async Task LoadRootCategoriesAsync()
        {
            IsBusy     = true;
            StatusText = "Loading categories\u2026";

            try
            {
                var cats = await _client.GetCategoriesAsync(null).ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    RootCategories.Clear();
                    foreach (var c in cats)
                        RootCategories.Add(new CategoryNode(c));
                    StatusText = string.Empty;
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => StatusText = $"Error loading categories: {ex.Message}");
            }
            finally
            {
                RunOnUiThread(() => IsBusy = false);
            }
        }

        /// <summary>
        /// Called when the user expands a node that has not yet been loaded.
        /// Replaces the sentinel null child with real children.
        /// </summary>
        public async Task LoadChildrenAsync(CategoryNode node)
        {
            // Already loaded (or truly empty) — sentinel is the single null element.
            if (node.Children.Count != 1 || node.Children[0] != null)
                return;

            node.IsLoading = true;

            try
            {
                var cats = await _client.GetCategoriesAsync(node.Category.Pk)
                                        .ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    node.Children.Clear();
                    foreach (var c in cats)
                        node.Children.Add(new CategoryNode(c));
                    node.IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    node.Children.Clear();
                    node.IsLoading = false;
                    StatusText = $"Error loading children: {ex.Message}";
                });
            }
        }

        /// <summary>
        /// Creates the part, re-fetches it, writes IPN + Name to the SW document,
        /// then raises PartCreated. Leaves the dialog open on any error.
        /// </summary>
        public async Task CreateAsync()
        {
            if (!CanCreate()) return;

            IsBusy     = true;
            StatusText = "Creating part\u2026";

            try
            {
                var categoryPk = _selectedCategory!.Category.Pk;
                var pk         = await _client.CreatePartAsync(categoryPk, _partName)
                                              .ConfigureAwait(false);

                RunOnUiThread(() => StatusText = "Fetching new part\u2026");

                var part = await _client.GetPartByPkAsync(pk).ConfigureAwait(false);

                if (part == null)
                {
                    RunOnUiThread(() =>
                    {
                        StatusText = "Part created but re-fetch failed. IPN not yet written.";
                        IsBusy     = false;
                    });
                    return;
                }

                // InvenTree plugins generate the IPN asynchronously after the POST.
                // Poll until it appears or 10 seconds (20 × 500 ms) elapse.
                if (string.IsNullOrEmpty(part.Ipn))
                {
                    const int maxAttempts = 20;
                    for (int i = 0; i < maxAttempts && string.IsNullOrEmpty(part?.Ipn); i++)
                    {
                        int secondsLeft = (maxAttempts - i) / 2;
                        RunOnUiThread(() => StatusText =
                            $"Waiting for part number from server\u2026 ({secondsLeft}s)");
                        await Task.Delay(_ipnPollDelayMs).ConfigureAwait(false);
                        part = await _client.GetPartByPkAsync(pk).ConfigureAwait(false);
                    }
                }

                var ipn  = part?.Ipn  ?? string.Empty;
                var name = part?.Name ?? string.Empty;

                RunOnUiThread(() =>
                {
                    var mapping = _mappingProvider?.GetMapping() ?? new PropertyMappingConfig();
                    // Only write IPN if we actually received one — avoid blanking the property
                    // if the plugin timed out.
                    if (!string.IsNullOrEmpty(ipn))
                        _propertyService.SetCustomProperty(mapping.IpnProperty, ipn);
                    _propertyService.SetCustomProperty(mapping.NameProperty, name);

                    if (string.IsNullOrEmpty(ipn))
                        StatusText = "Part created. Part number not yet generated \u2014 refresh manually once the server assigns it.";

                    IsBusy = false;
                    PartCreated?.Invoke(this, part ?? new InventreePart { Pk = pk, Name = name });
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    StatusText = $"Error: {ex.Message}";
                    IsBusy     = false;
                });
            }
        }

        // ── Threading helper ──────────────────────────────────────────────────

        private readonly SynchronizationContext? _uiContext
            = SynchronizationContext.Current;

        private void RunOnUiThread(Action action)
        {
            if (_uiContext != null)
                _uiContext.Post(_ => action(), null);
            else
                action();
        }
    }
}
