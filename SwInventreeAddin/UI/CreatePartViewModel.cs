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
        private readonly bool                      _waitForAutoPartNumber;

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

        private string _ipnEntry = string.Empty;
        /// <summary>Optional IPN entered by the user. When non-empty, sent as the part IPN on creation.</summary>
        public string IpnEntry
        {
            get => _ipnEntry;
            set => Set(ref _ipnEntry, value);
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

        private bool _isLoadingCategories;
        /// <summary>True only while the top-level category list is being loaded. Used for the tree overlay.</summary>
        public bool IsLoadingCategories
        {
            get => _isLoadingCategories;
            private set => Set(ref _isLoadingCategories, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        private bool _assembly;
        /// <summary>Can this part be built from other parts?</summary>
        public bool Assembly
        {
            get => _assembly;
            set => Set(ref _assembly, value);
        }

        private bool _component;
        /// <summary>Can this part be used to build other parts?</summary>
        public bool Component
        {
            get => _component;
            set => Set(ref _component, value);
        }

        private bool _purchaseable;
        /// <summary>Can this part be purchased from external suppliers?</summary>
        public bool Purchaseable
        {
            get => _purchaseable;
            set => Set(ref _purchaseable, value);
        }

        private bool _salable;
        /// <summary>Can this part be sold to external customers?</summary>
        public bool Salable
        {
            get => _salable;
            set => Set(ref _salable, value);
        }

        private bool _trackable;
        /// <summary>Can stock for this part be tracked by batch or serial number?</summary>
        public bool Trackable
        {
            get => _trackable;
            set => Set(ref _trackable, value);
        }

        private bool _testable;
        /// <summary>Can stock for this part be tested on receipt?</summary>
        public bool Testable
        {
            get => _testable;
            set => Set(ref _testable, value);
        }

        private bool _copyCategoryParameters;
        /// <summary>Copy category-level parameter templates onto the newly created part.</summary>
        public bool CopyCategoryParameters
        {
            get => _copyCategoryParameters;
            set => Set(ref _copyCategoryParameters, value);
        }

        public ObservableCollection<CategoryNode> RootCategories { get; }
            = new ObservableCollection<CategoryNode>();

        // ── Constructor ───────────────────────────────────────────────────────

        public CreatePartViewModel(
            IInventreeClient          client,
            IDocumentPropertyService  propertyService,
            string                    initialName,
            IPropertyMappingProvider? mappingProvider       = null,
            int                       ipnPollDelayMs        = 500,
            bool                      waitForAutoPartNumber = false,
            DocumentType              documentType          = DocumentType.Unknown)
        {
            _client                = client;
            _propertyService       = propertyService;
            _mappingProvider       = mappingProvider;
            _ipnPollDelayMs        = ipnPollDelayMs;
            _waitForAutoPartNumber = waitForAutoPartNumber;
            PartName               = initialName;

            // Seed the type flags from the SolidWorks document type, but keep both editable.
            Assembly  = documentType == DocumentType.Assembly;
            Component = documentType == DocumentType.Part;
        }

        // ── Methods ───────────────────────────────────────────────────────────

        private bool CanCreate() =>
            !_isBusy
            && !string.IsNullOrWhiteSpace(_partName)
            && _selectedCategory != null;

        /// <summary>Loads top-level categories into RootCategories.</summary>
        public async Task LoadRootCategoriesAsync()
        {
            IsBusy             = true;
            IsLoadingCategories = true;
            StatusText         = "Loading categories\u2026";

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
                RunOnUiThread(() =>
                {
                    IsBusy             = false;
                    IsLoadingCategories = false;
                });
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
            StatusText = "Checking part number\u2026";

            try
            {
                var categoryPk  = _selectedCategory!.Category.Pk;
                var ipnToSubmit = string.IsNullOrWhiteSpace(_ipnEntry) ? null : _ipnEntry.Trim();

                // If the user supplied an IPN, make sure it is not already in use.
                // InvenTree may silently auto-generate a different number for a
                // duplicate, so a client-side check is needed before creating.
                if (!string.IsNullOrWhiteSpace(ipnToSubmit))
                {
                    var existing = await _client.GetPartByIpnAsync(ipnToSubmit!).ConfigureAwait(false);
                    if (existing != null)
                    {
                        RunOnUiThread(() =>
                        {
                            StatusText = $"Part number '{ipnToSubmit}' already exists. Enter a different part number.";
                            IsBusy     = false;
                        });
                        return;
                    }
                }

                RunOnUiThread(() => StatusText = "Creating part\u2026");
                var flags = new PartCreationFlags
                {
                    Assembly              = _assembly,
                    Component             = _component,
                    Purchaseable          = _purchaseable,
                    Salable               = _salable,
                    Trackable             = _trackable,
                    Testable              = _testable,
                    CopyCategoryParameters = _copyCategoryParameters,
                };
                var pk          = await _client.CreatePartAsync(categoryPk, _partName, ipnToSubmit, flags)
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
                // Poll only when the toggle is enabled and the user didn't supply an IPN.
                bool pollEnabled = _waitForAutoPartNumber && ipnToSubmit == null;
                if (pollEnabled && string.IsNullOrEmpty(part.Ipn))
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
                    _propertyService.SetCustomProperty(mapping.PkProperty, pk.ToString());
                    // Only write IPN if we actually received one — avoid blanking the property
                    // if the plugin timed out.
                    if (!string.IsNullOrEmpty(ipn))
                        _propertyService.SetCustomProperty(mapping.IpnProperty, ipn);
                    _propertyService.SetCustomProperty(mapping.NameProperty, name);

                    // Show "refresh manually" only if the poll actually ran but IPN didn't arrive.
                    if (string.IsNullOrEmpty(ipn) && pollEnabled)
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
