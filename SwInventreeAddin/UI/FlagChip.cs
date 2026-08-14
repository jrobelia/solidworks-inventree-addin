using System.Collections.ObjectModel;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// One read-only boolean flag chip in the InvenTree Info section.
    /// Immutable presentation model; the ViewModel rebuilds the collection
    /// on each session change.
    /// </summary>
    public class FlagChip
    {
        /// <summary>Display label, e.g. "Active", "Assembly".</summary>
        public string Name { get; }

        /// <summary>Raw flag value from InvenTree; null when no part is loaded.</summary>
        public bool? Value { get; }

        /// <summary>Green-check glyph for true, red-X glyph for false, empty for null.</summary>
        public string Glyph => Value == null ? string.Empty : (Value.Value ? "\u2713" : "\u2717");

        public FlagChip(string name, bool? value)
        {
            Name  = name;
            Value = value;
        }
    }
}
