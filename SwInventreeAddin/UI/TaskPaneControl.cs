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
        // Public surface used by automated tests.
        public TextBox PartNumberTextBox      { get; private set; } = null!;
        public TextBox NamePreviewTextBox     { get; private set; } = null!;
        public TextBox NotesPreviewTextBox    { get; private set; } = null!;
        public TextBox RevisionPreviewTextBox { get; private set; } = null!;
        public Button  ApplyButton            { get; private set; } = null!;
        public Label   StatusLabel            { get; private set; } = null!;

        // Current-document value boxes (shown immediately when a part opens)
        private TextBox _currentDescriptionBox = null!;
        private TextBox _currentNotesBox        = null!;
        private TextBox _currentRevisionBox     = null!;

        // InvenTree rows — hidden until a successful fetch
        private Panel _nameInvenTreeRow     = null!;
        private Panel _notesInvenTreeRow    = null!;
        private Panel _revisionInvenTreeRow = null!;

        private Panel _propertiesSection = null!;   // whole properties block; hidden when no doc open

        private readonly IInventreeClient         _client;
        private readonly IDocumentPropertyService _propertyService;
        private InventreePart?                    _lastFetchedPart;

        // ── Style constants ────────────────────────────────────────────────────
        private static readonly Font  UiFont       = new Font("Segoe UI", 9f);
        private static readonly Font  UiFontBold   = new Font("Segoe UI", 9f, FontStyle.Bold);
        private static readonly Font  TagFont      = new Font("Segoe UI", 7.5f, FontStyle.Italic);
        private static readonly Color FieldBlue    = Color.FromArgb(0,  112, 192);
        private static readonly Color DividerGrey  = Color.FromArgb(185, 185, 185);
        private static readonly Color CurrentBg    = Color.FromArgb(240, 240, 240);
        private static readonly Color IncomingBg   = Color.FromArgb(255, 252, 210);
        private static readonly Color TagCurrentFg = Color.FromArgb(130, 130, 130);
        private static readonly Color TagNewFg     = Color.FromArgb(0,  130,  60);
        private static readonly Color ImportBtnBg  = Color.FromArgb(0,  112, 192);
        private static readonly Color ApplyBtnBg   = Color.FromArgb(0,  130,  60);
        private const int BoxHeight   = 22;
        private const int NotesHeight = 52;

        public TaskPaneControl(IInventreeClient client, IDocumentPropertyService propertyService)
        {
            _client          = client;
            _propertyService = propertyService;
            InitialiseControls();
            LoadPartNumber();
        }

        // ── Build UI ──────────────────────────────────────────────────────────

        private void InitialiseControls()
        {
            Font       = UiFont;
            BackColor  = SystemColors.Window;
            AutoScroll = true;
            Padding    = new Padding(0);

            // Controls use DockStyle.Top; add in BOTTOM-TO-TOP order.

            // Status label
            StatusLabel = new Label
            {
                Text      = string.Empty,
                Dock      = DockStyle.Top,
                AutoSize  = false,
                Height    = 32,
                ForeColor = Color.FromArgb(100, 100, 100),
                Padding   = new Padding(10, 4, 10, 0),
            };

            // Apply button
            ApplyButton = MakeButton("Apply to Document", ApplyBtnBg, DockStyle.Top);
            ApplyButton.Enabled = false;
            ApplyButton.Margin  = new Padding(10, 0, 10, 6);
            ApplyButton.Click  += (s, e) => ApplyToDocument();

            // Properties comparison section
            _propertiesSection = BuildPropertiesSection();
            _propertiesSection.Visible = false;

            // Divider line above the properties section
            var divLine = MakeDivider();

            // Import button
            var btnImport = MakeButton("Import from InvenTree", ImportBtnBg, DockStyle.Top);
            btnImport.Margin  = new Padding(10, 4, 10, 8);
            btnImport.Click  += async (s, e) => await FetchPartAsync();

            // OA Part Number entry
            PartNumberTextBox = new TextBox
            {
                Dock    = DockStyle.Top,
                Margin  = new Padding(10, 0, 10, 4),
                Height  = BoxHeight,
            };

            var lblPn = MakeFieldLabel("OA Part Number");
            lblPn.Padding = new Padding(10, 8, 10, 2);

            // Stack: bottom-to-top
            Controls.Add(StatusLabel);
            Controls.Add(ApplyButton);
            Controls.Add(_propertiesSection);
            Controls.Add(divLine);
            Controls.Add(btnImport);
            Controls.Add(PartNumberTextBox);
            Controls.Add(lblPn);
        }

        /// <summary>Builds the three-field comparison section (Name, Notes, Revision).</summary>
        private Panel BuildPropertiesSection()
        {
            var section = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0) };

            // Use locals for out params because C# properties can't be passed as out arguments.
            TextBox curRev, inRev; Panel rowRev;
            var revField = BuildComparisonField("Revision",
                out curRev, out inRev, out rowRev, multiline: false, incomingReadOnly: true);
            _currentRevisionBox   = curRev;
            RevisionPreviewTextBox = inRev;
            _revisionInvenTreeRow  = rowRev;

            TextBox curNotes, inNotes; Panel rowNotes;
            var notesField = BuildComparisonField("Notes",
                out curNotes, out inNotes, out rowNotes, multiline: true, incomingReadOnly: false);
            _currentNotesBox   = curNotes;
            NotesPreviewTextBox = inNotes;
            _notesInvenTreeRow  = rowNotes;

            TextBox curName, inName; Panel rowName;
            var nameField = BuildComparisonField("Name  \u2192  Description",
                out curName, out inName, out rowName, multiline: false, incomingReadOnly: false);
            _currentDescriptionBox = curName;
            NamePreviewTextBox     = inName;
            _nameInvenTreeRow      = rowName;

            var sectionHeader = BuildSectionHeader("Properties");

            section.Controls.Add(revField);
            section.Controls.Add(notesField);
            section.Controls.Add(nameField);
            section.Controls.Add(sectionHeader);

            return section;
        }

        /// <summary>
        /// Builds a field group containing:
        ///   a blue field label,
        ///   a grey "Current" row (always visible once a doc is open),
        ///   a cream "InvenTree" row (hidden until a successful fetch).
        /// </summary>
        private static Panel BuildComparisonField(
            string label,
            out TextBox currentBox,
            out TextBox incomingBox,
            out Panel   incomingRow,
            bool multiline,
            bool incomingReadOnly)
        {
            int h = multiline ? NotesHeight : BoxHeight;

            var group = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };

            // InvenTree row (cream) — add first so it docks below the current row
            incomingBox = new TextBox
            {
                ReadOnly  = incomingReadOnly,
                BackColor = incomingReadOnly ? CurrentBg : IncomingBg,
                Dock      = DockStyle.Top,
                Multiline = multiline,
                Height    = h,
                Margin    = new Padding(10, 0, 10, 0),
            };
            var tagNew = new Label
            {
                Text      = incomingReadOnly ? "InvenTree  (display only \u2014 never written)" : "InvenTree",
                Dock      = DockStyle.Top,
                Font      = TagFont,
                ForeColor = incomingReadOnly ? TagCurrentFg : TagNewFg,
                Height    = 16,
                Padding   = new Padding(10, 0, 0, 0),
            };
            incomingRow = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false };
            incomingRow.Controls.Add(incomingBox);
            incomingRow.Controls.Add(tagNew);

            // Current row (grey)
            currentBox = new TextBox
            {
                ReadOnly  = true,
                BackColor = CurrentBg,
                Dock      = DockStyle.Top,
                Multiline = multiline,
                Height    = h,
                Margin    = new Padding(10, 0, 10, 0),
            };
            var tagCurrent = new Label
            {
                Text      = "Current",
                Dock      = DockStyle.Top,
                Font      = TagFont,
                ForeColor = TagCurrentFg,
                Height    = 16,
                Padding   = new Padding(10, 0, 0, 0),
            };

            var fieldLabel = MakeFieldLabel(label);
            fieldLabel.Padding = new Padding(10, 4, 10, 0);

            // Add bottom-to-top: incomingRow last so it appears below currentBox
            group.Controls.Add(incomingRow);
            group.Controls.Add(currentBox);
            group.Controls.Add(tagCurrent);
            group.Controls.Add(fieldLabel);

            return group;
        }

        private static Panel BuildSectionHeader(string title)
        {
            var p = new Panel { Dock = DockStyle.Top, AutoSize = true };

            var lbl = new Label
            {
                Text      = title,
                Dock      = DockStyle.Top,
                Font      = UiFontBold,
                ForeColor = Color.FromArgb(50, 50, 50),
                Height    = 20,
                Padding   = new Padding(10, 4, 0, 0),
            };
            var line = MakeDivider();

            p.Controls.Add(lbl);
            p.Controls.Add(line);
            return p;
        }

        private static Panel MakeDivider() =>
            new Panel { Dock = DockStyle.Top, Height = 1, BackColor = DividerGrey, Margin = new Padding(0) };

        private static Label MakeFieldLabel(string text) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Top,
                ForeColor = FieldBlue,
                Font      = UiFont,
                AutoSize  = false,
                Height    = 20,
                Padding   = new Padding(10, 2, 0, 0),
            };

        private static Button MakeButton(string text, Color backColor, DockStyle dock)
        {
            var btn = new Button
            {
                Text      = text,
                Dock      = dock,
                Height    = 27,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = UiFontBold,
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        // ── Behaviour ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the active document's custom properties and fills the "Current" column.
        /// Clears everything when no document is open.
        /// Called on startup and on every document open/switch/close event.
        /// </summary>
        public void LoadPartNumber()
        {
            var partNo = _propertyService.GetCustomProperty("PartNo");

            if (string.IsNullOrEmpty(partNo))
            {
                ClearAll();
                return;
            }

            PartNumberTextBox.Text          = partNo;
            _currentDescriptionBox.Text     = _propertyService.GetCustomProperty("Description");
            _currentNotesBox.Text           = _propertyService.GetCustomProperty("Notes");
            _currentRevisionBox.Text        = _propertyService.GetCustomProperty("Revision");
            _propertiesSection.Visible      = true;

            // Clear any stale InvenTree preview from a previous document
            _nameInvenTreeRow.Visible       = false;
            _notesInvenTreeRow.Visible      = false;
            _revisionInvenTreeRow.Visible   = false;
            NamePreviewTextBox.Text         = string.Empty;
            NotesPreviewTextBox.Text        = string.Empty;
            RevisionPreviewTextBox.Text     = string.Empty;
            ApplyButton.Enabled             = false;
            _lastFetchedPart                = null;
            StatusLabel.Text               = string.Empty;
        }

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            PartNumberTextBox.Text          = string.Empty;
            _currentDescriptionBox.Text     = string.Empty;
            _currentNotesBox.Text           = string.Empty;
            _currentRevisionBox.Text        = string.Empty;
            _propertiesSection.Visible      = false;

            NamePreviewTextBox.Text         = string.Empty;
            NotesPreviewTextBox.Text        = string.Empty;
            RevisionPreviewTextBox.Text     = string.Empty;
            _nameInvenTreeRow.Visible       = false;
            _notesInvenTreeRow.Visible      = false;
            _revisionInvenTreeRow.Visible   = false;

            ApplyButton.Enabled             = false;
            StatusLabel.Text               = string.Empty;
            _lastFetchedPart               = null;
        }

        public async Task FetchPartAsync()
        {
            if (string.IsNullOrEmpty(PartNumberTextBox.Text))
                LoadPartNumber();

            var ipn = PartNumberTextBox.Text;
            if (string.IsNullOrEmpty(ipn))
            {
                StatusLabel.Text = "Open a part first, or type an OA Part Number.";
                return;
            }

            StatusLabel.Text   = "Fetching from InvenTree\u2026";
            ApplyButton.Enabled = false;

            try
            {
                var part = await _client.GetPartByIpnAsync(ipn);

                if (part == null)
                {
                    StatusLabel.Text              = $"No part found in InvenTree for: {ipn}";
                    _nameInvenTreeRow.Visible     = false;
                    _notesInvenTreeRow.Visible    = false;
                    _revisionInvenTreeRow.Visible = false;
                    return;
                }

                NamePreviewTextBox.Text         = part.Name;
                NotesPreviewTextBox.Text        = part.Notes;
                RevisionPreviewTextBox.Text     = part.Revision;
                _nameInvenTreeRow.Visible       = true;
                _notesInvenTreeRow.Visible      = true;
                _revisionInvenTreeRow.Visible   = true;
                ApplyButton.Enabled             = true;
                _lastFetchedPart               = part;
                StatusLabel.Text               = string.Empty;
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

            // Refresh current column to reflect what was just written
            _currentDescriptionBox.Text = _lastFetchedPart.Name;
            _currentNotesBox.Text       = _lastFetchedPart.Notes;

            StatusLabel.Text = "\u2713  Applied to document.";
        }
    }
}
