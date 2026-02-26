using System;
using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal dialog for configuring the InvenTree server URL and API key.
    /// Credentials are saved via IConfigProvider (DPAPI-encrypted in production).
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly IConfigProvider _configProvider;

        private TextBox _urlBox   = null!;
        private TextBox _apiBox   = null!;
        private Label   _status   = null!;
        private Button  _testBtn  = null!;
        private Button  _saveBtn  = null!;

        public SettingsForm(IConfigProvider configProvider)
        {
            _configProvider = configProvider;
            BuildForm();
            LoadCurrentValues();
        }

        private void BuildForm()
        {
            Text            = "InvenTree Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterScreen;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ClientSize      = new Size(550, 230);
            Font            = new Font("Segoe UI", 10f);
            BackColor       = SystemColors.Window;
            Padding         = new Padding(16);

            var lblUrl = new Label { Text = "Server URL", AutoSize = true, Location = new Point(16, 20) };
            _urlBox    = new TextBox { Location = new Point(16, 42), Width = 518, Font = Font };

            var lblKey = new Label { Text = "API Key", AutoSize = true, Location = new Point(16, 78) };
            _apiBox    = new TextBox { Location = new Point(16, 100), Width = 518, Font = Font };

            _status = new Label
            {
                Location  = new Point(16, 136),
                Width     = 518,
                Height    = 28,
                AutoSize  = false,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font      = new Font("Segoe UI", 9.5f),
            };

            _testBtn = new Button
            {
                Text     = "Test Connection",
                Location = new Point(16, 172),
                Width    = 140,
                Height   = 32,
                BackColor = Color.FromArgb(0, 112, 192),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            _testBtn.FlatAppearance.BorderSize = 0;
            _testBtn.Click += OnTestClick;

            _saveBtn = new Button
            {
                Text      = "Save",
                Location  = new Point(390, 172),
                Width     = 70,
                Height    = 32,
                BackColor = Color.FromArgb(0, 130, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            _saveBtn.FlatAppearance.BorderSize = 0;
            _saveBtn.Click += OnSaveClick;

            var cancelBtn = new Button
            {
                Text      = "Cancel",
                Location  = new Point(464, 172),
                Width     = 70,
                Height    = 32,
                FlatStyle = FlatStyle.Flat,
            };
            cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { lblUrl, _urlBox, lblKey, _apiBox, _status, _testBtn, _saveBtn, cancelBtn });
            AcceptButton = _saveBtn;
            CancelButton = cancelBtn;
        }

        private void LoadCurrentValues()
        {
            var config = _configProvider.GetServerConfig();
            if (config == null) return;
            _urlBox.Text = config.Url ?? string.Empty;
            _apiBox.Text = config.ApiKey ?? string.Empty;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_urlBox.Text) || string.IsNullOrWhiteSpace(_apiBox.Text))
            {
                _status.Text      = "Both URL and API Key are required.";
                _status.ForeColor = Color.FromArgb(180, 40, 0);
                return;
            }

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url    = _urlBox.Text.Trim(),
                    ApiKey = _apiBox.Text.Trim(),
                });
            }
            catch (Exception ex)
            {
                _status.Text      = $"Failed to save settings: {ex.Message}";
                _status.ForeColor = Color.FromArgb(180, 40, 0);
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private async void OnTestClick(object sender, EventArgs e)
        {
            var url    = _urlBox.Text.Trim();
            var apiKey = _apiBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            {
                _status.Text      = "Enter a URL and API Key before testing.";
                _status.ForeColor = Color.FromArgb(180, 40, 0);
                return;
            }

            _testBtn.Enabled = false;
            _status.Text      = "Testing\u2026";
            _status.ForeColor = Color.FromArgb(100, 100, 100);

            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(url);
                    client.DefaultRequestHeaders.Add("Authorization", $"Token {apiKey}");
                    var response = await client.GetAsync("api/part/?limit=1").ConfigureAwait(true);
                    if (response.IsSuccessStatusCode)
                    {
                        _status.Text      = "\u2713  Connection successful.";
                        _status.ForeColor = Color.FromArgb(0, 130, 60);
                    }
                    else
                    {
                        _status.Text      = $"Server responded with: {(int)response.StatusCode} {response.ReasonPhrase}";
                        _status.ForeColor = Color.FromArgb(180, 40, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                _status.Text      = $"Connection failed: {ex.Message}";
                _status.ForeColor = Color.FromArgb(180, 40, 0);
            }
            finally
            {
                _testBtn.Enabled = true;
            }
        }
    }
}
