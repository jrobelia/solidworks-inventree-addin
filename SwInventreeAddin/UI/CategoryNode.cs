using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// A single node in the category tree.
    /// Pure C# — no WPF types.
    /// </summary>
    public class CategoryNode : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public InventreeCategory Category { get; }

        public CategoryNode(InventreeCategory category)
        {
            Category = category;
            // Sentinel child so the TreeView shows an expand arrow before children are loaded.
            if (category.HasChildren)
                _children.Add(null!);
        }

        // ── Bindable collections ─────────────────────────────────────────────

        private readonly BatchObservableCollection<CategoryNode?> _children =
            new BatchObservableCollection<CategoryNode?>();

        public ObservableCollection<CategoryNode?> Children => _children;

        /// <summary>Batch-resets the children collection without re-entrant WPF layout passes.</summary>
        internal void ResetChildren(IEnumerable<CategoryNode?> items)
            => _children.Reset(items);

        // ── State ────────────────────────────────────────────────────────────

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => Set(ref _isLoading, value);
        }
    }
}
