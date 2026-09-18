using System.Windows.Controls;
using System.Windows.Media;

namespace SwInventreeAddin.UI
{
    // The shared status-bar surface: a severity stripe plus a read-only
    // selectable text line. Callers report an outcome; the control owns the
    // rendering. Used wherever a section or the dialog footer reports what
    // an action did (ADR-0018's status-bar-next-to-the-action pattern).
    public partial class StatusBarControl : UserControl
    {
        public StatusBarControl()
        {
            InitializeComponent();
        }

        internal void SetStatus(string text, StatusSeverity severity)
        {
            StatusText.Text = text;
            StatusText.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            StatusStripe.Background = SeverityToBrush(severity);
        }

        private Brush SeverityToBrush(StatusSeverity severity) =>
            (Brush)FindResource(severity switch
            {
                StatusSeverity.Success => "BrushStatusSuccess",
                StatusSeverity.Warning => "BrushStatusWarning",
                StatusSeverity.Error => "BrushStatusError",
                _ => "BrushStatusNone",
            });
    }
}
