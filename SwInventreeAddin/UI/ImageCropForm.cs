using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Modal dialog that shows a captured image and lets the user drag a crop
    /// rectangle before confirming. Returns <see cref="DialogResult.OK"/> with
    /// <see cref="CropRectangle"/> set, or <see cref="DialogResult.Cancel"/>.
    /// If the user confirms without dragging, <see cref="CropRectangle"/> is
    /// <see cref="Rectangle.Empty"/> (meaning "use the full image").
    /// </summary>
    public class ImageCropForm : Form
    {
        /// <summary>The crop rectangle chosen by the user (Empty = full image).</summary>
        public Rectangle CropRectangle { get; private set; } = Rectangle.Empty;

        /// <summary>Exposed for tests — square-lock checkbox state.</summary>
        public CheckBox SquareLockCheckBox { get; private set; } = null!;

        private readonly Image  _sourceImage;
        private readonly PictureBox _pictureBox;
        private readonly PictureBox _previewBox;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        // Draw state
        private Point _dragStart;
        private Point _dragEnd;
        private bool  _isDragging;
        private bool  _hasCrop;

        // Move state
        private bool  _isMoving;
        private Point _moveOrigin;
        private Rectangle _rectAtMoveStart;

        public ImageCropForm(Image sourceImage)
        {
            _sourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));

            Text            = "Crop Image for InvenTree";
            StartPosition   = FormStartPosition.CenterParent;
            Size            = new Size(900, 700);
            MinimumSize     = new Size(600, 400);
            FormBorderStyle = FormBorderStyle.Sizable;

            // Load InvenTree icon from Resources folder next to the DLL
            try
            {
                var dir      = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                var iconPath = Path.Combine(dir, "Resources", "inventree_icon.png");
                if (File.Exists(iconPath))
                {
                    using var bmp = new Bitmap(iconPath);
                    Icon = Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch { /* icon is cosmetic — never crash */ }

            var splitContainer = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = Orientation.Vertical,
                SplitterDistance = 600,
            };

            // Left: source image with drag-to-crop
            _pictureBox = new PictureBox
            {
                Dock     = DockStyle.Fill,
                Image    = _sourceImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor   = Cursors.Cross,
            };
            _pictureBox.MouseDown += OnMouseDown;
            _pictureBox.MouseMove += OnMouseMove;
            _pictureBox.MouseUp   += OnMouseUp;
            _pictureBox.Paint     += OnPaint;

            splitContainer.Panel1.Controls.Add(_pictureBox);

            // Right: preview of cropped area
            _previewBox = new PictureBox
            {
                Dock      = DockStyle.Fill,
                SizeMode  = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(240, 240, 240),
            };
            var previewLabel = new Label
            {
                Text      = "Preview",
                Dock      = DockStyle.Top,
                Height    = 24,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            splitContainer.Panel2.Controls.Add(_previewBox);
            splitContainer.Panel2.Controls.Add(previewLabel);

            // Bottom button bar
            var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 46 };

            _confirmButton = new Button
            {
                Text      = "Confirm",
                Width     = 100,
                Height    = 32,
                Anchor    = AnchorStyles.Bottom | AnchorStyles.Right,
                BackColor = Color.FromArgb(0, 130, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            };
            _confirmButton.FlatAppearance.BorderSize = 0;
            _confirmButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            _cancelButton = new Button
            {
                Text      = "Cancel",
                Width     = 100,
                Height    = 32,
                Anchor    = AnchorStyles.Bottom | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 10f),
            };
            _cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            SquareLockCheckBox = new CheckBox
            {
                Text    = "Square crop",
                Checked = true,
                Left    = 12,
                Top     = 12,
                Width   = 120,
                Font    = new Font("Segoe UI", 9f),
                Anchor  = AnchorStyles.Left | AnchorStyles.Bottom,
            };

            _confirmButton.Location = new Point(buttonPanel.Width - 220, 7);
            _cancelButton.Location  = new Point(buttonPanel.Width - 110, 7);
            buttonPanel.Controls.Add(_confirmButton);
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(SquareLockCheckBox);

            Controls.Add(splitContainer);
            Controls.Add(buttonPanel);
        }

        // ── Coordinate translation ────────────────────────────────────────────

        private Point PictureBoxToImage(Point clientPoint)
        {
            if (_pictureBox.Image == null) return clientPoint;

            float imgW = _pictureBox.Image.Width;
            float imgH = _pictureBox.Image.Height;
            float boxW = _pictureBox.ClientSize.Width;
            float boxH = _pictureBox.ClientSize.Height;

            float scale = Math.Min(boxW / imgW, boxH / imgH);
            float offX  = (boxW - imgW * scale) / 2f;
            float offY  = (boxH - imgH * scale) / 2f;

            int imgX = (int)((clientPoint.X - offX) / scale);
            int imgY = (int)((clientPoint.Y - offY) / scale);

            imgX = Math.Max(0, Math.Min(imgX, (int)imgW - 1));
            imgY = Math.Max(0, Math.Min(imgY, (int)imgH - 1));

            return new Point(imgX, imgY);
        }

        private Point ImageToPictureBox(Point imagePoint)
        {
            if (_pictureBox.Image == null) return imagePoint;

            float imgW = _pictureBox.Image.Width;
            float imgH = _pictureBox.Image.Height;
            float boxW = _pictureBox.ClientSize.Width;
            float boxH = _pictureBox.ClientSize.Height;

            float scale = Math.Min(boxW / imgW, boxH / imgH);
            float offX  = (boxW - imgW * scale) / 2f;
            float offY  = (boxH - imgH * scale) / 2f;

            return new Point(
                (int)(imagePoint.X * scale + offX),
                (int)(imagePoint.Y * scale + offY));
        }

        // ── Mouse handling ────────────────────────────────────────────────────

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            var imgPt = PictureBoxToImage(e.Location);

            if (_hasCrop && CropRectangle.Contains(imgPt))
            {
                // Start moving the existing rectangle
                _isMoving        = true;
                _isDragging      = false;
                _moveOrigin      = imgPt;
                _rectAtMoveStart = CropRectangle;
            }
            else
            {
                // Start drawing a new rectangle
                _isMoving   = false;
                _isDragging = true;
                _hasCrop    = false;
                _dragStart  = imgPt;
                _dragEnd    = imgPt;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var imgPt = PictureBoxToImage(e.Location);

            // Update cursor based on hover position
            if (!_isDragging && !_isMoving)
            {
                _pictureBox.Cursor = (_hasCrop && CropRectangle.Contains(imgPt))
                    ? Cursors.SizeAll
                    : Cursors.Cross;
            }

            if (_isDragging)
            {
                _dragEnd = ApplySquareLock(_dragStart, imgPt);
                _pictureBox.Invalidate();
            }
            else if (_isMoving)
            {
                int dx    = imgPt.X - _moveOrigin.X;
                int dy    = imgPt.Y - _moveOrigin.Y;
                int imgW  = _sourceImage.Width;
                int imgH  = _sourceImage.Height;
                int newX  = Math.Max(0, Math.Min(_rectAtMoveStart.X + dx, imgW - _rectAtMoveStart.Width));
                int newY  = Math.Max(0, Math.Min(_rectAtMoveStart.Y + dy, imgH - _rectAtMoveStart.Height));
                CropRectangle = new Rectangle(newX, newY, _rectAtMoveStart.Width, _rectAtMoveStart.Height);
                _pictureBox.Invalidate();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_isMoving)
            {
                _isMoving = false;
                _pictureBox.Cursor = Cursors.SizeAll;
                UpdatePreview(CropRectangle);
                return;
            }

            if (!_isDragging) return;
            _isDragging = false;

            var imgPt = PictureBoxToImage(e.Location);
            _dragEnd  = ApplySquareLock(_dragStart, imgPt);
            CommitDrag();
        }

        /// <summary>
        /// Applies square-lock: constrains end so the resulting rect is square.
        /// Uses the larger of |ΔX| and |ΔY|, preserving the drag direction.
        /// </summary>
        private Point ApplySquareLock(Point start, Point end)
        {
            if (!SquareLockCheckBox.Checked) return end;

            int dx   = end.X - start.X;
            int dy   = end.Y - start.Y;
            int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Point(
                start.X + (dx >= 0 ? size : -size),
                start.Y + (dy >= 0 ? size : -size));
        }

        private void CommitDrag()
        {
            var rect = MakeRect(_dragStart, _dragEnd);
            if (rect.Width > 5 && rect.Height > 5)
            {
                _hasCrop      = true;
                CropRectangle = rect;
                UpdatePreview(rect);
            }
            else
            {
                _hasCrop      = false;
                CropRectangle = Rectangle.Empty;
                _previewBox.Image?.Dispose();
                _previewBox.Image = null;
            }
            _pictureBox.Invalidate();
        }

        /// <summary>
        /// Test seam: simulates a mouse drag in image coordinates, bypassing
        /// PictureBox coordinate translation. Square lock is applied if checked.
        /// </summary>
        public void SimulateDrag(Point imageStart, Point imageEnd)
        {
            _isDragging = true;
            _isMoving   = false;
            _hasCrop    = false;
            _dragStart  = imageStart;
            _dragEnd    = ApplySquareLock(imageStart, imageEnd);
            CommitDrag();
        }

        // ── Painting ──────────────────────────────────────────────────────────

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (!_isDragging && !_hasCrop && !_isMoving) return;

            Rectangle activeRect;
            if (_isDragging)
                activeRect = MakeRect(_dragStart, _dragEnd);
            else
                activeRect = CropRectangle;

            var topLeft     = ImageToPictureBox(activeRect.Location);
            var bottomRight = ImageToPictureBox(new Point(activeRect.Right, activeRect.Bottom));
            var displayRect = MakeRect(topLeft, bottomRight);

            using (var pen = new Pen(Color.Red, 2f))
                e.Graphics.DrawRectangle(pen, displayRect);

            using (var brush = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
            {
                var c = _pictureBox.ClientRectangle;
                e.Graphics.FillRectangle(brush, 0, 0, c.Width, displayRect.Top);
                e.Graphics.FillRectangle(brush, 0, displayRect.Bottom, c.Width, c.Height - displayRect.Bottom);
                e.Graphics.FillRectangle(brush, 0, displayRect.Top, displayRect.Left, displayRect.Height);
                e.Graphics.FillRectangle(brush, displayRect.Right, displayRect.Top, c.Width - displayRect.Right, displayRect.Height);
            }
        }

        private void UpdatePreview(Rectangle cropRect)
        {
            _previewBox.Image?.Dispose();
            var preview = new Bitmap(cropRect.Width, cropRect.Height);
            using (var g = Graphics.FromImage(preview))
            {
                g.DrawImage(_sourceImage,
                    new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                    cropRect, GraphicsUnit.Pixel);
            }
            _previewBox.Image = preview;
        }

        private static Rectangle MakeRect(Point a, Point b)
        {
            return new Rectangle(
                Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _previewBox.Image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
