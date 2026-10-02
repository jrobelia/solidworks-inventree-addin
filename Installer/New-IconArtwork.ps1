# New-IconArtwork.ps1 — draws the installer icon from scratch: a red
# isometric cube (SolidWorks part) upper-left overlapping a gold/blue
# database cylinder (InvenTree) lower-right, on a transparent canvas so the
# shapes read at small sizes. The original sw-inventree-addin_icon.png is
# left untouched.
#
# Output: Installer\inventree-icon.png — the source New-InstallerIcon.ps1
# wraps into inventree-icon.ico. Run this first when the artwork changes.

$dest = "$PSScriptRoot\inventree-icon.png"
$S = 1024

Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function Fill-Poly([double[][]]$pts, [System.Drawing.Color]$c, [int]$stroke = 0) {
    $f = [System.Drawing.PointF[]]($pts | ForEach-Object {
        [System.Drawing.PointF]::new($_[0], $_[1]) })
    if ($stroke -gt 0) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPolygon($pen, $f)
        $pen.Dispose()
    }
    $brush = New-Object System.Drawing.SolidBrush $c
    $g.FillPolygon($brush, $f)
    $brush.Dispose()
}

# --- Cube (isometric), upper-left ---
# Top rhombus vertices; the vertical drop makes the two side faces.
$T  = 310, 70
$R  = 580, 230
$B  = 310, 390
$L  = 40, 230
$L2 = 40, 530
$B2 = 310, 690
$R2 = 580, 530
$stroke = 16
Fill-Poly @($T, $R, $B, $L)      ([System.Drawing.Color]::FromArgb(242, 35, 60))  $stroke   # top
Fill-Poly @($L, $B, $B2, $L2)    ([System.Drawing.Color]::FromArgb(200, 22, 42))  $stroke   # left
Fill-Poly @($B, $R, $R2, $B2)    ([System.Drawing.Color]::FromArgb(165, 15, 32))  $stroke   # right

# --- Database cylinder, lower-right (drawn last -> sits in front) ---
$cx = 700; $rx = 290; $ry = 105
$topCy = 360; $botCy = 880

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding
$path.AddEllipse($cx - $rx, $topCy - $ry, 2 * $rx, 2 * $ry)
$path.AddRectangle((New-Object System.Drawing.Rectangle ($cx - $rx), $topCy, (2 * $rx), ($botCy - $topCy)))
$path.AddEllipse($cx - $rx, $botCy - $ry, 2 * $rx, 2 * $ry)
$g.SetClip($path)

$bands = @(
    @{ top = $topCy; bottom = 545; color = [System.Drawing.Color]::FromArgb(247, 181, 0) }
    @{ top = 545;    bottom = 730; color = [System.Drawing.Color]::FromArgb(232, 154, 0) }
    @{ top = 730;    bottom = $botCy + $ry; color = [System.Drawing.Color]::FromArgb(61, 123, 217) }
)
foreach ($band in $bands) {
    $brush = New-Object System.Drawing.SolidBrush $band.color
    $g.FillRectangle($brush, $cx - $rx, $band.top, 2 * $rx, ($band.bottom - $band.top))
    $brush.Dispose()
}
# band separators: white ellipse rings at the junctions
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 14
foreach ($j in @(545, 730)) {
    $g.DrawEllipse($pen, $cx - $rx, $j - $ry, 2 * $rx, 2 * $ry)
}
$pen.Dispose()
$g.ResetClip()

# top face of the cylinder
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 232, 180))
$g.FillEllipse($brush, $cx - $rx, $topCy - $ry, 2 * $rx, 2 * $ry)
$brush.Dispose()
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 14
$g.DrawEllipse($pen, $cx - $rx, $topCy - $ry, 2 * $rx, 2 * $ry)
$pen.Dispose()

$g.Dispose()
$bmp.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Wrote $dest" -ForegroundColor Green
