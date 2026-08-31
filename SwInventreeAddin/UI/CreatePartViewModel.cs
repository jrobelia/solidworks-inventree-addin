using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

        private string _ipnEntry = string.Empty;
        /// <summary>Optional IPN entered by the user. When non-empty, sent as the part IPN on creation.</summary>
        public string IpnEntry
        {
            get => _ipnEntry;
            set
            {
                var changed = !Equals(_ipnEntry, value);
                if (changed)
                    Set(ref _ipnErrorText, string.Empty, nameof(IpnErrorText));

                var wasBlank = string.IsNullOrWhiteSpace(_ipnEntry);
                var isBlank  = string.IsNullOrWhiteSpace(value);

                Set(ref _ipnEntry, value);
                Set(ref _isWaitForServerIpnEnabled, isBlank, nameof(IsWaitForServerIpnEnabled));

                if (wasBlank && !isBlank)
                {
                    _waitForServerAssignedIpnRemembered = _waitForServerAssignedIpn;
                    Set(ref _waitForServerAssignedIpn, false, nameof(WaitForServerAssignedIpn));
                }
                else if (!wasBlank && isBlank)
                {
                    Set(ref _waitForServerAssignedIpn, _waitForServerAssignedIpnRemembered, nameof(WaitForServerAssignedIpn));
                }
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
        /// <summary>Can stock for this part be tracked by serial number?</summary>
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

        private bool _waitForServerAssignedIpn;
        private bool _waitForServerAssignedIpnRemembered;
        /// <summary>
        /// When true, the dialog waits and polls for a server-assigned IPN before closing.
        /// When false, the part is created without waiting.
        /// Remembered while the IPN field is blank so it can be restored when the field is cleared.
        /// </summary>
        public bool WaitForServerAssignedIpn
        {
            get => _waitForServerAssignedIpn;
            set
            {
                Set(ref _waitForServerAssignedIpn, value);
                if (_isWaitForServerIpnEnabled)
                    _waitForServerAssignedIpnRemembered = value;
            }
        }

        private bool _isWaitForServerIpnEnabled = true;
        /// <summary>
        /// True when the IPN field is blank, so waiting for a server-assigned IPN is applicable.
        /// False when the user has entered an IPN.
        /// </summary>
        public bool IsWaitForServerIpnEnabled
        {
            get => _isWaitForServerIpnEnabled;
            private set => Set(ref _isWaitForServerIpnEnabled, value);
        }

        private string _ipnErrorText = string.Empty;
        /// <summary>Validation error returned by InvenTree for a user-entered IPN.</summary>
        public string IpnErrorText
        {
            get => _ipnErrorText;
            private set => Set(ref _ipnErrorText, value);
        }

        private readonly BatchObservableCollection<CategoryNode> _rootCategories =
            new BatchObservableCollection<CategoryNode>();

        public ObservableCollection<CategoryNode> RootCategories => _rootCategories;

        // ── Constructor ───────────────────────────────────────────────────────

        public CreatePartViewModel(
            IInventreeClient          client,
            IDocumentPropertyService  propertyService,
            string                    initialName,
            IPropertyMappingProvider? mappingProvider            = null,
            int                       ipnPollDelayMs             = 500,
            bool                      waitForServerAssignedIpn = false,
            DocumentType              documentType               = DocumentType.Unknown)
        {
            _client                   = client;
            _propertyService          = propertyService;
            _mappingProvider          = mappingProvider;
            _ipnPollDelayMs           = ipnPollDelayMs;
            _waitForServerAssignedIpn           = waitForServerAssignedIpn;
            _waitForServerAssignedIpnRemembered = waitForServerAssignedIpn;
            PartName                            = initialName;

            // Seed the type flags from the SolidWorks document type, but keep both editable.
            Assembly  = documentType == DocumentType.Assembly;
            Component = documentType == DocumentType.Part || documentType == DocumentType.Assembly;
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
                    _rootCategories.Reset(cats.Select(c => new CategoryNode(c)));
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
                    node.Children.Reset(cats.Select(c => new CategoryNode(c)));
                    node.IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    node.Children.Reset(Array.Empty<CategoryNode?>());
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

            IsBusy      = true;
            IpnErrorText = string.Empty;
            StatusText  = "Checking IPN\u2026";

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
                            StatusText = $"IPN '{ipnToSubmit}' already exists. Enter a different IPN.";
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
                bool pollEnabled = _waitForServerAssignedIpn && ipnToSubmit == null;
                if (pollEnabled && string.IsNullOrEmpty(part.Ipn))
                {
                    const int maxAttempts = 20;
                    for (int i = 0; i < maxAttempts && string.IsNullOrEmpty(part?.Ipn); i++)
                    {
                        int secondsLeft = (maxAttempts - i) / 2;
                        RunOnUiThread(() => StatusText =
                            $"Waiting for server-assigned IPN\u2026 ({secondsLeft}s)");
                        await Task.Delay(_ipnPollDelayMs).ConfigureAwait(false);
                        part = await _client.GetPartByPkAsync(pk).ConfigureAwait(false);
                    }
                }

                var ipn  = part?.Ipn  ?? string.Empty;
                var name = part?.Name ?? string.Empty;

                var mappingResult = _mappingProvider?.GetMappingResult();

                if (mappingResult != null && !mappingResult.CanUseForPartSync)
                {
                    RunOnUiThread(() =>
                    {
                        // MessageOrDefault already carries the right severity for Invalid, NeedsUpgrade, and NewerSchema.
                        StatusText = mappingResult.MessageOrDefault;
                        IsBusy     = false;
                    });
                    return;
                }

                var mapping = mappingResult?.Config ?? PropertyMappingConfig.WithDefaults();

                RunOnUiThread(() =>
                {
                    if (!string.IsNullOrEmpty(mapping.PkProperty))
                        _propertyService.SetCustomProperty(mapping.PkProperty!, pk.ToString());
                    // Only write IPN if we actually received one — avoid blanking the property
                    // if the plugin timed out.
                    if (!string.IsNullOrEmpty(ipn) && !string.IsNullOrEmpty(mapping.IpnProperty))
                        _propertyService.SetCustomProperty(mapping.IpnProperty!, ipn);
                    if (!string.IsNullOrEmpty(mapping.NameProperty))
                        _propertyService.SetCustomProperty(mapping.NameProperty!, name);

                    // Show "refresh manually" only if the poll actually ran but IPN didn't arrive.
                    if (string.IsNullOrEmpty(ipn) && pollEnabled)
                        StatusText = "Part created. IPN not yet generated \u2014 refresh manually once the server assigns it.";

                    IsBusy = false;
                    PartCreated?.Invoke(this, part ?? new InventreePart { Pk = pk, Name = name });
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    var ipnError = ExtractIpnError(ex.Message);
                    if (!string.IsNullOrEmpty(ipnError))
                    {
                        IpnErrorText = ipnError;
                        StatusText   = ipnError;
                    }
                    else
                    {
                        StatusText = $"Error: {ex.Message}";
                    }
                    IsBusy = false;
                });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to extract the first IPN field error from an InvenTree validation
        /// response embedded in an exception message.
        /// </summary>
        private static string ExtractIpnError(string message)
        {
            var jsonStart = message.IndexOf('{');
            if (jsonStart < 0) return string.Empty;

            try
            {
                var json = message.Substring(jsonStart);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ipn", out var ipnErrors) &&
                    ipnErrors.ValueKind == JsonValueKind.Array)
                {
                    var errors = new List<string>();
                    foreach (var element in ipnErrors.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                            errors.Add(element.GetString() ?? string.Empty);
                    }
                    return string.Join(" ", errors);
                }
            }
            catch
            {
                // Ignore malformed JSON; the caller will show the raw message.
            }

            return string.Empty;
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
