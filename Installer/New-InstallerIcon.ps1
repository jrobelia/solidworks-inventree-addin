# New-InstallerIcon.ps1 — regenerates Installer\inventree-icon.ico from the
# installer artwork produced by New-IconArtwork.ps1. Run after regenerating
# inventree-icon.png; the .ico is committed so Package.ps1 does not depend
# on this script.
#
# Emits a multi-size ICO (16/32/48 PNG-encoded frames + 256 PNG frame).
# PNG-encoded ICO frames are supported on Windows Vista+, which covers the
# Win10/11 requirement, and by Inno Setup's SetupIconFile.

$src  = "$PSScriptRoot\inventree-icon.png"
$dest = "$PSScriptRoot\inventree-icon.ico"
$sizes = 16, 32, 48, 256

Add-Type -AssemblyName System.Drawing

$image = [System.Drawing.Image]::FromFile($src)
try {
    $frames = foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($image, 0, 0, $size, $size)
        } finally { $g.Dispose() }
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        [PSCustomObject]@{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
} finally { $image.Dispose() }

$fs = [System.IO.File]::Create($dest)
try {
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0)               # reserved
    $bw.Write([uint16]1)               # type: icon
    $bw.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { [byte]0 } else { [byte]$f.Size }
        $bw.Write($dim)                # width (0 = 256)
        $bw.Write($dim)                # height
        $bw.Write([byte]0)             # palette
        $bw.Write([byte]0)             # reserved
        $bw.Write([uint16]1)           # planes
        $bw.Write([uint16]32)          # bits per pixel
        $bw.Write([uint32]$f.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $bw.Write($f.Bytes) }
} finally { $fs.Close() }

Write-Host "Wrote $dest ($($frames.Count) frames)" -ForegroundColor Green
