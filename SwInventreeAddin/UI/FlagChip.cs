using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// One read-only boolean flag chip in the InvenTree Info section.
    /// A stable, mutable presentation model: the ViewModel creates the seven
    /// chips once and updates <see cref="Value"/> in place via
    /// <see cref="INotifyPropertyChanged"/> when the part session changes.
    /// Updating in place raises only property-change notifications on the
    /// existing text/foreground bindings, so the WPF layout pass is
    /// confined to a single text/foreground change and never re-enters
    /// SolidWorks COM. (Rebuilding a bound collection with Clear + Add
    /// destroyed and recreated containers, ran a larger layout pass, and
    /// collided with a SolidWorks view repaint, re-entering the COM
    /// CustomPropertyManager and crashing the process. See #87.)
    /// </summary>
    public class FlagChip : INotifyPropertyChanged
    {
        /// <summary>Display label, e.g. "Active", "Assembly". Immutable.</summary>
        public string Name { get; }

        /// <summary>
        /// Raw flag value from InvenTree; null when no part is loaded.
        /// Settable so the ViewModel can update an existing chip in place
        /// instead of replacing the collection entry.
        /// </summary>
        public bool? Value
        {
            get => _value;
            set
            {
                if (!Set(ref _value, value)) return;
                OnPropertyChanged(nameof(Glyph));
                OnPropertyChanged(nameof(Display));
            }
        }
        private bool? _value;

        /// <summary>Green-check glyph for true, red-X glyph for false, empty for null.</summary>
        public string Glyph => Value == null ? string.Empty : (Value.Value ? "\u2713" : "\u2717");

        /// <summary>
        /// Full chip text (e.g. "Active: ✓"). Empty when no part is loaded so the
        /// chip is not visible. Bound directly to a single TextBlock to avoid
        /// multi-Run WPF binding issues inside the SolidWorks task pane.
        /// </summary>
        public string Display => Value == null ? string.Empty : $"{Name}: {Glyph}";

        public FlagChip(string name, bool? value = null)
        {
            Name  = name;
            _value = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
