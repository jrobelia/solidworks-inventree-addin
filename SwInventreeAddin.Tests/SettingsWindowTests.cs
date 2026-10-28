using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using SwInventreeAddin.Config;
using SwInventreeAddin.Tests.Stubs;
using SwInventreeAddin.UI;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace SwInventreeAddin.Tests
{
    // Genuinely visual assertions only — every rule assertion (orchestration,
    // precedence, probe lifecycle, the mapping projection, status wording)
    // lives in SettingsViewModelTests. What stays here: tooltips, read-only
    // boxes, PasswordBox dots/placeholders, dialog launching, the card never
    // rendering the saved key, layout/styling, and the render wiring that
    // forwards VM outputs into the controls.
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class SettingsWindowTests
    {
        private string _localMappingPath = null!;
        private SynchronizationContext? _previousContext;

        [SetUp]
        public void SetUp()
        {
            _localMappingPath = Path.Combine(Path.GetTempPath(),
                $"settings_window_mapping_{Guid.NewGuid():N}.json");

            // The VM marshals UI-bound updates through SynchronizationContext.Send.
            // NUnit's STA context queues Sends where only NUnit's own wait loop
            // drains them — a Dispatcher.PushFrame inside a test would deadlock.
            // The WPF dispatcher's context is pumped by any frame (and by
            // NUnit's STA pump), matching what the VM captures in production.
            _previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        }

        [TearDown]
        public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(_previousContext);

            if (File.Exists(_localMappingPath))
                File.Delete(_localMappingPath);
        }

        // ── Window chrome ────────────────────────────────────────────────────

        [Test]
        public void Title_ShowsTheBrandedNameAndInstalledVersion()
        {
            var window = CreateWindow(versionInfo: new StubVersionInfo { Version = "9.9.9-test" });

            Assert.That(window.Title, Is.EqualTo("InvenTree Link Settings - v9.9.9-test"));
        }

        // ── Mapping section rendering ────────────────────────────────────────
        // The health→severity/label/enabled projection lives in the VM tests;
        // what remains here proves the status bar's chrome forwards the pair.

        [Test]
        public void MappingStatusText_LongMessage_ToolTipContainsFullMessage()
        {
            var longMessage = "Invalid mapping file: " + new string('x', 500);
            var mappingProvider = new StubPropertyMappingProvider
            {
                LocalFilePath = _localMappingPath,
                Health = MappingHealth.Invalid,
                Message = longMessage
            };

            var window = CreateWindow(mappingProvider: mappingProvider);
            var textBox = GetStatusBarTextBox(window, "MappingStatusBar");

            Assert.That(textBox.ToolTip, Is.InstanceOf<string>());
            Assert.That((string)textBox.ToolTip, Does.Contain(longMessage));
            Assert.That(textBox.ToolTip, Is.EqualTo(textBox.Text));
        }

        // ── Status bar chrome ────────────────────────────────────────────────

        [TestCase("ActionStatusBar")]
        [TestCase("ConnectionStatusBar")]
        [TestCase("MappingStatusBar")]
        public void StatusBarText_IsReadOnlySelectableTextBox(string barName)
        {
            var window = CreateWindow();
            var textBox = GetStatusBarTextBox(window, barName);

            Assert.That(textBox.IsReadOnly, Is.True);
            Assert.That(textBox.Focusable, Is.True);
            Assert.That(textBox.IsTabStop, Is.False);
        }

        [Test]
        public void ActionStatusText_LongError_ToolTipContainsFullMessage()
        {
            var longMessage = "Failed to save server settings: " + new string('x', 500);
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException(longMessage),
            };

            var window = CreateWindow(applyService: applyService);

            Click(window, "ApplyButton");

            var textBox = GetStatusBarTextBox(window, "ActionStatusBar");
            Assert.That(textBox.ToolTip, Is.InstanceOf<string>());
            Assert.That((string)textBox.ToolTip, Does.Contain(longMessage));
        }

        // ── Status card rendering ────────────────────────────────────────────
        // The card appears once anything is saved; the layout never changes
        // between states — the title, dot, and line values do.

        [Test]
        public void Constructor_WithNoSavedConfig_HidesStatusCard()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetElement(window, "ConnectionCard").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_WithNoSavedConfig_ShowsTheFormDirectly()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
            });
        }

        [Test]
        public void Constructor_WithFullConfig_HidesTheForm()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetCredentialForm(window).Visibility,
                            Is.EqualTo(Visibility.Collapsed));
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void StatusCard_NeverShowsTheSavedApiKey()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "super-secret-key"));

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardServer"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardCredential"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetText(window, "ConnectionCardConnection"),
                            Does.Not.Contain("super-secret-key"));
                Assert.That(GetPasswordBox(window, "ApiKeyBox").Password, Is.Empty,
                            "the saved key is never re-shown in the field");
            });
        }

        // The mode switcher and the masked/revealed field pair are gone — one
        // form, one masked key field, no Show/Hide control.
        [Test]
        public void Constructor_NoModeSwitcherOrRevealControlsRemain()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "AccountModeButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiKeyModeButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ShowApiKeyButton"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiBox"), Is.Null);
                Assert.That(LogicalTreeHelper.FindLogicalNode(window, "ApiKeyMaskedBox"), Is.Null);
            });
        }

        // ── Open probe → card render ─────────────────────────────────────────
        // The verdict→card projection is a VM rule (SettingsViewModelTests);
        // what stays here proves the Close → cancel wiring only.

        // The misbehave knob violates the test-connection contract on purpose
        // so the Closed → cancel wiring gets a real discard to prove.
        [Test, Timeout(15000)]
        public void OpenProbe_WhenLateVerdictArrivesAfterClose_IsDiscarded()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();
            SolidWorksWindowHandle.Set(form.Handle);

            try
            {
                var pending = new TaskCompletionSource<ConnectionProbeResult>();
                var applyService = new StubSettingsApplyService
                {
                    PendingTestResult = pending,
                    IgnoreCallerCancellation = true,
                };
                var window = CreateWindow(
                    applyService: applyService,
                    configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

                window.Show();
                Assert.That(
                    HiddenTestWindow.IsOnScreen(
                        HiddenTestWindow.GetRect(new WindowInteropHelper(window).Handle)),
                    Is.False, "Test dialog must stay off every monitor");
                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Testing connection…"));

                // Close cancels the probe's token, but the stub ignores it and
                // still delivers a normal verdict — the window must discard it.
                window.Close();
                pending.SetResult(new ConnectionProbeResult(
                    ConnectionProbeStatus.Connected, "Connection successful."));
                WaitForProbe(window);

                Assert.That(GetText(window, "ConnectionCardTitle"),
                            Is.EqualTo("Testing connection…"),
                            "a verdict landing after Close is discarded, never applied to the card");
            }
            finally
            {
                SolidWorksWindowHandle.Set(IntPtr.Zero);
            }
        }

        // ── ConnectionStateChanged forward (#241 reach path) ─────────────────

        [Test]
        public void ConnectionStateChanged_ForwardsTheViewModelNotification()
        {
            var window = CreateWindow();
            ServerConnectionStatus? received = null;
            window.ConnectionStateChanged += (_, status) => received = status;

            Click(window, "TestConnectionButton");

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Indicator, Is.EqualTo(ServerConnectionIndicator.Connected));
        }

        // ── Dots placeholder ─────────────────────────────────────────────────
        // Dots stand in for a saved key — shown only when a key is actually
        // saved, never for a server-only config, and never as editable text.

        [Test]
        public void Constructor_WithSavedKey_ShowsDotsOnApiKeyField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void Constructor_WithServerOnlySaved_ShowsNoDotsOnApiKeyField()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", string.Empty));

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void Constructor_WithNoSavedConfig_ShowsNoDotsOnApiKeyField()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        [Test]
        public void ApiKeyBox_WhenTyped_HidesDotsPlaceholder()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                        Is.EqualTo(Visibility.Collapsed));
        }

        // ── Card toolbar actions ─────────────────────────────────────────────

        [Test]
        public void ChangeCredential_WhenClicked_RevealsOnlyTheCredentialFields()
        {
            var window = CreateWindow();

            Click(window, "ChangeCredentialButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetElement(window, "UrlFieldPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed));
            });
        }

        // ── Card toolbar vs design ───────────────────────────────────────────
        // Design v2 (docs/design/settings-window-v2.png): the action row sits
        // under a thin divider, spreads across the card — change actions left,
        // the destructive action right — the card is the light interactive
        // surface, the change buttons wear standard chrome grey, and
        // Remove API key carries the error-red destructive treatment.

        [Test]
        public void CardToolbar_ChangeButtons_WearStandardChromeOnTheLightCard()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var card = GetElement(window, "ConnectionCard") as Border;
            var chrome = GetBrush(window, "BrushSectionHeader");

            Assert.Multiple(() =>
            {
                Assert.That(((SolidColorBrush)card!.Background).Color,
                            Is.EqualTo(Colors.White),
                            "the card is the light interactive surface");
                Assert.That(GetButton(window, "ChangeServerButton").Background,
                            Is.SameAs(chrome),
                            "Change server wears the standard secondary chrome");
                Assert.That(GetButton(window, "ChangeCredentialButton").Background,
                            Is.SameAs(chrome),
                            "Change credential wears the standard secondary chrome");
            });
        }

        // Any fill must come from the style, not a local value: a local
        // Background outranks SecondaryButtonStyle's IsMouseOver trigger and
        // silently removes the hover feedback.
        [Test]
        public void CardToolbar_ChangeButtons_SetNoLocalFillSoHoverSurvives()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var cardToolbarStyle = window.TryFindResource("CardToolbarButtonStyle") as Style;
            Assert.That(cardToolbarStyle, Is.Not.Null,
                        "DesignTokens.xaml must define CardToolbarButtonStyle");

            Assert.Multiple(() =>
            {
                foreach (var name in new[] { "ChangeServerButton", "ChangeCredentialButton" })
                {
                    var button = GetButton(window, name);
                    Assert.That(button.Style, Is.SameAs(cardToolbarStyle), name);
                    Assert.That(button.ReadLocalValue(Button.BackgroundProperty),
                                Is.EqualTo(DependencyProperty.UnsetValue),
                                $"{name}: a local Background would beat the hover trigger");
                }
            });
        }

        [Test]
        public void CardToolbar_RemoveApiKeyButton_UsesDestructiveStyling()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var button = GetButton(window, "RemoveApiKeyButton");
            var error = GetBrush(window, "BrushStatusError");

            Assert.Multiple(() =>
            {
                Assert.That(button.Foreground, Is.SameAs(error),
                            "the destructive action's text is error red");
                Assert.That(button.BorderBrush, Is.SameAs(error),
                            "the destructive action's outline is error red");
                Assert.That(button.BorderThickness, Is.EqualTo(new Thickness(1)),
                            "the red outline must actually render");
            });
        }

        // The destructive action's hover is the design's light-red tint, not
        // the shared grey — a style trigger on DangerButtonStyle overrides the
        // inherited template hover (style triggers outrank template triggers).
        [Test]
        public void CardToolbar_RemoveApiKeyButton_HoverUsesTheDangerTint()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var dangerStyle = window.TryFindResource("DangerButtonStyle") as Style;
            Assert.That(dangerStyle, Is.Not.Null,
                        "DesignTokens.xaml must define DangerButtonStyle");

            var hover = dangerStyle!.Triggers.OfType<Trigger>()
                .FirstOrDefault(t => t.Property == UIElement.IsMouseOverProperty);
            Assert.That(hover, Is.Not.Null,
                        "DangerButtonStyle needs a style-level IsMouseOver trigger — otherwise the inherited template hover paints it grey");

            var fill = hover!.Setters.OfType<Setter>()
                .FirstOrDefault(s => s.Property == Control.BackgroundProperty);
            Assert.That(fill, Is.Not.Null, "the hover trigger must set Background");
            Assert.That(((SolidColorBrush)fill!.Value).Color,
                        Is.EqualTo(Color.FromArgb(0xFF, 0xFD, 0xEC, 0xEA)),
                        "the destructive hover tint is the design's light red #FDECEA");
        }

        [Test]
        public void CardToolbar_Layout_SpreadsAcrossTheCardUnderADivider()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var divider = LogicalTreeHelper.FindLogicalNode(window, "ConnectionCardToolbarDivider")
                          as FrameworkElement;
            Assert.That(divider, Is.InstanceOf<Separator>(),
                        "a thin divider separates the status lines from the action row");

            var changeServer = GetButton(window, "ChangeServerButton");
            var changeCredential = GetButton(window, "ChangeCredentialButton");
            var remove = GetButton(window, "RemoveApiKeyButton");

            var row = LogicalTreeHelper.GetParent(remove) as Grid;
            Assert.That(row, Is.Not.Null, "the action row is a Grid so it can span the card");

            Assert.Multiple(() =>
            {
                Assert.That(IsInside(window, "ChangeServerButton", "ConnectionCardToolbar"), Is.True);
                Assert.That(IsInside(window, "ChangeCredentialButton", "ConnectionCardToolbar"), Is.True);
                Assert.That(IsInside(window, "RemoveApiKeyButton", "ConnectionCardToolbar"), Is.True);
                Assert.That(IsInside(window, "ConnectionCardToolbarDivider", "ConnectionCardToolbar"),
                            Is.True, "the divider collapses with the toolbar");
                Assert.That(Grid.GetRow(divider!), Is.LessThan(Grid.GetRow(row!)),
                            "the divider sits above the button row");

                // A * column between the change pair and the destructive action
                // spreads the row: changes left-of-center, remove at the right.
                int removeColumn = Grid.GetColumn(remove);
                int lastChangeColumn = Math.Max(
                    Grid.GetColumn(changeServer), Grid.GetColumn(changeCredential));
                bool starBetween = row!.ColumnDefinitions
                    .Where((c, i) => i > lastChangeColumn && i < removeColumn)
                    .Any(c => c.Width.IsStar);
                Assert.That(starBetween, Is.True,
                            "a * column pushes Remove API key to the card's right edge");
                Assert.That(removeColumn, Is.EqualTo(row!.ColumnDefinitions.Count - 1),
                            "Remove API key anchors the right edge");
            });
        }

        // ── Apply / Test / Save click → render ───────────────────────────────
        // One proof per action that the click reaches the VM and the result
        // renders — every rule behind the result lives in the VM tests.

        [Test]
        public void Apply_FromFreshState_RevealsTheStatusCard()
        {
            var configProvider = StubConfigProvider.WithNoSavedConfig();
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);

            GetTextBox(window, "UrlBox").Text = "https://inventree.example.com";
            GetPasswordBox(window, "ApiKeyBox").Password = "inv-new";

            Click(window, "ApplyButton");

            Assert.Multiple(() =>
            {
                Assert.That(GetElement(window, "ConnectionCard").Visibility,
                            Is.EqualTo(Visibility.Visible));
                Assert.That(GetText(window, "ConnectionCardTitle"), Is.EqualTo("Connected"));
                Assert.That(GetDot(window).Fill,
                            Is.SameAs(GetBrush(window, "BrushStatusSuccess")),
                            "the indicator→brush switch is window logic — this is its proof");
                Assert.That(GetElement(window, "CredentialFieldsPanel").Visibility,
                            Is.EqualTo(Visibility.Collapsed),
                            "a complete config collapses the form back to the card");
                Assert.That(GetElement(window, "ApiKeyDotsPlaceholder").Visibility,
                            Is.EqualTo(Visibility.Visible),
                            "the freshly saved key shows as dots");
            });
        }

        [Test]
        public void Test_WhenProbeInFlight_ReportsTestingInConnectionStatus()
        {
            var pending = new TaskCompletionSource<ConnectionProbeResult>();
            var applyService = new StubSettingsApplyService { PendingTestResult = pending };
            var window = CreateWindow(
                applyService: applyService,
                configProvider: StubConfigProvider.WithNoSavedConfig());

            GetTextBox(window, "UrlBox").Text = "https://inventree.example.com";
            GetPasswordBox(window, "ApiKeyBox").Password = "stub-key";

            Click(window, "TestConnectionButton");

            Assert.That(GetStatusBarText(window, "ConnectionStatusBar"),
                        Is.EqualTo("Testing connection…"));

            pending.SetCanceled();
        }

        // ── Action row placement ─────────────────────────────────────────────

        [Test]
        public void TestConnectionButton_AndConnectionStatusBar_LiveInConnectionActionRow()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(IsInside(window, "TestConnectionButton", "ConnectionActionRow"),
                            Is.True,
                            "TestConnectionButton must sit inside ConnectionActionRow");
                Assert.That(IsInside(window, "ConnectionStatusBar", "ConnectionActionRow"), Is.True,
                            "the connection status bar must sit inside ConnectionActionRow");
            });
        }

        [Test]
        public void DialogActionRow_HoldsActionStatusBarAndDialogLevelButtons()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(IsInside(window, "ActionStatusBar", "DialogActionRow"), Is.True,
                            "the action status bar belongs in the bottom dialog row");
                Assert.That(IsInside(window, "ApplyButton", "DialogActionRow"), Is.True);
                Assert.That(IsInside(window, "SaveButton", "DialogActionRow"), Is.True);
                Assert.That(IsInside(window, "CancelButton", "DialogActionRow"), Is.True);
            });
        }

        [Test]
        public void TestConnectionButton_IsOutsideCollapsibleCredentialForm()
        {
            var window = CreateWindow();

            Assert.That(IsInside(window, "TestConnectionButton", "CredentialFormScroll"),
                        Is.False,
                        "TestConnectionButton must not sit inside the credential form");
        }

        // ── Save → dialog result ─────────────────────────────────────────────

        [Test]
        public void Save_Click_WhenApplyFails_StaysOpenAndShowsErrorStatus()
        {
            var applyService = new StubSettingsApplyService
            {
                ExceptionToThrowOnApply = new SettingsApplyException("Failed to save server settings: boom"),
            };
            var window = CreateWindow(applyService: applyService);

            Click(window, "SaveButton");

            Assert.Multiple(() =>
            {
                Assert.That(window.DialogResult, Is.Not.True,
                            "a failed Apply must not close the dialog as success");
                Assert.That(GetStatusBarText(window, "ActionStatusBar"),
                            Is.EqualTo("Failed to save server settings: boom"));
            });
        }

        [Test]
        public void Save_Click_WhenApplySucceeds_ClosesDialogWithTrueResult()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();
            SolidWorksWindowHandle.Set(form.Handle);

            try
            {
                var window = CreateWindow();
                Exception? assertFailure = null;

                window.ContentRendered += (s, e) =>
                {
                    var timer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = TimeSpan.FromMilliseconds(300),
                    };

                    timer.Tick += (s2, e2) =>
                    {
                        timer.Stop();

                        try
                        {
                            var helper = new WindowInteropHelper(window);
                            var dialogRect = HiddenTestWindow.GetRect(helper.Handle);

                            Assert.That(
                                HiddenTestWindow.IsOnScreen(dialogRect),
                                Is.False, "Test dialog must stay off every monitor");
                        }
                        catch (Exception ex)
                        {
                            assertFailure = ex;
                        }

                        Click(window, "SaveButton");
                    };

                    timer.Start();
                };

                var result = window.ShowDialog();

                if (assertFailure != null)
                    Assert.Fail($"Off-screen assertion failed: {assertFailure.Message}");

                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(true));
                    Assert.That(window.DialogResult, Is.EqualTo(true));
                    Assert.That(GetStatusBarText(window, "ActionStatusBar"), Does.Contain("Saved"));
                });
            }
            finally
            {
                SolidWorksWindowHandle.Set(IntPtr.Zero);
            }
        }

        // ── Remove API key → render ──────────────────────────────────────────

        [Test]
        public void RemoveApiKeyButton_IsInsideCardToolbar()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            Assert.That(IsInside(window, "RemoveApiKeyButton", "ConnectionCardToolbar"), Is.True);
        }

        // The only click→render proof of the RemoveApiKey forward: the click
        // reaches _vm.RemoveApiKeyAsync and the outcome renders — the field
        // clears, message text, and card landing are VM rules tested there.
        // The confirm seam is stubbed yes; its real dialog is proven below.
        [Test]
        public void RemoveApiKey_WhenClicked_ForwardsToTheViewModelAndRenders()
        {
            var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
            var applyService = new StubSettingsApplyService(configProvider);
            var window = CreateWindow(applyService: applyService, configProvider: configProvider);
            window.ViewModel.ConfirmRemoveApiKey = () => true;

            Click(window, "RemoveApiKeyButton");

            Assert.Multiple(() =>
            {
                Assert.That(applyService.RemoveCallCount, Is.EqualTo(1),
                            "the click must reach _vm.RemoveApiKeyAsync");
                Assert.That(GetStatusBarText(window, "ConnectionStatusBar"), Is.Not.Empty,
                            "the command's outcome renders on the connection status bar");
            });
        }

        // The wired confirm is a real MessageDialog owned by the Settings
        // window itself — not the SolidWorks main window. The click blocks on
        // the modal ShowDialog, so a timer inside the nested dispatcher loop
        // finds the dialog by its native source, proves it centers on the
        // Settings window's rect and stays off every monitor, then closes it
        // — a decline, so removal must never run.
        [Test, Timeout(15000)]
        public void RemoveApiKey_Click_ConfirmDialogCentersOnTheSettingsWindow()
        {
            using var form = HiddenTestWindow.CreateOwnerForm();
            form.Show();
            SolidWorksWindowHandle.Set(form.Handle);

            SettingsWindow? window = null;
            try
            {
                var configProvider = new StubConfigProvider("https://inventree.example.com", "saved-key");
                var applyService = new StubSettingsApplyService(configProvider);
                window = CreateWindow(applyService: applyService, configProvider: configProvider);
                window.Show();
                var windowHandle = new WindowInteropHelper(window).Handle;

                // The dialog's Window does not exist until the click creates it, so
                // a short find-poll discovers it from the native sources and hands
                // off to the shared centering helper — which owns the 5px tolerance,
                // off-screen gate, decline-close, and the never-centered failure.
                // The ~4s find deadline covers only "dialog never appeared" so
                // find + the helper's 8s stay inside [Timeout(15000)].
                MessageDialog? found = null;
                Task? wait = null;
                string? failure = null;
                var deadline = DateTime.UtcNow.AddSeconds(4);
                var timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(50),
                };
                timer.Tick += (s, e) =>
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        timer.Stop();
                        failure = "no confirmation dialog appeared after clicking Remove API key";
                        return;
                    }

                    try
                    {
                        var dialog = PresentationSource.CurrentSources
                            .OfType<HwndSource>()
                            .Select(source => source.RootVisual)
                            .OfType<MessageDialog>()
                            .FirstOrDefault();
                        if (dialog == null) return;

                        found = dialog;
                        timer.Stop();
                        wait = HiddenTestWindow.WaitForCenteredOnOwnerAsync(found, windowHandle);
                    }
                    catch
                    {
                        // the source list can mutate mid-enumeration — retry next tick
                    }
                };
                timer.Start();

                Click(window, "RemoveApiKeyButton");
                timer.Stop();

                if (failure != null) Assert.Fail(failure);
                Assert.That(found, Is.Not.Null,
                            "clicking Remove API key must open the confirmation dialog");

                // The helper faults with the last-observed rects if the dialog
                // never centered or stayed on a monitor before its deadline.
                wait!.GetAwaiter().GetResult();

                var dialogOwner = new WindowInteropHelper(found!).Owner;

                Assert.Multiple(() =>
                {
                    Assert.That(dialogOwner, Is.EqualTo(windowHandle),
                                "the dialog must be owned by the Settings window, not the SolidWorks window");
                    Assert.That(applyService.RemoveCallCount, Is.EqualTo(0),
                                "closing the dialog declines — removal must not run");
                    Assert.That(configProvider.GetServerConfig()!.ApiKey, Is.EqualTo("saved-key"),
                                "a decline leaves the saved key untouched");
                });
            }
            finally
            {
                window?.Close();
                SolidWorksWindowHandle.Set(IntPtr.Zero);
            }
        }

        // ── Form structure ───────────────────────────────────────────────────

        [Test]
        public void CredentialForm_IsScrollViewerWithBoundedMaxHeight()
        {
            var window = CreateWindow();
            var scroller = GetCredentialForm(window);

            Assert.Multiple(() =>
            {
                Assert.That(scroller, Is.InstanceOf<ScrollViewer>());
                Assert.That(((ScrollViewer)scroller).MaxHeight,
                            Is.LessThan(double.PositiveInfinity));
            });
        }

        // Design v2: the scroll region must fit the fresh state — both
        // username/password and API key blocks — without scrolling.
        [Test]
        public void CredentialForm_MaxHeightFitsTheFreshStateForm()
        {
            var window = CreateWindow();
            var scroller = (ScrollViewer)GetCredentialForm(window);

            scroller.Measure(new Size(416, double.PositiveInfinity));
            double formHeight = scroller.DesiredSize.Height;

            Assert.That(scroller.MaxHeight, Is.GreaterThanOrEqualTo(formHeight));
        }

        [Test]
        public void CredentialForm_ContainsServerUrlField()
        {
            var window = CreateWindow();

            Assert.That(IsInside(window, "UrlBox", "CredentialFormScroll"), Is.True,
                        "server URL must be inside CredentialFormScroll");
        }

        // The "or" divider scopes username+password OR API key only — the
        // Server URL is always required and not part of the choice. A labelled
        // Credential group wraps the credential fields so the divider cannot
        // read as "URL+login vs key".
        [Test]
        public void CredentialGroup_GroupsCredentialFieldsUnderALabel_ExcludingServerUrl()
        {
            var window = CreateWindow(configProvider: StubConfigProvider.WithNoSavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(GetText(window, "CredentialGroupLabel"),
                            Is.EqualTo("Credential"));
                Assert.That(IsInside(window, "UsernameBox", "CredentialGroup"), Is.True);
                Assert.That(IsInside(window, "PasswordBox", "CredentialGroup"), Is.True);
                Assert.That(IsInside(window, "CredentialOrDivider", "CredentialGroup"), Is.True);
                Assert.That(IsInside(window, "ApiKeyBox", "CredentialGroup"), Is.True);
                Assert.That(IsInside(window, "UrlBox", "CredentialGroup"), Is.False,
                            "the Server URL is not part of the credential choice");
            });
        }


        // ── Section titles (#327) ────────────────────────────────────────────
        // The three section headers carry the wording decided during QA of the
        // rename; the audit's mixed-casing shipped strings and the terse
        // canonical forms must be gone.

        [Test]
        public void SectionTitles_UseTheDecidedWording()
        {
            var window = CreateWindow();

            var texts = AllTextBlockTexts(window).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(texts, Does.Contain("InvenTree Server Connection"));
                Assert.That(texts, Does.Contain("Property & Table Mappings"));
                Assert.That(texts, Does.Contain("Keyword in the SOLIDWORKS BOM Table Name"));
                Assert.That(texts, Does.Not.Contain("Inventree Server Connection"));
                Assert.That(texts, Does.Not.Contain("Inventree to Solidworks Mappings"));
                Assert.That(texts, Does.Not.Contain("SolidWorks BOM Table Name Keyword"));
                Assert.That(texts, Does.Not.Contain("Server Connection"));
                Assert.That(texts, Does.Not.Contain("Property Mappings"));
                Assert.That(texts, Does.Not.Contain("BOM Keyword"));
            });
        }

        // ── Field, label & spacing polish (#329) ─────────────────────────────

        // SameAs, not EqualTo: a FontFamily literal would equal the token by
        // value but be a different instance — only a resource reference
        // carries the dictionary's own object. FontSize is a double, so the
        // value check cannot discriminate a literal "12" — ReadLocalValue
        // exposes the deferred reference (a ResourceReferenceExpression;
        // internal on net48, asserted via its public Expression base) a
        // DynamicResource leaves behind. (The root element must use
        // DynamicResource: StaticResource cannot see Window.Resources.)
        [Test]
        public void Window_ChromeFonts_ComeFromDesignTokens()
        {
            var window = CreateWindow();

            Assert.Multiple(() =>
            {
                Assert.That(window.FontFamily,
                            Is.SameAs(window.TryFindResource("FontBody")));
                Assert.That(window.ReadLocalValue(Window.FontSizeProperty),
                            Is.InstanceOf<Expression>(),
                            "a literal FontSize would pass the value check — only the resource reference proves the token");
                Assert.That(window.FontSize,
                            Is.EqualTo((double)window.TryFindResource("FontSizeBody")!));
            });
        }

        // The read-only path display is a value field: the documented
        // ValueFieldStyle carries read-only, the echo tooltip, and the
        // surface fill — a local Background or ToolTip would outrank the
        // style's own treatment.
        [Test]
        public void LocalPathBox_WearsTheReadOnlyValueFieldStyle()
        {
            var window = CreateWindow();

            var style = window.TryFindResource("ValueFieldStyle") as Style;
            Assert.That(style, Is.Not.Null,
                        "DesignTokens.xaml must define ValueFieldStyle");

            var box = GetTextBox(window, "LocalPathBox");
            Assert.Multiple(() =>
            {
                Assert.That(box.Style, Is.SameAs(style));
                Assert.That(box.IsReadOnly, Is.True);
                Assert.That(box.ReadLocalValue(Control.BackgroundProperty),
                            Is.EqualTo(DependencyProperty.UnsetValue),
                            "a local fill outranks the style's own surface treatment");
                Assert.That(box.ReadLocalValue(FrameworkElement.ToolTipProperty),
                            Is.EqualTo(DependencyProperty.UnsetValue),
                            "a local ToolTip outranks the style's empty-text suppression trigger");
                Assert.That(box.Focusable, Is.True,
                            "ValueFieldStyle parks non-focusable for display-only fields; " +
                            "this path is copied by hand, so the element re-enables focus");
            });
        }

        // Each label lives inside its radio's Content so clicking the text
        // toggles the radio; siblings wear the same foreground brush.
        [Test]
        public void MappingRadios_CarryMatchingLabelsAsContent()
        {
            var window = CreateWindow();

            var foreground = GetBrush(window, "BrushForeground");
            var localLabel = GetRadioContentLabel(window, "LocalRadio");
            var sharedLabel = GetRadioContentLabel(window, "SharedRadio");

            Assert.Multiple(() =>
            {
                Assert.That(localLabel.Text, Is.EqualTo("Local mapping file"));
                Assert.That(sharedLabel.Text, Is.EqualTo("Shared mapping file (optional)"));
                Assert.That(localLabel.Foreground, Is.SameAs(foreground));
                Assert.That(sharedLabel.Foreground, Is.SameAs(foreground));
            });
        }

        [Test]
        public void BomKeywordField_HasAFieldLabelAndStandardHeight()
        {
            var window = CreateWindow();

            var label = AllTextBlocks(window).FirstOrDefault(
                t => t.Text == "Keyword" &&
                     ReferenceEquals(t.Style, window.TryFindResource("FieldLabelStyle")));
            Assert.That(label, Is.Not.Null,
                        "the BOM keyword field carries a FieldLabelStyle label like every other field");
            Assert.That(GetTextBox(window, "BomKeywordBox").Height, Is.EqualTo(28));
        }

        [Test]
        public void ApiKeyDotsPlaceholder_DotsAreLargerAndMoreNumerous()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var dots = GetElement(window, "ApiKeyDotsPlaceholder") as TextBlock;
            Assert.That(dots, Is.Not.Null);

            double bodySize = (double)window.TryFindResource("FontSizeBody")!;
            var subtle = GetBrush(window, "BrushSubtle");
            Assert.Multiple(() =>
            {
                Assert.That(dots!.FontSize, Is.GreaterThan(bodySize),
                            "saved-key dots read larger than body text (QA on #263)");
                Assert.That(dots.Text.Length, Is.GreaterThanOrEqualTo(24),
                            "the dot run must reach across the field, not stop at twelve");
                Assert.That(dots.Foreground, Is.SameAs(subtle),
                            "the placeholder keeps the subtle brush (item 5 preserve conjunct)");
                Assert.That(dots.IsHitTestVisible, Is.False,
                            "the overlay must let clicks reach the PasswordBox underneath");
            });
        }

        // Item 6's directive covers the "Server URL" label too: it takes the
        // FieldLabelStyle default margin like its sibling labels — the window's
        // last ad-hoc label margin is gone.
        [Test]
        public void ServerUrlLabel_TakesTheFieldLabelStyleDefaultMargin()
        {
            var window = CreateWindow();

            var label = AllTextBlocks(window).FirstOrDefault(
                t => t.Text == "Server URL" &&
                     ReferenceEquals(t.Style, window.TryFindResource("FieldLabelStyle")));
            Assert.That(label, Is.Not.Null,
                        "the Server URL field carries a FieldLabelStyle label like every other field");
            Assert.That(label!.ReadLocalValue(FrameworkElement.MarginProperty),
                        Is.EqualTo(DependencyProperty.UnsetValue),
                        "the label takes the style default 0,6,0,2 — no ad-hoc label margin");
        }

        [Test]
        public void CardToolbar_RemoveApiKeyButton_WearsTheCardToolbarDangerStyle()
        {
            var window = CreateWindow(
                configProvider: new StubConfigProvider("https://inventree.example.com", "saved-key"));

            var style = window.TryFindResource("CardToolbarDangerButtonStyle") as Style;
            Assert.That(style, Is.Not.Null,
                        "DesignTokens.xaml must define CardToolbarDangerButtonStyle");

            var button = GetButton(window, "RemoveApiKeyButton");
            Assert.Multiple(() =>
            {
                Assert.That(button.Style, Is.SameAs(style),
                            "the destructive action takes the card-toolbar danger style");
                Assert.That(button.ReadLocalValue(FrameworkElement.HeightProperty),
                            Is.EqualTo(DependencyProperty.UnsetValue),
                            "the 24px card-toolbar chrome lives in the style, not locals");
                Assert.That(button.ReadLocalValue(FrameworkElement.MarginProperty),
                            Is.EqualTo(DependencyProperty.UnsetValue));
            });
        }

        // ── Section text styles (#328) ───────────────────────────────────────
        // One shared title treatment and one shared intro treatment per
        // section, defined once in DesignTokens.xaml — the audit found the
        // same help/intro role rendered in three different styles (#252
        // findings 7–8).

        [TestCase("InvenTree Server Connection")]
        [TestCase("Property & Table Mappings")]
        [TestCase("Keyword in the SOLIDWORKS BOM Table Name")]
        public void SectionStyles_Title_WearsSharedStyle(string titleText)
        {
            var window = CreateWindow();
            var titleStyle = GetStyle(window, "SectionTitleStyle");
            var title = FindTextBlock(window, titleText);

            Assert.Multiple(() =>
            {
                Assert.That(title.Style, Is.SameAs(titleStyle),
                            "the section title must wear SectionTitleStyle");
                Assert.That(title.FontWeight, Is.EqualTo(FontWeights.SemiBold));
                Assert.That(title.Foreground,
                            Is.SameAs(GetBrush(window, "BrushForeground")));
            });
        }

        [TestCase("InvenTree Server Connection")]
        [TestCase("Property & Table Mappings")]
        [TestCase("Keyword in the SOLIDWORKS BOM Table Name")]
        public void SectionStyles_Intro_FollowsTitleWearingSharedStyle(string titleText)
        {
            var window = CreateWindow();
            var introStyle = GetStyle(window, "SectionIntroStyle");
            var intro = NextTextBlockSibling(FindTextBlock(window, titleText));

            Assert.That(intro, Is.Not.Null, $"no intro line under '{titleText}'");
            Assert.Multiple(() =>
            {
                Assert.That(intro!.Style, Is.SameAs(introStyle),
                            "the section intro must wear SectionIntroStyle");
                Assert.That(intro!.Text, Is.Not.Empty);
            });
        }

        // The Server Connection intro is new with #328 — the other two
        // sections already had one; the mock's line is the wording target.
        [Test]
        public void SectionStyles_ServerConnectionIntro_ReadsLikeTheMock()
        {
            var window = CreateWindow();
            var intro = NextTextBlockSibling(
                FindTextBlock(window, "InvenTree Server Connection"));

            Assert.That(intro?.Text,
                        Is.EqualTo("InvenTree server address and credentials."));
        }

        [TestCase("Used once to request an API token")]
        [TestCase("If both are filled, the API key is used.")]
        public void SectionStyles_CredentialHelp_WearsSharedIntroStyle(string prefix)
        {
            var window = CreateWindow();
            var introStyle = GetStyle(window, "SectionIntroStyle");
            var help = AllTextBlocks(window).FirstOrDefault(b => b.Text.StartsWith(prefix));

            Assert.That(help, Is.Not.Null, $"no help line starting '{prefix}'");
            Assert.Multiple(() =>
            {
                Assert.That(help!.Style, Is.SameAs(introStyle), prefix);
                Assert.That(help!.Opacity, Is.EqualTo(1.0), prefix);
            });
        }

        // FieldLabelStyle is a field-label style — paragraph intros and help
        // text are not labels.
        [Test]
        public void SectionStyles_FieldLabelStyle_OnlyOnFieldLabels()
        {
            var window = CreateWindow();
            var labelStyle = GetStyle(window, "FieldLabelStyle");

            var labelled = AllTextBlocks(window)
                .Where(b => ReferenceEquals(b.Style, labelStyle))
                .Select(b => b.Text)
                .ToList();

            Assert.That(labelled, Is.EquivalentTo(
                new[] { "Server URL", "Username", "Password", "API Key", "Keyword" }));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static IEnumerable<TextBlock> AllTextBlocks(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var node = child as DependencyObject;
                if (node == null) continue;
                if (node is TextBlock textBlock)
                    yield return textBlock;
                foreach (var descendant in AllTextBlocks(node))
                    yield return descendant;
            }
        }

        private static TextBlock GetRadioContentLabel(Window window, string radioName)
        {
            var radio = LogicalTreeHelper.FindLogicalNode(window, radioName) as RadioButton;
            Assert.That(radio, Is.Not.Null, $"Could not find RadioButton named '{radioName}'.");
            var label = radio!.Content as TextBlock;
            Assert.That(label, Is.Not.Null,
                        $"{radioName} must carry its label as Content so clicking the text toggles it");
            return label!;
        }

        private static IEnumerable<string> AllTextBlockTexts(DependencyObject root) =>
            AllTextBlocks(root).Select(b => b.Text);

        private static TextBlock FindTextBlock(Window window, string text)
        {
            var block = AllTextBlocks(window).FirstOrDefault(b => b.Text == text);
            Assert.That(block, Is.Not.Null, $"no TextBlock with text '{text}'");
            return block!;
        }

        private static TextBlock? NextTextBlockSibling(FrameworkElement element)
        {
            if (!(LogicalTreeHelper.GetParent(element) is Panel parent))
                return null;
            var children = parent.Children;
            for (int i = children.IndexOf(element) + 1; i < children.Count; i++)
                if (children[i] is TextBlock textBlock)
                    return textBlock;
            return null;
        }

        private static Style GetStyle(Window window, string key)
        {
            var style = window.TryFindResource(key) as Style;
            Assert.That(style, Is.Not.Null, $"DesignTokens.xaml must define {key}");
            return style!;
        }

        private SettingsWindow CreateWindow(
            IPropertyMappingProvider? mappingProvider = null,
            ISettingsApplyService? applyService = null,
            IMappingProviderFactory? mappingProviderFactory = null,
            IConfigProvider? configProvider = null,
            IVersionInfo? versionInfo = null)
        {
            mappingProvider ??= new StubPropertyMappingProvider();
            applyService ??= new StubSettingsApplyService();
            mappingProviderFactory ??= new StubMappingProviderFactory
            {
                Factory = _ => mappingProvider,
            };
            configProvider ??= new StubConfigProvider("https://example.com", "stub-key");
            versionInfo ??= new StubVersionInfo();

            return new SettingsWindow(
                configProvider,
                mappingProvider,
                versionInfo,
                applyService,
                mappingProviderFactory);
        }

        private static void Click(Window window, string name) =>
            GetButton(window, name).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        private static string GetText(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name);
            var textBlock = element as TextBlock;
            var textBox = element as TextBox;
            Assert.That(textBlock ?? (object?)textBox, Is.Not.Null,
                        $"Could not find TextBlock or TextBox named '{name}'.");
            return textBlock?.Text ?? textBox?.Text ?? string.Empty;
        }

        private static FrameworkElement GetElement(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as FrameworkElement;
            Assert.That(element, Is.Not.Null, $"Could not find element named '{name}'.");
            return element!;
        }

        private static Ellipse GetDot(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "ConnectionStatusDot") as Ellipse;
            Assert.That(element, Is.Not.Null, "Could not find Ellipse named 'ConnectionStatusDot'.");
            return element!;
        }

        private static PasswordBox GetPasswordBox(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as PasswordBox;
            Assert.That(element, Is.Not.Null, $"Could not find PasswordBox named '{name}'.");
            return element!;
        }

        private static Button GetButton(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name);
            var button = element as Button;
            Assert.That(button, Is.Not.Null, $"Could not find Button named '{name}'.");
            return button!;
        }

        private static TextBox GetTextBox(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as TextBox;
            Assert.That(element, Is.Not.Null, $"Could not find TextBox named '{name}'.");
            return element!;
        }

        private static StatusBarControl GetStatusBar(Window window, string name)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, name) as StatusBarControl;
            Assert.That(element, Is.Not.Null, $"Could not find StatusBarControl named '{name}'.");
            return element!;
        }

        private static string GetStatusBarText(Window window, string name) =>
            GetStatusBar(window, name).StatusText.Text;

        private static TextBox GetStatusBarTextBox(Window window, string name) =>
            GetStatusBar(window, name).StatusText;

        private static Brush GetBrush(Window window, string key)
        {
            var brush = window.TryFindResource(key) as Brush;
            Assert.That(brush, Is.Not.Null, $"Could not find resource '{key}'.");
            return brush!;
        }

        private static ScrollViewer GetCredentialForm(Window window)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, "CredentialFormScroll") as ScrollViewer;
            Assert.That(element, Is.Not.Null,
                        "Could not find ScrollViewer named 'CredentialFormScroll'.");
            return element!;
        }

        private static bool IsInside(Window window, string elementName, string ancestorName)
        {
            var element = LogicalTreeHelper.FindLogicalNode(window, elementName) as DependencyObject;
            Assert.That(element, Is.Not.Null, $"Could not find element named '{elementName}'.");

            var ancestor = LogicalTreeHelper.FindLogicalNode(window, ancestorName) as DependencyObject;
            Assert.That(ancestor, Is.Not.Null, $"Could not find element named '{ancestorName}'.");

            for (var parent = LogicalTreeHelper.GetParent(element!); parent != null;
                 parent = LogicalTreeHelper.GetParent(parent))
            {
                if (ReferenceEquals(parent, ancestor)) return true;
            }

            return false;
        }

        // The open probe applies its verdict via Dispatcher.Invoke from a pool
        // thread, so a plain await in the test deadlocks — NUnit does not pump
        // the dispatcher while it waits. Pump a DispatcherFrame until the task
        // completes instead.
        private static void WaitForProbe(SettingsWindow window)
        {
            var task = window.OpenProbeTask;
            Assert.That(task, Is.Not.Null, "no open probe was started");

            var frame = new DispatcherFrame();
            task!.ContinueWith(
                _ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
    }
}
