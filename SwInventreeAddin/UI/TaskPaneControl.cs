using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    public class TaskPaneControl : UserControl
    {
        // Public surface used by automated tests — must not be removed or renamed.
        public TextBox PartNumberTextBox      { get; private set; } = null!;
        public TextBox NamePreviewTextBox     { get; private set; } = null!;
        public TextBox NotesPreviewTextBox    { get; private set; } = null!;
        public TextBox RevisionPreviewTextBox { get; private set; } = null!;
        public Button  ApplyButton            { get; private set; } = null!;
        public Label   StatusLabel            { get; private set; } = null!;

        // Current-document display fields (not part of test surface)
        private TextBox _currentDescriptionTextBox = null!;
        private TextBox _currentNotesTextBox        = null!;
        private TextBox _currentRevisionTextBox     = null!;

        private Panel _currentSection   = null!;
        private Panel _inventreeSection = null!;

        private readonly IInventreeClient         _client;
        private readonly IDocumentPropertyService _propertyService;
        private InventreePart?                    _lastFetchedPart;

        // Visual constants matching SolidWorks Custom Properties panel look
        private static readonly Font  SwFont          = new Font("Segoe UI", 9f);
        private static readonly Font  SwFontBold      = new Font("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Color SwBlue          = Color.FromArgb(0, 113, 188);
        private static readonly Color SwSectionBg     = Color.FromArgb(227, 227, 227);
        private static readonly Color SwSectionFg     = Color.FromArgb(50,  50,  50);
        private static readonly Color CurrentValueBg  = Color.FromArgb(242, 242, 242); // grey = existing
        private static readonly Color IncomingValueBg = Color.FromArgb(255, 255, 220); // cream = new
        private static readonly Color ReadOnlyFg      = Color.FromArgb(100, 100, 100);

        public TaskPaneControl(IInventreeClient client, IDocumentPropertyService propertyService)
        {
            _client          = client;
            _propertyService = propertyService;
            InitialiseControls();
            LoadPartNumber();
        }

        // -----------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------

        private void InitialiseControls()
        {
            Font        = SwFont;
            BackColor   = SystemColors.Window;
            AutoScroll  = true;
            Padding     = new Padding(6, 6, 6, 6);

            // -- OA Part Number --------------------------------------------------
            var lblPartNumber = MakeFieldLabel("OA Part Number");
            PartNumberTextBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 4) };

            var btnImport = new Button
            {
                Text      = "Import from InvenTree",
                Dock      = DockStyle.Top,
                Height    = 26,
                BackColor = SwBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(0, 2, 0, 6),
                Font      = SwFontBold,
            };
            btnImport.FlatAppearance.BorderSize = 0;
            btnImport.Click += async (s, e) => await FetchPartAsync();

            // -- Current in Document section -------------------------------------
            _currentSection = BuildSection(
                header: "Current in Document",
                build: p =>
                {
                    _currentDescriptionTextBox = MakeReadOnlyBox(p, CurrentValueBg, multiline: false);
                    AddFieldRow(p, "Name (Description)", _currentDescriptionTextBox);

                    _currentNotesTextBox = MakeReadOnlyBox(p, CurrentValueBg, multiline: true);
                    AddFieldRow(p, "Notes", _currentNotesTextBox);

                    _currentRevisionTextBox = MakeReadOnlyBox(p, CurrentValueBg, multiline: false);
                    AddFieldRow(p, "Revision", _currentRevisionTextBox);
                });
            _currentSection.Visible = false;

            // -- From InvenTree section ------------------------------------------
            _inventreeSection = BuildSection(
                header: "From InvenTree  (preview — not yet applied)",
                build: p =>
                {
                    NamePreviewTextBox = MakeReadOnlyBox(p, IncomingValueBg, multiline: false);
                    AddFieldRow(p, "Name → Description", NamePreviewTextBox);

                    NotesPreviewTextBox = MakeReadOnlyBox(p, IncomingValueBg, multiline: true);
                    AddFieldRow(p, "Notes", NotesPreviewTextBox);

                    RevisionPreviewTextBox = MakeReadOnlyBox(p, CurrentValueBg, multiline: false);
                    RevisionPreviewTextBox.ForeColor = ReadOnlyFg;
                    AddFieldRow(p, "Revision  (display only — never written)", RevisionPreviewTextBox);
                });
            _inventreeSection.Visible = false;

            // -- Apply & status --------------------------------------------------
            ApplyButton = new Button
            {
                Text      = "Apply to Document",
                Dock      = DockStyle.Top,
                Height    = 26,
                Enabled   = false,
                FlatStyle = FlatStyle.Flat,
                Margin    = new Padding(0, 4, 0, 4),
            };
            ApplyButton.Click += (s, e) => ApplyToDocument();

            StatusLabel = new Label
            {
                Text      = string.Empty,
                Dock      = DockStyle.Top,
                AutoSize  = false,
                Height    = 36,
                ForeColor = ReadOnlyFg,
            };

            // Controls.Add with DockStyle.Top: last added = topmost on screen.
            Controls.Add(StatusLabel);
            Controls.Add(ApplyButton);
            Controls.Add(_inventreeSection);
            Controls.Add(_currentSection);
            Controls.Add(btnImport);
            Controls.Add(PartNumberTextBox);
            Controls.Add(lblPartNumber);
        }

        /// <summary>Builds a titled section panel and calls <paramref name="build"/> to populate it.</summary>
        private static Panel BuildSection(string header, Action<Panel> build)
        {
            var section = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };

            // Content panel — controls stack inside using DockStyle.Top.
            // The content panel must be added BEFORE the header so the header docks on top.
            var content = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 2, 4, 0) };
            build(content);
            section.Controls.Add(content);

            var lblHeader = new Label
            {
                Text      = header,
                Dock      = DockStyle.Top,
                BackColor = SwSectionBg,
                ForeColor = SwSectionFg,
                Font      = SwFontBold,
                Padding   = new Padding(4, 3, 0, 3),
                AutoSize  = false,
                Height    = 22,
            };
            section.Controls.Add(lblHeader);

            return section;
        }

        /// <summary>Adds a label + textbox row to <paramref name="parent"/> using DockStyle.Top.</summary>
        private static void AddFieldRow(Panel parent, string labelText, Control field)
        {
            // Added last = docks to top, so add field first then label.
            parent.Controls.Add(field);
            parent.Controls.Add(MakeFieldLabel(labelText));
        }

        private static Label MakeFieldLabel(string text) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Top,
                ForeColor = SwBlue,
                Font      = SwFont,
                AutoSize  = false,
                Height    = 18,
                Padding   = new Padding(0, 4, 0, 0),
            };

        private static TextBox MakeReadOnlyBox(Panel parent, Color backColor, bool multiline)
        {
            var tb = new TextBox
            {
                ReadOnly  = true,
                BackColor = backColor,
                Dock      = DockStyle.Top,
                Multiline = multiline,
                Height    = multiline ? 52 : 21,
                Margin    = new Padding(0, 0, 0, 3),
            };
            return tb;
        }

        // -----------------------------------------------------------------------
        // Behaviour
        // -----------------------------------------------------------------------

        /// <summary>
        /// Reads current custom properties from the active document and fills the
        /// "Current in Document" section.  Clears the panel when no document is open.
        /// Called on startup and every time SolidWorks fires a document-change event.
        /// </summary>
        public void LoadPartNumber()
        {
            var partNo = _propertyService.GetCustomProperty("PartNo");

            if (string.IsNullOrEmpty(partNo))
            {
                ClearAll();
                return;
            }

            PartNumberTextBox.Text = partNo;

            _currentDescriptionTextBox.Text = _propertyService.GetCustomProperty("Description");
            _currentNotesTextBox.Text       = _propertyService.GetCustomProperty("Notes");
            _currentRevisionTextBox.Text    = _propertyService.GetCustomProperty("Revision");
            _currentSection.Visible         = true;
        }

        /// <summary>Clears all fields and hides preview sections.  Called when no document is open.</summary>
        public void ClearAll()
        {
            PartNumberTextBox.Text = string.Empty;

            _currentDescriptionTextBox.Text = string.Empty;
            _currentNotesTextBox.Text       = string.Empty;
            _currentRevisionTextBox.Text    = string.Empty;
            _currentSection.Visible         = false;

            NamePreviewTextBox.Text     = string.Empty;
            NotesPreviewTextBox.Text    = string.Empty;
            RevisionPreviewTextBox.Text = string.Empty;
            _inventreeSection.Visible   = false;

            ApplyButton.Enabled = false;
            StatusLabel.Text    = string.Empty;
            _lastFetchedPart    = null;
        }

        public async Task FetchPartAsync()
        {
            if (string.IsNullOrEmpty(PartNumberTextBox.Text))
                LoadPartNumber();

            var ipn = PartNumberTextBox.Text;
            if (string.IsNullOrEmpty(ipn))
            {
                StatusLabel.Text = "Enter an OA Part Number, or open a part and click Import again.";
                return;
            }

            StatusLabel.Text = "Fetching from InvenTree\u2026";

            try
            {
                var part = await _client.GetPartByIpnAsync(ipn);

                if (part == null)
                {
                    StatusLabel.Text          = $"No part found for IPN: {ipn}.";
                    _inventreeSection.Visible = false;
                    ApplyButton.Enabled       = false;
                    return;
                }

                NamePreviewTextBox.Text     = part.Name;
                NotesPreviewTextBox.Text    = part.Notes;
                RevisionPreviewTextBox.Text = part.Revision;
                _inventreeSection.Visible   = true;
                ApplyButton.Enabled         = true;
                _lastFetchedPart            = part;
                StatusLabel.Text            = string.Empty;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        public void ApplyToDocument()
        {
            if (_lastFetchedPart == null)
                return;

            _propertyService.SetCustomProperty("Description", _lastFetchedPart.Name);
            _propertyService.SetCustomProperty("Notes",       _lastFetchedPart.Notes);

            // Refresh current-document section to show the values just written.
            _currentDescriptionTextBox.Text = _lastFetchedPart.Name;
            _currentNotesTextBox.Text       = _lastFetchedPart.Notes;

            StatusLabel.Text = "Applied to document.";
        }
    }
}
