using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// ObservableCollection that can be reset with a single CollectionChanged/Reset notification.
    /// Avoids the multiple Clear/Add events that cause re-entrant WPF layout passes.
    /// </summary>
    public class BatchObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>
        /// Clears the collection and replaces it with <paramref name="items"/> in a single Reset event.
        /// </summary>
        public void Reset(IEnumerable<T> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            CheckReentrancy();

            // Snapshot first so we do not enumerate into the same collection we are about to clear.
            var snapshot = new List<T>(items);

            Items.Clear();
            foreach (var item in snapshot)
                Items.Add(item);

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
