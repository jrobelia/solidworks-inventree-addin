using System;
using System.Drawing;

namespace SwInventreeAddin.UI
{
    /// <summary>
    /// Pure geometry logic for image cropping.
    /// No UI framework dependency — fully unit-testable.
    /// </summary>
    public class CropGeometry
    {
        private readonly int _imageWidth;
        private readonly int _imageHeight;

        // Drag state
        private Point     _dragStart;
        private Point     _dragEnd;
        private bool      _isDragging;
        private bool      _hasCrop;

        // Move state
        private bool      _isMoving;
        private Point     _moveOrigin;
        private Rectangle _rectAtMoveStart;

        /// <summary>The crop rectangle in image coordinates (Empty = full image).</summary>
        public Rectangle CropRectangle { get; private set; } = Rectangle.Empty;

        /// <summary>Whether a valid crop rectangle exists.</summary>
        public bool HasCrop => _hasCrop;

        /// <summary>Whether the user is currently dragging a new rectangle.</summary>
        public bool IsDragging => _isDragging;

        /// <summary>Whether the user is currently moving an existing rectangle.</summary>
        public bool IsMoving => _isMoving;

        /// <summary>Whether drag should produce a square.</summary>
        public bool SquareLock { get; set; } = true;

        public CropGeometry(int imageWidth, int imageHeight)
        {
            _imageWidth  = imageWidth;
            _imageHeight = imageHeight;
        }

        // ── Public API (called by the window on mouse events) ─────────────

        /// <summary>Begin a new drag or start moving an existing rectangle.</summary>
        public void OnMouseDown(Point imagePoint)
        {
            if (_hasCrop && CropRectangle.Contains(imagePoint))
            {
                _isMoving        = true;
                _isDragging      = false;
                _moveOrigin      = imagePoint;
                _rectAtMoveStart = CropRectangle;
            }
            else
            {
                _isMoving   = false;
                _isDragging = true;
                _hasCrop    = false;
                _dragStart  = imagePoint;
                _dragEnd    = imagePoint;
            }
        }

        /// <summary>Update the in-progress drag or move. Returns true if the display needs updating.</summary>
        public bool OnMouseMove(Point imagePoint)
        {
            if (_isDragging)
            {
                _dragEnd = ApplySquareLock(_dragStart, imagePoint);
                return true;
            }

            if (_isMoving)
            {
                int dx   = imagePoint.X - _moveOrigin.X;
                int dy   = imagePoint.Y - _moveOrigin.Y;
                int newX = Math.Max(0, Math.Min(_rectAtMoveStart.X + dx, _imageWidth  - _rectAtMoveStart.Width));
                int newY = Math.Max(0, Math.Min(_rectAtMoveStart.Y + dy, _imageHeight - _rectAtMoveStart.Height));
                CropRectangle = new Rectangle(newX, newY, _rectAtMoveStart.Width, _rectAtMoveStart.Height);
                return true;
            }

            return false;
        }

        /// <summary>Returns true if <paramref name="imagePoint"/> is inside the current crop rectangle.</summary>
        public bool HitTest(Point imagePoint) =>
            _hasCrop && CropRectangle.Contains(imagePoint);

        /// <summary>Finish a drag or move.</summary>
        public void OnMouseUp(Point imagePoint)
        {
            if (_isMoving)
            {
                _isMoving = false;
                return;
            }

            if (!_isDragging) return;
            _isDragging = false;

            _dragEnd = ApplySquareLock(_dragStart, imagePoint);
            CommitDrag();
        }

        /// <summary>
        /// Test seam: simulates a complete drag in image coordinates.
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

        /// <summary>Returns the active rectangle (in-progress drag or committed crop).</summary>
        public Rectangle GetActiveRect()
        {
            if (_isDragging)
                return MakeRect(_dragStart, _dragEnd);
            return CropRectangle;
        }

        // ── Coordinate translation ──────────────────────────────────────

        /// <summary>Convert a display-space point to image coordinates.</summary>
        public Point DisplayToImage(Point displayPoint, double displayWidth, double displayHeight)
        {
            double scale = Math.Min(displayWidth / _imageWidth, displayHeight / _imageHeight);
            double offX  = (displayWidth  - _imageWidth  * scale) / 2.0;
            double offY  = (displayHeight - _imageHeight * scale) / 2.0;

            int imgX = (int)((displayPoint.X - offX) / scale);
            int imgY = (int)((displayPoint.Y - offY) / scale);

            imgX = Math.Max(0, Math.Min(imgX, _imageWidth  - 1));
            imgY = Math.Max(0, Math.Min(imgY, _imageHeight - 1));

            return new Point(imgX, imgY);
        }

        /// <summary>Convert an image-coordinate point to display-space.</summary>
        public Point ImageToDisplay(Point imagePoint, double displayWidth, double displayHeight)
        {
            double scale = Math.Min(displayWidth / _imageWidth, displayHeight / _imageHeight);
            double offX  = (displayWidth  - _imageWidth  * scale) / 2.0;
            double offY  = (displayHeight - _imageHeight * scale) / 2.0;

            return new Point(
                (int)(imagePoint.X * scale + offX),
                (int)(imagePoint.Y * scale + offY));
        }

        // ── Internals ─────────────────────────────────────────────

        private Point ApplySquareLock(Point start, Point end)
        {
            if (!SquareLock) return end;

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
            }
            else
            {
                _hasCrop      = false;
                CropRectangle = Rectangle.Empty;
            }
        }

        /// <summary>Normalises two corner points into a positive-size Rectangle.</summary>
        public static Rectangle MakeRect(Point a, Point b) =>
            new Rectangle(
                Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
