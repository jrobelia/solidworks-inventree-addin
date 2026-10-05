using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    // The Task Pane value-field tooltip contract (#299): every field's
    // tooltip is its own text, an empty field shows no tooltip, and the
    // "Read-only — value from InvenTree" message is gone. The mechanism
    // lives on the styles, so the tests consume the styles directly and
    // guard each field against a local ToolTip that would outrank the
    // style's empty-suppression trigger.
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class TaskPaneViewTooltipTests
    {
        // ── ValueFieldStyle: echo + suppression ────────────────────────────

        [Test]
        public void ValueFieldStyle_WhenTextPresent_ToolTipEchoesText()
        {
            var box = StyledBox(FindStyle("ValueFieldStyle"));

            box.Text = "Rev B";

            Assert.That(box.ToolTip, Is.EqualTo("Rev B"));
        }

        [Test]
        public void ValueFieldStyle_WhenTextEmpty_ToolTipIsNull()
        {
            var box = StyledBox(FindStyle("ValueFieldStyle"));
            box.Text = "Rev B";

            box.Text = string.Empty;

            Assert.That(box.ToolTip, Is.Null);
        }

        // ── InvenTreeFieldStyle: same contract, read-only message gone ─────

        [Test]
        public void InvenTreeFieldStyle_WhenTextPresent_ToolTipEchoesText()
        {
            var box = StyledBox(FindStyle("InvenTreeFieldStyle"));

            box.Text = "Rev B";

            Assert.That(box.ToolTip, Is.EqualTo("Rev B"));
        }

        [Test]
        public void InvenTreeFieldStyle_WhenTextEmpty_ToolTipIsNull()
        {
            var box = StyledBox(FindStyle("InvenTreeFieldStyle"));
            box.Text = "Rev B";

            box.Text = string.Empty;

            Assert.That(box.ToolTip, Is.Null);
        }

        // ── Status strip: tooltip still StatusToolTip, suppressed when empty ─────

        [Test]
        public void StatusTextBoxStyle_WhenTextPresent_ToolTipShowsStatusToolTip()
        {
            var box = StyledBox(FindStyle("StatusTextBoxStyle"));
            box.DataContext = new StatusContext { StatusToolTip = "suppressed detail" };

            box.Text = "Top entry";

            Assert.That(box.ToolTip, Is.EqualTo("suppressed detail"),
                        "the strip tooltip carries StatusToolTip detail, not the displayed text");
        }

        [Test]
        public void StatusTextBoxStyle_WhenTextEmpty_ToolTipIsNull()
        {
            var box = StyledBox(FindStyle("StatusTextBoxStyle"));
            box.DataContext = new StatusContext { StatusToolTip = "suppressed detail" };
            box.Text = "Top entry";

            box.Text = string.Empty;

            Assert.That(box.ToolTip, Is.Null);
        }

        // ── Per-field wiring ───────────────────────────────────────────────

        // The tooltip contract holds only if the field takes its tooltip from
        // the shared style: a local ToolTip value outranks the style trigger
        // and silently re-opens the empty-rectangle bug on that field.
        [TestCaseSource(nameof(ValueFieldCases))]
        public void ValueField_ToolTip_ComesFromSharedStyleNotLocalValue(
            string textPath, string styleKey)
        {
            var view = new TaskPaneView();
            var box = FindFieldByTextBinding(view, textPath);

            Assert.Multiple(() =>
            {
                Assert.That(box.Style, Is.SameAs(FindStyle(view, styleKey)),
                            $"{textPath} must use {styleKey}");
                Assert.That(box.ReadLocalValue(FrameworkElement.ToolTipProperty),
                            Is.EqualTo(DependencyProperty.UnsetValue),
                            $"{textPath}: a local ToolTip would outrank the style's empty suppression");
            });
        }

        // The read-only Part Number field above the Fetch button is out of
        // scope — it must not pick up the echo-tooltip style.
        [Test]
        public void PartNumberBox_KeepsSWFieldStyleAndNoToolTip()
        {
            var view = new TaskPaneView();
            var box = FindFieldByTextBinding(view, "PartNumber");

            Assert.Multiple(() =>
            {
                Assert.That(box.Style, Is.SameAs(FindStyle(view, "SWFieldStyle")));
                Assert.That(box.ToolTip, Is.Null);
            });
        }

        // ── Cases and helpers ──────────────────────────────────────────────

        private static readonly string[][] ValueFieldCases =
        {
            new[] { "CurrentName", "ValueFieldStyle" },
            new[] { "CurrentDescription", "ValueFieldStyle" },
            new[] { "CurrentNotes", "ValueFieldStyle" },
            new[] { "CurrentRevision", "ValueFieldStyle" },
            new[] { "CurrentPk", "ValueFieldStyle" },
            new[] { "NamePreview", "InvenTreeFieldStyle" },
            new[] { "DescriptionPreview", "InvenTreeFieldStyle" },
            new[] { "NotesPreview", "InvenTreeFieldStyle" },
            new[] { "RevisionPreview", "InvenTreeFieldStyle" },
            new[] { "PkPreview", "InvenTreeFieldStyle" },
            new[] { "InStockDisplay", "InvenTreeFieldStyle" },
            new[] { "OrderingDisplay", "InvenTreeFieldStyle" },
        };

        private static TextBox StyledBox(Style style)
        {
            // The styles' setters pull tokens (fonts, brushes) via
            // StaticResource — merge the same DesignTokens dictionary the
            // view carries so those lookups resolve on a bare TextBox.
            var box = new TextBox();
            box.Resources.MergedDictionaries.Add(Tokens());
            box.Style = style;
            return box;
        }

        // The view's first merged dictionary is DesignTokens.xaml.
        private static ResourceDictionary Tokens() =>
            new TaskPaneView().Resources.MergedDictionaries[0];

        private static Style FindStyle(string key) => FindStyle(new TaskPaneView(), key);

        private static Style FindStyle(TaskPaneView view, string key)
        {
            var style = view.TryFindResource(key) as Style;
            Assert.That(style, Is.Not.Null, $"TaskPaneView must resolve '{key}'.");
            return style!;
        }

        private static TextBox FindFieldByTextBinding(DependencyObject root, string path)
        {
            foreach (var box in AllTextBoxes(root))
            {
                var binding = BindingOperations.GetBinding(box, TextBox.TextProperty);
                if (binding?.Path?.Path == path)
                    return box;
            }

            Assert.Fail($"No TextBox in TaskPaneView binds Text to '{path}'.");
            return null!;
        }

        private static IEnumerable<TextBox> AllTextBoxes(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TextBox box)
                    yield return box;
                if (child is DependencyObject dependency)
                    foreach (var nested in AllTextBoxes(dependency))
                        yield return nested;
            }
        }

        private sealed class StatusContext
        {
            public string? StatusToolTip { get; set; }
        }
    }
}
