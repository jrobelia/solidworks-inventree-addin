using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiImage = System.Drawing.Image;
using Point = System.Drawing.Point;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// WPF image crop dialog. Geometry logic lives in <see cref="CropGeometry"/>;
    /// this class is purely presentation: render the image, draw overlays, sync state.
    /// </summary>
    public partial class ImageCropWindow : Window
    {
        private readonly GdiImage       _sourceImage;
        private readonly CropGeometry  _geo;

        /// <summary>The crop rectangle chosen by the user (Empty = full image).</summary>
        public Rectangle CropRectangle => _geo.CropRectangle;

        public ImageCropWindow(GdiImage sourceImage)
        {
            _sourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
            _geo         = new CropGeometry(sourceImage.Width, sourceImage.Height);

            InitializeComponent();

            // Set WPF Image source from the GDI+ Image
            SourceImage.Source = ToWpfBitmap(sourceImage);

            // Keep SquareLock in sync with checkbox
            SquareLockCheck.Checked   += (s, e) => _geo.SquareLock = true;
            SquareLockCheck.Unchecked += (s, e) => _geo.SquareLock = false;

            // Restore saved bounds (or centre on screen) before the window appears
            Loaded  += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Restore saved size (width/height only)
            if (CropWindowBounds.TryLoad(out var b))
            {
                Width  = b[2];
                Height = b[3];
            }

            // Always centre on primary screen (SystemParameters is DIP-based, matching WPF Left/Top)
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width  - Width)  / 2.0;
            Top  = workArea.Top  + (workArea.Height - Height) / 2.0;

            // Try to set SolidWorks as owner (cosmetic — keeps dialog in front)
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = SolidWorksWindowHandle.Get();
            }
            catch { /* cosmetic */ }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (WindowState == WindowState.Normal)
                CropWindowBounds.Save(Width, Height);
        }

        // ── Mouse handlers ───────────────────────────────────────────

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            var imgPt = _geo.DisplayToImage(
                new Point((int)pos.X, (int)pos.Y),
                OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);

            _geo.OnMouseDown(imgPt);

            OverlayCanvas.Cursor = _geo.IsMoving ? Cursors.SizeAll : Cursors.Cross;
            OverlayCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            var imgPt = _geo.DisplayToImage(
                new Point((int)pos.X, (int)pos.Y),
                OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);

            // Update cursor when hovering (not dragging)
            if (!_geo.IsDragging && !_geo.IsMoving)
            {
                OverlayCanvas.Cursor = _geo.HitTest(imgPt) ? Cursors.SizeAll : Cursors.Cross;
            }

            if (_geo.OnMouseMove(imgPt))
                UpdateOverlay();
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            var imgPt = _geo.DisplayToImage(
                new Point((int)pos.X, (int)pos.Y),
                OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);

            _geo.OnMouseUp(imgPt);
            OverlayCanvas.ReleaseMouseCapture();

            UpdateOverlay();
            UpdatePreview();
        }

        // ── Overlay rendering ────────────────────────────────────────

        private void UpdateOverlay()
        {
            var rect = _geo.GetActiveRect();
            if (rect.IsEmpty || rect.Width <= 5 || rect.Height <= 5)
            {
                DimOverlay.Visibility = CropBorder.Visibility = Visibility.Collapsed;
                return;
            }

            double cw = OverlayCanvas.ActualWidth;
            double ch = OverlayCanvas.ActualHeight;

            var tl = _geo.ImageToDisplay(rect.Location, cw, ch);
            var br = _geo.ImageToDisplay(new Point(rect.Right, rect.Bottom), cw, ch);

            double x1 = tl.X, y1 = tl.Y;
            double x2 = br.X, y2 = br.Y;

            // Single path: full canvas minus the crop hole — no seams, no overlaps
            DimOverlay.Data = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, cw, ch)),
                new RectangleGeometry(new Rect(x1, y1, x2 - x1, y2 - y1)));
            DimOverlay.Visibility = Visibility.Visible;

            // Crop border
            Canvas.SetLeft(CropBorder, x1);
            Canvas.SetTop(CropBorder, y1);
            CropBorder.Width  = Math.Max(0, x2 - x1);
            CropBorder.Height = Math.Max(0, y2 - y1);
            CropBorder.Visibility = Visibility.Visible;
        }

        private void UpdatePreview()
        {
            var rect = _geo.CropRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                PreviewImage.Source = null;
                return;
            }

            var preview = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(preview))
            {
                g.DrawImage(_sourceImage,
                    new Rectangle(0, 0, rect.Width, rect.Height),
                    rect, GraphicsUnit.Pixel);
            }
            PreviewImage.Source = ToWpfBitmap(preview);
            preview.Dispose();
        }

        // ── Button handlers ──────────────────────────────────────────

        private void Confirm_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        // ── Helpers ───────────────────────────────────────────────

        /// <summary>Convert a GDI+ Image to a WPF BitmapSource.</summary>
        private static BitmapSource ToWpfBitmap(GdiImage image)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption  = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
    }

    /// <summary>
    /// Persists the crop window's Left/Top/Width/Height to a plain text file
    /// so the position and size are remembered between sessions.
    /// </summary>
    internal static class CropWindowBounds
    {
        private static readonly string FilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SwInventreeAddin", "crop_window_bounds.txt");

        /// <summary>Save window size. Silently swallows IO errors.</summary>
        public static void Save(double width, double height)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath,
                    string.Format(CultureInfo.InvariantCulture, "{0},{1}", width, height));
            }
            catch { /* non-critical */ }
        }

        /// <summary>
        /// Try to load saved size. Returns true and populates
        /// <paramref name="bounds"/> as [left(0), top(0), width, height].
        /// </summary>
        public static bool TryLoad(out double[] bounds)
        {
            bounds = Array.Empty<double>();
            try
            {
                if (!File.Exists(FilePath)) return false;
                var parts = File.ReadAllText(FilePath).Split(',');
                if (parts.Length < 2) return false;
                if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double w)) return false;
                if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double h)) return false;
                if (w < 100 || h < 100) return false;
                bounds = new[] { 0.0, 0.0, w, h };
                return true;
            }
            catch { return false; }
        }
    }
}
