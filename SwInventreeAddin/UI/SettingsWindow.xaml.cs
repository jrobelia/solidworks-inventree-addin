using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// WPF settings dialog — replaces the old WinForms SettingsForm.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly IConfigProvider _configProvider;

        public SettingsWindow(IConfigProvider configProvider)
        {
            _configProvider = configProvider;
            InitializeComponent();

            // Try to centre over the SolidWorks main window.
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* cosmetic */ }

            // Pre-fill saved values.
            var config = _configProvider.GetServerConfig();
            if (config != null)
            {
                UrlBox.Text = config.Url    ?? string.Empty;
                ApiBox.Text = config.ApiKey ?? string.Empty;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text) || string.IsNullOrWhiteSpace(ApiBox.Text))
            {
                SetStatus("Both URL and API Key are required.", error: true);
                return;
            }

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url    = UrlBox.Text.Trim(),
                    ApiKey = ApiBox.Text.Trim(),
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to save: {ex.Message}", error: true);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var url    = UrlBox.Text.Trim();
            var apiKey = ApiBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus("Enter a URL and API Key before testing.", error: true);
                return;
            }

            SetStatus("Testing\u2026", error: false);

            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(url);
                    client.DefaultRequestHeaders.Add("Authorization", $"Token {apiKey}");
                    var response = await client.GetAsync("api/part/?limit=1").ConfigureAwait(true);

                    if (response.IsSuccessStatusCode)
                        SetStatus("\u2713  Connection successful.", error: false, success: true);
                    else
                        SetStatus($"Server responded: {(int)response.StatusCode} {response.ReasonPhrase}", error: true);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Connection failed: {ex.Message}", error: true);
            }
        }

        private void SetStatus(string text, bool error, bool success = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = error   ? new SolidColorBrush(Color.FromRgb(180, 40, 0))
                                  : success ? new SolidColorBrush(Color.FromRgb(0, 130, 60))
                                  :           (Brush)FindResource("BrushSubtle");
        }
    }
}
