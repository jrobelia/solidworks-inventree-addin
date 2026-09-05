using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class BatchObservableCollectionTests
    {
        [Test]
        public void Reset_RaisesSingleCollectionChangedReset()
        {
            var col = new BatchObservableCollection<string>();
            col.Add("first");

            int collectionChanges = 0;
            var inpc = (INotifyPropertyChanged)col;
            col.CollectionChanged += (_, e) =>
            {
                collectionChanges++;
                Assert.That(e.Action, Is.EqualTo(NotifyCollectionChangedAction.Reset));
            };

            col.Reset(new[] { "one", "two", "three" });

            Assert.That(col.Count, Is.EqualTo(3));
            Assert.That(collectionChanges, Is.EqualTo(1), "Reset should raise exactly one CollectionChanged event.");
        }

        [Test]
        public void Reset_RaisesPropertyChangedForCountAndItemArray()
        {
            var col = new BatchObservableCollection<string>();
            col.Add("first");

            var propertyChanges = new List<string>();
            var inpc = (INotifyPropertyChanged)col;
            inpc.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName!);

            col.Reset(new[] { "one", "two", "three" });

            Assert.That(propertyChanges, Has.Member("Count"));
            Assert.That(propertyChanges, Has.Member("Item[]"));
        }
    }
}
