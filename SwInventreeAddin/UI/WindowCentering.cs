using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Centers a WPF <see cref="Window"/> over an arbitrary Win32 owner window using
    /// native screen coordinates. This works around WPF <c>CenterOwner</c> not correctly
    /// centering on a non-WPF, maximised, multi-monitor / high-DPI owner.
    /// </summary>
    internal static class WindowCentering
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const uint SwpNoSize     = 0x0001;
        private const uint SwpNoZOrder   = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        // Number of consecutive stable native-rectangle polls before the window is revealed.
        private const int StableTickThreshold = 3;
        private const int PollingIntervalMs   = 50;

        // Far off-screen so no frame is visible before the centered position is known.
        private const int HiddenLeft = -32000;
        private const int HiddenTop  = -32000;

        // ── Native interop ────────────────────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint flags);

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // ── Assembly test seam ────────────────────────────────────────────────

        /// <summary>
        /// Calculates the top-left screen coordinates that center a dialog over its owner.
        /// The dialog's own origin is ignored; only its width and height matter.
        /// Exposed so the unit tests can verify the pure math without creating WPF windows.
        /// </summary>
        internal static (int left, int top) CalculateCenteredPosition(NativeRect ownerRect, NativeRect dialogRect)
        {
            int ownerWidth   = ownerRect.Right  - ownerRect.Left;
            int ownerHeight  = ownerRect.Bottom - ownerRect.Top;
            int dialogWidth  = dialogRect.Right - dialogRect.Left;
            int dialogHeight = dialogRect.Bottom - dialogRect.Top;

            int left = ownerRect.Left + (ownerWidth  - dialogWidth)  / 2;
            int top  = ownerRect.Top  + (ownerHeight - dialogHeight) / 2;

            return (left, top);
        }

        // ── Public (within-assembly) interface ────────────────────────────────

        /// <summary>
        /// Sets the Win32 owner of <paramref name="window"/> to <paramref name="ownerHandle"/>
        /// and centers the window on that owner once its native size and position are stable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The window starts at <c>Opacity="0"</c>, is moved far off-screen at
        /// <see cref="Window.SourceInitialized"/>, then centered in
        /// <see cref="Window.ContentRendered"/>.
        /// </para>
        /// <para>
        /// Resizable and <c>SizeToContent</c> dialogs may still be adjusting their native
        /// rectangle after <c>ContentRendered</c> (for example, when DWM applies the resizable
        /// border or when async content changes the window size). A low-priority
        /// <see cref="DispatcherTimer"/> polls <c>GetWindowRect</c> until the rectangle is
        /// stable and re-centers on any change. The window is revealed only when the final
        /// position has been set.
        /// </para>
        /// </remarks>
        public static void Attach(Window window, IntPtr ownerHandle)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            try
            {
                var helper = new WindowInteropHelper(window);
                if (ownerHandle != IntPtr.Zero)
                    helper.Owner = ownerHandle;
            }
            catch { /* cosmetic only */ }

            window.SourceInitialized += (s, e) => OnSourceInitialized(window, ownerHandle);
            window.ContentRendered   += (s, e) => OnContentRendered(window, ownerHandle);
        }

        // ── Private implementation ────────────────────────────────────────────

        private static void OnSourceInitialized(Window window, IntPtr ownerHandle)
        {
            try
            {
                var dialogHandle = new WindowInteropHelper(window).Handle;
                if (ownerHandle == IntPtr.Zero || dialogHandle == IntPtr.Zero)
                    return;

                // Move the transparent window off-screen before it has a chance to paint
                // a frame at the default location.
                _ = SetWindowPos(
                    dialogHandle,
                    IntPtr.Zero,
                    HiddenLeft,
                    HiddenTop,
                    0,
                    0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }
            catch { /* cosmetic only */ }
        }

        private static void OnContentRendered(Window window, IntPtr ownerHandle)
        {
            // First centering pass while still transparent. Resizable / SizeToContent
            // windows may still change size after this, so the reveal is deferred until
            // the native rectangle has stabilized.
            Center(window, ownerHandle);

            _ = new CenteringState(window, ownerHandle);
        }

        private static void Center(Window window, IntPtr ownerHandle)
        {
            try
            {
                var dialogHandle = new WindowInteropHelper(window).Handle;
                if (ownerHandle == IntPtr.Zero || dialogHandle == IntPtr.Zero)
                    return;

                // Force a layout pass so SizeToContent windows reach their final size
                // before we read the native window rectangle.
                window.UpdateLayout();

                if (!GetWindowRect(ownerHandle, out var ownerRect) ||
                    !GetWindowRect(dialogHandle, out var dialogRect))
                    return;

                var (left, top) = CalculateCenteredPosition(ownerRect, dialogRect);

                _ = SetWindowPos(
                    dialogHandle,
                    IntPtr.Zero,
                    left,
                    top,
                    0,
                    0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }
            catch { /* cosmetic only */ }
        }

        // ── Stabilization helper ──────────────────────────────────────────────

        /// <summary>
        /// Tracks the native rectangle of a dialog until it is stable, re-centers on
        /// <see cref="Window.SizeChanged"/> or native-rect changes, and reveals the window
        /// only when the final position has been applied.
        /// </summary>
        private sealed class CenteringState
        {
            private readonly Window _window;
            private readonly IntPtr _ownerHandle;
            private readonly DispatcherTimer _timer;
            private readonly SizeChangedEventHandler _sizeChangedHandler;
            private readonly EventHandler _closedHandler;

            private NativeRect? _lastDialogRect;
            private int _stableTickCount;
            private bool _isRevealed;
            private bool _isClosed;

            public CenteringState(Window window, IntPtr ownerHandle)
            {
                _window = window;
                _ownerHandle = ownerHandle;

                _sizeChangedHandler = OnSizeChanged;
                _closedHandler = OnClosed;

                _window.SizeChanged += _sizeChangedHandler;
                _window.Closed += _closedHandler;

                _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
                {
                    Interval = TimeSpan.FromMilliseconds(PollingIntervalMs),
                };
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }

            private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            {
                if (_isRevealed || _isClosed)
                    return;

                // The window size is still changing; keep it hidden and reset the
                // stability count so the timer does not reveal too early.
                _stableTickCount = 0;
                _lastDialogRect = null;
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                _isClosed = true;
                StopAndCleanup();
            }

            private void OnTimerTick(object? sender, EventArgs e)
            {
                if (_isRevealed || _isClosed)
                {
                    StopAndCleanup();
                    return;
                }

                // Re-center before reading the rectangle, so a SizeToContent / resizable
                // window that has grown is moved to the correct centered position.
                Center(_window, _ownerHandle);

                if (!TryGetDialogRect(out var dialogRect))
                {
                    StopAndCleanup();
                    return;
                }

                if (_lastDialogRect.HasValue && !RectsEqual(_lastDialogRect.Value, dialogRect))
                {
                    _stableTickCount = 0;
                    _lastDialogRect = dialogRect;
                    return;
                }

                _lastDialogRect = dialogRect;
                _stableTickCount++;

                if (_stableTickCount < StableTickThreshold)
                    return;

                // The native rectangle has stayed the same for several consecutive ticks.
                // Center one last time and reveal the window.
                Center(_window, _ownerHandle);
                _window.Opacity = 1.0;
                _isRevealed = true;
                StopAndCleanup();
            }

            private bool TryGetDialogRect(out NativeRect dialogRect)
            {
                dialogRect = default;
                try
                {
                    var handle = new WindowInteropHelper(_window).Handle;
                    if (handle == IntPtr.Zero)
                        return false;

                    return GetWindowRect(handle, out dialogRect);
                }
                catch
                {
                    return false;
                }
            }

            private void StopAndCleanup()
            {
                _timer?.Stop();

                if (_window != null)
                {
                    _window.SizeChanged -= _sizeChangedHandler;
                    _window.Closed -= _closedHandler;
                }
            }

            private static bool RectsEqual(NativeRect a, NativeRect b)
            {
                return a.Left == b.Left && a.Top == b.Top
                    && a.Right == b.Right && a.Bottom == b.Bottom;
            }
        }
    }
}
