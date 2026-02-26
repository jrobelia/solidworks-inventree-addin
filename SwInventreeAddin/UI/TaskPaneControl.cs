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
        // ApplyButton is a hidden sentinel — not shown in the UI, only used by tests
        // to check that the apply action becomes available after a successful fetch.
        public Button  ApplyButton            { get; private set; } = null!;
        // Per-field apply buttons shown in the UI.
        public Button  ApplyNameButton        { get; private set; } = null!;
        public Button  ApplyNotesButton       { get; private set; } = null!;
        // Push revision from SolidWorks up to InvenTree.
        public Button  PushRevisionButton     { get; private set; } = null!;
        public Label   StatusLabel            { get; private set; } = null!;
        // Load-from-InvenTree button — exposed so tests can check enabled state.
        public Button  FetchButton            { get; private set; } = null!;
        // Settings gear button — always present at the top of the panel.
        public Button  SettingsButton         { get; private set; } = null!;

        /// <summary>Raised when the user clicks the Settings button.</summary>
        public event EventHandler? SettingsRequested;

        // Current-document value boxes (shown immediately when a part opens)
        private TextBox _currentDescriptionBox = null!;
        private TextBox _currentNotesBox        = null!;
        private TextBox _currentRevisionBox     = null!;

        // InvenTree rows — hidden until a successful fetch
        private Panel _nameInvenTreeRow     = null!;
        private Panel _notesInvenTreeRow    = null!;
        private Panel _revisionInvenTreeRow = null!;

        private Panel _propertiesSection = null!;   // whole properties block; hidden when no doc open

        private IInventreeClient?                 _client;
        private readonly IDocumentPropertyService _propertyService;
        private InventreePart?                    _lastFetchedPart;

        // ── Style constants ────────────────────────────────────────────────────
        // Fonts — matched to SolidWorks Custom Properties panel
        private static readonly Font  UiFont      = new Font("Segoe UI", 11f);
        private static readonly Font  UiFontBold  = new Font("Segoe UI", 11f,   FontStyle.Bold);
        private static readonly Font  LabelFont   = new Font("Segoe UI", 10.5f, FontStyle.Bold);  // field label — bold like SW
        private static readonly Font  TagFont     = new Font("Segoe UI", 10f,  FontStyle.Italic);
        private static readonly Font  SectionFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        // Colours — white inputs match SW; cream distinguishes InvenTree data
        private static readonly Color LabelFg        = Color.FromArgb(30,  30,  30);   // dark, same as SW labels
        private static readonly Color SectionHeaderBg = Color.FromArgb(215, 215, 215); // subtle SW grey band
        private static readonly Color SectionHeaderFg = Color.FromArgb(30,  30,  30);
        private static readonly Color DividerGrey     = Color.FromArgb(185, 185, 185);
        private static readonly Color CurrentBg       = Color.White;                   // white — matches SW input boxes
        private static readonly Color IncomingBg      = Color.FromArgb(255, 252, 210); // cream — InvenTree data
        private static readonly Color TagCurrentFg    = Color.FromArgb(110, 110, 110);
        private static readonly Color TagNewFg        = Color.FromArgb(0,  130,  60);
        private static readonly Color ImportBtnBg     = Color.FromArgb(0,  112, 192);  // SW blue
        private static readonly Color ApplyBtnBg      = Color.FromArgb(0,  130,  60);  // green
        private const int BoxHeight   = 26;
        private const int NotesHeight = 80;

        public TaskPaneControl(IInventreeClient? client, IDocumentPropertyService propertyService)
        {
            _client          = client;
            _propertyService = propertyService;
            InitialiseControls();
            LoadPartNumber();  // calls ResetInvenTreeState(), which shows the no-server prompt if _client is null
        }

        // ── Build UI ──────────────────────────────────────────────────────────

        private void InitialiseControls()
        {
            Font       = UiFont;
            BackColor  = SystemColors.Window;
            AutoScroll = true;
            // 10 px left/right gives all DockStyle.Top children a consistent side margin.
            // 8 px top prevents the task pane host from clipping the first label.
            Padding    = new Padding(10, 8, 10, 0);

            // Controls use DockStyle.Top; add in BOTTOM-TO-TOP order.

            // Status label — side padding comes from UserControl, so only top padding here.
            StatusLabel = new Label
            {
                Text      = string.Empty,
                Dock      = DockStyle.Top,
                AutoSize  = false,
                Height    = 36,
                ForeColor = Color.FromArgb(100, 100, 100),
                Padding   = new Padding(0, 6, 0, 0),
                Font      = UiFont,
            };

            // ApplyButton is a hidden sentinel for tests — not added to Controls.
            ApplyButton = new Button { Enabled = false };

            // Properties comparison section
            _propertiesSection = BuildPropertiesSection();
            _propertiesSection.Visible = false;

            // Divider line above the properties section
            var divLine = MakeDivider();

            // Settings button — always enabled, lives above everything else.
            SettingsButton = MakeButton("\u2699 Settings", Color.FromArgb(230, 230, 230), DockStyle.Top);
            SettingsButton.ForeColor = Color.FromArgb(60, 60, 60);
            SettingsButton.Font = UiFont;
            SettingsButton.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

            // Load-from-InvenTree button
            FetchButton = MakeButton("Load Properties from InvenTree", ImportBtnBg, DockStyle.Top);
            // ConfigureAwait(false) on the outer await so the click-handler continuation
            // never tries to resume on the (possibly ShowDialog-disturbed) SyncContext.
            FetchButton.Click += async (s, e) => await FetchPartAsync().ConfigureAwait(false);

            // OA Part Number entry
            PartNumberTextBox = new TextBox
            {
                Dock    = DockStyle.Top,
                Height  = 26,
                Font    = UiFont,
            };

            // "OA Part Number" label — 8 px top gap before first label, inside label.
            var lblPn = MakeFieldLabel("OA Part Number");

            // Stack: bottom-to-top (StatusLabel at bottom, SettingsButton at top)
            Controls.Add(StatusLabel);
            Controls.Add(_propertiesSection);
            Controls.Add(divLine);
            Controls.Add(FetchButton);
            Controls.Add(PartNumberTextBox);
            Controls.Add(lblPn);
            Controls.Add(SettingsButton);
        }

        /// <summary>Builds the three-field comparison section (Name, Notes, Revision).</summary>
        private Panel BuildPropertiesSection()
        {
            var section = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0) };

            TextBox curNotes, inNotes; Panel rowNotes; Button? notesApply;
            var notesField = BuildComparisonField("Notes",
                out curNotes, out inNotes, out rowNotes, out notesApply, multiline: true, incomingReadOnly: false);
            _currentNotesBox   = curNotes;
            NotesPreviewTextBox = inNotes;
            _notesInvenTreeRow  = rowNotes;
            ApplyNotesButton = notesApply!;
            ApplyNotesButton.Click += (s, e) => ApplyNotesToDocument();

            TextBox curName, inName; Panel rowName; Button? nameApply;
            var nameField = BuildComparisonField("Name",
                out curName, out inName, out rowName, out nameApply, multiline: false, incomingReadOnly: false);
            _currentDescriptionBox = curName;
            NamePreviewTextBox     = inName;
            _nameInvenTreeRow      = rowName;
            ApplyNameButton = nameApply!;
            ApplyNameButton.Click += (s, e) => ApplyNameToDocument();

            TextBox curRev, inRev; Panel rowRev; Button? _;
            var revField = BuildComparisonField("Revision",
                out curRev, out inRev, out rowRev, out _, multiline: false, incomingReadOnly: true);
            _currentRevisionBox   = curRev;
            RevisionPreviewTextBox = inRev;
            _revisionInvenTreeRow  = rowRev;

            // Push Revision button — sends the SW revision up to InvenTree (opposite direction).
            PushRevisionButton = MakeButton("Push SW Rev to InvenTree", ImportBtnBg, DockStyle.Top);
            PushRevisionButton.Enabled = false;
            PushRevisionButton.Click  += async (s, e) =>
            {
                var confirm = MessageBox.Show(
                    "Push the SolidWorks revision to InvenTree?\n\nThis will overwrite the revision stored in InvenTree.",
                    "Confirm Push",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm == DialogResult.Yes)
                    await PushRevisionToInventreeAsync().ConfigureAwait(false);
            };

            var sectionHeader = BuildSectionHeader("Properties");

            section.Controls.Add(PushRevisionButton);
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
            out Button? applyButton,
            bool multiline,
            bool incomingReadOnly)
        {
            int h = multiline ? NotesHeight : BoxHeight;

            // 14 px top padding creates breathing room between fields.
            var group = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 14, 0, 0) };

            // Build the apply button wrapper (only for writable fields)
            applyButton = null;
            Panel? applyWrapper = null;
            if (!incomingReadOnly)
            {
                applyButton = new Button
                {
                    Text      = "Apply to SW Prop",
                    Dock      = DockStyle.Right,
                    Width     = 185,
                    Height    = 28,
                    BackColor = ApplyBtnBg,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font      = UiFont,
                    Enabled   = false,
                };
                applyButton.FlatAppearance.BorderSize = 0;
                applyWrapper = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 3, 0, 3) };
                applyWrapper.Controls.Add(applyButton);
            }

            // InvenTree row (cream) — controls added bottom-to-top
            incomingBox = new TextBox
            {
                ReadOnly  = incomingReadOnly,
                BackColor = incomingReadOnly ? CurrentBg : IncomingBg,
                Dock      = DockStyle.Top,
                Multiline = multiline,
                Height    = h,
                Font      = UiFont,
            };
            var tagNew = new Label
            {
                Text      = "InvenTree",
                Dock      = DockStyle.Top,
                Font      = TagFont,
                ForeColor = incomingReadOnly ? TagCurrentFg : TagNewFg,
                Height    = 22,
                Padding   = new Padding(2, 3, 0, 0),
            };
            incomingRow = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false };
            // Add bottom-to-top: applyWrapper (if any) → incomingBox → tagNew
            if (applyWrapper != null) incomingRow.Controls.Add(applyWrapper);
            incomingRow.Controls.Add(incomingBox);
            incomingRow.Controls.Add(tagNew);

            // Current row (white — matches SW input boxes)
            currentBox = new TextBox
            {
                ReadOnly  = true,
                BackColor = CurrentBg,
                Dock      = DockStyle.Top,
                Multiline = multiline,
                Height    = h,
                Font      = UiFont,
            };
            var tagCurrent = new Label
            {
                Text      = "Current",
                Dock      = DockStyle.Top,
                Font      = TagFont,
                ForeColor = TagCurrentFg,
                Height    = 22,
                Padding   = new Padding(2, 3, 0, 0),
            };

            var fieldLabel = MakeFieldLabel(label);

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

            // Solid grey band matching SolidWorks section headers ("Part Details", "Revision" etc.)
            var band = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 26,
                BackColor = SectionHeaderBg,
            };
            var lbl = new Label
            {
                Text      = title,
                Dock      = DockStyle.Fill,
                Font      = SectionFont,
                ForeColor = SectionHeaderFg,
                Padding   = new Padding(10, 4, 0, 0),
                AutoSize  = false,
            };
            band.Controls.Add(lbl);

            p.Controls.Add(band);
            return p;
        }

        private static Panel MakeDivider() =>
            new Panel { Dock = DockStyle.Top, Height = 1, BackColor = DividerGrey, Margin = new Padding(0) };

        private static Label MakeFieldLabel(string text) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Top,
                ForeColor = LabelFg,
                Font      = LabelFont,
                AutoSize  = false,
                // Height generously sized so bold text is never clipped vertically.
                // No left/right padding — the UserControl's Padding handles that.
                Height    = 28,
                Padding   = new Padding(0, 4, 0, 0),
            };

        private static Button MakeButton(string text, Color backColor, DockStyle dock)
        {
            var btn = new Button
            {
                Text      = text,
                Dock      = dock,
                Height    = 30,
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
        /// Re-reads Description, Notes, and Revision from the live document into the
        /// "Current" column. The OA Part Number box is intentionally not touched so
        /// the user can type a different IPN to search InvenTree with.
        /// </summary>
        private void RefreshCurrentProperties()
        {
            _currentDescriptionBox.Text = _propertyService.GetCustomProperty("Description");
            _currentNotesBox.Text       = _propertyService.GetCustomProperty("Notes");
            _currentRevisionBox.Text    = _propertyService.GetCustomProperty("Revision");
        }

        /// <summary>
        /// Hot-swaps the InvenTree client after credentials are saved in the Settings dialog.
        /// Pass null when no credentials are configured yet.
        /// </summary>
        public void UpdateClient(IInventreeClient? newClient)
        {
            _client = newClient;

            if (_client == null)
            {
                FetchButton.Enabled  = false;
                StatusLabel.Text     = "No server configured — click \u2699 Settings to get started";
                StatusLabel.ForeColor = Color.FromArgb(180, 80, 0);
            }
            else
            {
                FetchButton.Enabled  = true;
                StatusLabel.Text     = string.Empty;
                StatusLabel.ForeColor = Color.FromArgb(100, 100, 100);
            }
        }

        public void LoadPartNumber()
        {
            var partNo = _propertyService.GetCustomProperty("PartNo");

            if (string.IsNullOrEmpty(partNo))
            {
                ClearAll();
                return;
            }

            PartNumberTextBox.Text     = partNo;
            _propertiesSection.Visible = true;
            RefreshCurrentProperties();

            // Clear any stale InvenTree preview from a previous document
            ResetInvenTreeState();
        }

        /// <summary>Resets the entire panel. Called when no document is active.</summary>
        public void ClearAll()
        {
            PartNumberTextBox.Text          = string.Empty;
            _currentDescriptionBox.Text     = string.Empty;
            _currentNotesBox.Text           = string.Empty;
            _currentRevisionBox.Text        = string.Empty;
            _propertiesSection.Visible      = false;

            ResetInvenTreeState();

            // No document is open — override any enabled state left by ResetInvenTreeState.
            if (_client != null)
            {
                FetchButton.Enabled   = false;
                StatusLabel.Text      = "Open a part in SolidWorks to get started.";
                StatusLabel.ForeColor = Color.FromArgb(100, 100, 100);
            }
        }

        /// <summary>
        /// Clears all InvenTree-fetched data from the UI and disables action buttons.
        /// Called by both LoadPartNumber (document switch) and ClearAll (no document).
        /// </summary>
        private void ResetInvenTreeState()
        {
            _nameInvenTreeRow.Visible       = false;
            _notesInvenTreeRow.Visible      = false;
            _revisionInvenTreeRow.Visible   = false;
            NamePreviewTextBox.Text         = string.Empty;
            NotesPreviewTextBox.Text        = string.Empty;
            RevisionPreviewTextBox.Text     = string.Empty;
            ApplyButton.Enabled             = false;
            ApplyNameButton.Enabled         = false;
            ApplyNotesButton.Enabled        = false;
            PushRevisionButton.Enabled      = false;

            // If no client is configured, keep the orange prompt visible rather than clearing it.
            if (_client == null)
            {
                StatusLabel.Text      = "No server configured \u2014 click \u2699 Settings to get started";
                StatusLabel.ForeColor = Color.FromArgb(180, 80, 0);
                FetchButton.Enabled   = false;
            }
            else
            {
                StatusLabel.Text      = string.Empty;
                StatusLabel.ForeColor = Color.FromArgb(100, 100, 100);
            }
            _lastFetchedPart                = null;
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread.
        /// If called from a background thread and the control has a window handle,
        /// uses a synchronous Invoke so callers (including tests) see the result immediately.
        /// If there is no handle yet (e.g. in unit tests), runs directly on the current thread.
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (IsHandleCreated && InvokeRequired)
                Invoke(action);
            else
                action();
        }

        public async Task FetchPartAsync()
        {
            RefreshCurrentProperties();

            var ipn = PartNumberTextBox.Text;
            if (string.IsNullOrEmpty(ipn))
            {
                StatusLabel.Text = "Open a part in SolidWorks to get started.";
                return;
            }

            StatusLabel.Text    = "Fetching from InvenTree\u2026";
            ApplyButton.Enabled      = false;
            ApplyNameButton.Enabled  = false;
            ApplyNotesButton.Enabled = false;

            if (_client == null)
            {
                StatusLabel.Text = "No server configured — click \u2699 Settings to get started";
                return;
            }

            // ConfigureAwait(false) prevents resuming on the STA SynchronizationContext
            // (which ShowDialog may have disturbed).  The HTTP client also uses
            // ConfigureAwait(false) internally, so the real HTTP call never blocks the
            // STA thread.  In tests the stub returns synchronously, so ConfigureAwait(false)
            // is a no-op and everything stays on the calling thread — no hang.
            // After the await, RunOnUiThread marshals UI updates back to the STA thread
            // (via Invoke) in SolidWorks, or runs them directly in tests.
            InventreePart? part = null;
            Exception? fetchError = null;

            try   { part = await _client.GetPartByIpnAsync(ipn).ConfigureAwait(false); }
            catch (Exception ex) { fetchError = ex; }

            RunOnUiThread(new Action(() =>
            {

                if (fetchError != null)
                {
                    StatusLabel.Text = $"Error: {fetchError.Message}";
                    return;
                }

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
                ApplyNameButton.Enabled         = true;
                ApplyNotesButton.Enabled        = true;
                PushRevisionButton.Enabled      = true;
                _lastFetchedPart                = part;
                StatusLabel.Text                = string.Empty;
            }));
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

        public void ApplyNameToDocument()
        {
            if (_lastFetchedPart == null)
                return;

            // Write from the TextBox so user edits in the preview field are honoured.
            var value = NamePreviewTextBox.Text;
            _propertyService.SetCustomProperty("Description", value);
            _currentDescriptionBox.Text = value;
            StatusLabel.Text = "\u2713  Name applied.";
        }

        public void ApplyNotesToDocument()
        {
            if (_lastFetchedPart == null)
                return;

            // Write from the TextBox so user edits in the preview field are honoured.
            var value = NotesPreviewTextBox.Text;
            _propertyService.SetCustomProperty("Notes", value);
            _currentNotesBox.Text = value;
            StatusLabel.Text = "\u2713  Notes applied.";
        }

        public async Task PushRevisionToInventreeAsync()
        {
            if (_lastFetchedPart == null)
                return;

            if (_lastFetchedPart.Pk == 0)
            {
                StatusLabel.Text = "Error: cannot push revision \u2014 InvenTree part ID (Pk) is missing.";
                return;
            }

            var revision = _propertyService.GetCustomProperty("Revision");
            StatusLabel.Text = "Pushing revision to InvenTree\u2026";

            if (_client == null)
            {
                StatusLabel.Text = "No server configured \u2014 click \u2699 Settings to get started";
                return;
            }

            try
            {
                await _client.UpdatePartRevisionAsync(_lastFetchedPart.Pk, revision).ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    _lastFetchedPart.Revision   = revision;
                    RevisionPreviewTextBox.Text  = revision;
                    StatusLabel.Text             = "\u2713  Revision pushed to InvenTree.";
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => StatusLabel.Text = $"Error: {ex.Message}");
            }
        }
    }
}
