using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    internal static class LogicalTreeTestHelper
    {
        internal static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
            where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is T typed)
                    yield return typed;

                foreach (var descendant in FindLogicalChildren<T>(child))
                    yield return descendant;
            }
        }

        internal static void AssertBandedHeader(FrameworkElement root, string title)
        {
            var header = FindLogicalChildren<TextBlock>(root)
                .FirstOrDefault(tb => tb.Text == title);

            Assert.That(header, Is.Not.Null, $"Could not find header '{title}'.");
            Assert.That((header!.Parent as Border)?.Background,
                Is.SameAs(root.FindResource("BrushSectionHeader")),
                $"'{title}' must sit in a banded section header");
        }
    }
}
