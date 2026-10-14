# Generates the StickerBoard icon: Assets\app.ico (16-256 px)
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-PreviewPath preview.png]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot "..\Assets\app.ico"),
    [string]$PreviewPath
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function Color([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function Solid($c) { New-Object System.Drawing.SolidBrush $c }
function Pt([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

# Draw in 256-unit coordinates and scale down; small sizes skip fine details
function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 256, $size / 256)
    $detail = $size -ge 32

    # Board
    $board = New-RoundRect 8 8 240 240 52
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 0 0), (Pt 0 256), (Color 255 0x3A 0x40 0x4E), (Color 255 0x1A 0x1D 0x24)
    $g.FillPath($bg, $board)
    if ($detail) { $g.DrawPath((New-Object System.Drawing.Pen (Color 60 255 255 255), 4), $board) }

    # Photo card (top left, tilted slightly left)
    $state = $g.Save()
    $g.TranslateTransform(104, 100)
    $g.RotateTransform(-9)
    $g.FillPath((Solid (Color 90 0 0 0)), (New-RoundRect -66 -48 140 112 12))
    $g.FillPath((Solid (Color 255 0xF4 0xF6 0xFA)), (New-RoundRect -70 -54 140 112 12))
    $photo = New-Object System.Drawing.Drawing2D.LinearGradientBrush (Pt 0 -44), (Pt 0 40), (Color 255 0x7C 0xBA 0xFF), (Color 255 0x3A 0x78 0xE0)
    $g.FillRectangle($photo, -60, -44, 120, 82)
    if ($detail) {
        $g.FillEllipse((Solid (Color 255 0xFF 0xD8 0x4A)), 22, -36, 22, 22)
        $mountain = [System.Drawing.PointF[]]@((Pt -60 38), (Pt -26 -2), (Pt -4 22), (Pt 18 4), (Pt 60 38))
        $g.FillPolygon((Solid (Color 235 0xE6 0xF1 0xFF)), $mountain)
    }
    $g.Restore($state)

    # Sticky note (bottom right, tilted right, folded corner)
    $state = $g.Save()
    $g.TranslateTransform(166, 166)
    $g.RotateTransform(7)
    $note = [System.Drawing.PointF[]]@((Pt -62 -62), (Pt 62 -62), (Pt 62 30), (Pt 30 62), (Pt -62 62))
    $shadow = [System.Drawing.PointF[]]($note | ForEach-Object { Pt ($_.X + 4) ($_.Y + 7) })
    $g.FillPolygon((Solid (Color 100 0 0 0)), $shadow)
    $g.FillPolygon((Solid (Color 255 0xF7 0xDC 0x5A)), $note)
    $g.FillRectangle((Solid (Color 255 0xE8 0xC8 0x3C)), -62, -62, 124, 20)
    $g.FillPolygon((Solid (Color 255 0xD4 0xB0 0x2C)), [System.Drawing.PointF[]]@((Pt 62 30), (Pt 30 62), (Pt 30 30)))
    if ($detail) {
        $ink = Solid (Color 150 0x6B 0x55 0x10)
        $g.FillPath($ink, (New-RoundRect -46 -28 88 9 4.5))
        $g.FillPath($ink, (New-RoundRect -46 -8 78 9 4.5))
        $g.FillPath($ink, (New-RoundRect -46 12 52 9 4.5))
    }
    $g.Restore($state)

    $g.Dispose()
    $bmp
}

# Sizes below 256 are stored as classic DIB entries (BITMAPINFOHEADER + BGRA + AND mask) so older loaders can read them
function Get-DibBytes($bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]($s * $s * 4)); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {   # bottom-up
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $row.Length)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $maskStride = [int]([math]::Ceiling($s / 32) * 4)
    $w.Write((New-Object byte[] ($maskStride * $s)))
    $w.Flush()
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($PreviewPath) { $bmp.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
        $bytes = $ms.ToArray()
    } else {
        $bytes = [byte[]](Get-DibBytes $bmp)
    }
    $bmp.Dispose()
    , $bytes
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutPath) | Out-Null
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $OutPath)).Path + "\" + (Split-Path $OutPath -Leaf), $out.ToArray())
"wrote $OutPath ($($out.Length) bytes)"
