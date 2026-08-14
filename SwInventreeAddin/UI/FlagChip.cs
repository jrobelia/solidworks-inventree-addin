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
    /// existing text/foreground bindings, so the WPF <c>ItemsControl</c>
    /// never rebuilds its containers and never runs a layout pass on a
    /// session change. (Rebuilding the collection — Clear + Add — destroyed
    /// and recreated containers, and the layout burst collided with a
    /// SolidWorks view repaint, re-entering the COM CustomPropertyManager
    /// and crashing the process. See #87.)
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
                if (Set(ref _value, value))
                    OnPropertyChanged(nameof(Glyph));
            }
        }
        private bool? _value;

        /// <summary>Green-check glyph for true, red-X glyph for false, empty for null.</summary>
        public string Glyph => Value == null ? string.Empty : (Value.Value ? "\u2713" : "\u2717");

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
