using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.SolidWorks;

namespace SwInventreeAddin.UI
{
    public class TaskPaneControl : UserControl
    {
        public TextBox PartNumberTextBox      { get; private set; } = null!;
        public TextBox NamePreviewTextBox     { get; private set; } = null!;
        public TextBox NotesPreviewTextBox    { get; private set; } = null!;
        public TextBox RevisionPreviewTextBox { get; private set; } = null!;
        public Button  ApplyButton            { get; private set; } = null!;
        public Label   StatusLabel            { get; private set; } = null!;

        private readonly IInventreeClient        _client;
        private readonly IDocumentPropertyService _propertyService;
        private InventreePart?                   _lastFetchedPart;
        private Panel                            _previewPanel = null!;

        public TaskPaneControl(IInventreeClient client, IDocumentPropertyService propertyService)
        {
            _client          = client;
            _propertyService = propertyService;
            InitialiseControls();
            LoadPartNumber();
        }

        private void InitialiseControls()
        {
            var lblPartNumber = new Label { Text = "OA Part Number", Dock = DockStyle.Top };

            PartNumberTextBox = new TextBox { Dock = DockStyle.Top };

            var btnImport = new Button { Text = "Import from InvenTree", Dock = DockStyle.Top };
            btnImport.Click += async (s, e) => await FetchPartAsync();

            _previewPanel = new Panel { Dock = DockStyle.Top, Visible = false, AutoSize = true };

            var lblRevisionCaption = new Label { Text = "InvenTree Revision:", Dock = DockStyle.Top };
            RevisionPreviewTextBox = new TextBox { ReadOnly = true, Dock = DockStyle.Top };
            var lblNotesCaption    = new Label { Text = "Notes:",             Dock = DockStyle.Top };
            NotesPreviewTextBox    = new TextBox { ReadOnly = true, Dock = DockStyle.Top };
            var lblNameCaption     = new Label { Text = "Name:",              Dock = DockStyle.Top };
            NamePreviewTextBox     = new TextBox { ReadOnly = true, Dock = DockStyle.Top };

            // Controls added in reverse order because DockStyle.Top stacks upward
            _previewPanel.Controls.Add(RevisionPreviewTextBox);
            _previewPanel.Controls.Add(lblRevisionCaption);
            _previewPanel.Controls.Add(NotesPreviewTextBox);
            _previewPanel.Controls.Add(lblNotesCaption);
            _previewPanel.Controls.Add(NamePreviewTextBox);
            _previewPanel.Controls.Add(lblNameCaption);

            ApplyButton = new Button { Text = "Apply to Document", Dock = DockStyle.Top, Enabled = false };
            ApplyButton.Click += (s, e) => ApplyToDocument();

            StatusLabel = new Label { Text = string.Empty, Dock = DockStyle.Top };

            Controls.Add(StatusLabel);
            Controls.Add(ApplyButton);
            Controls.Add(_previewPanel);
            Controls.Add(btnImport);
            Controls.Add(PartNumberTextBox);
            Controls.Add(lblPartNumber);
        }

        private void LoadPartNumber()
        {
            var partNo = _propertyService.GetCustomProperty("PartNo");
            PartNumberTextBox.Text = partNo;
            if (string.IsNullOrEmpty(partNo))
                StatusLabel.Text = "No PartNo property found on open document.";
        }

        public async Task FetchPartAsync()
        {
            var ipn = PartNumberTextBox.Text;
            if (string.IsNullOrEmpty(ipn))
            {
                StatusLabel.Text = "Enter an OA Part Number first.";
                return;
            }

            StatusLabel.Text = "Fetching from InvenTree\u2026";

            try
            {
                var part = await _client.GetPartByIpnAsync(ipn);

                if (part == null)
                {
                    StatusLabel.Text      = $"No part found for IPN: {ipn}.";
                    _previewPanel.Visible = false;
                    ApplyButton.Enabled   = false;
                    return;
                }

                NamePreviewTextBox.Text     = part.Name;
                NotesPreviewTextBox.Text    = part.Notes;
                RevisionPreviewTextBox.Text = part.Revision;
                _previewPanel.Visible       = true;
                ApplyButton.Enabled         = true;
                _lastFetchedPart            = part;
                StatusLabel.Text            = "Loaded.";
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
            StatusLabel.Text = "Applied to document.";
        }
    }
}
