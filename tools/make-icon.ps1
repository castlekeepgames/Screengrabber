<#
.SYNOPSIS
    Regenerates src/Screengrabber/Screengrabber.ico: a blue rounded square with white snip-corner brackets,
    drawn separately at each size so small sizes stay crisp. Sizes up to 64 px are stored as classic 32-bit
    bitmaps (readable by every Windows API); 256 px is stored as PNG, as Windows expects.
#>
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\src\Screengrabber\Screengrabber.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $size / 32.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 10 * $s; $far = 32 * $s - $d - 1 * $s
    $path.AddArc(1 * $s, 1 * $s, $d, $d, 180, 90)
    $path.AddArc($far, 1 * $s, $d, $d, 270, 90)
    $path.AddArc($far, $far, $d, $d, 0, 90)
    $path.AddArc(1 * $s, $far, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(37, 99, 235))), $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 3 * $s))
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
    foreach ($bracket in @(@(7,14, 7,7, 14,7), @(18,7, 25,7, 25,14), @(25,18, 25,25, 18,25), @(14,25, 7,25, 7,18))) {
        $pts = for ($i = 0; $i -lt 6; $i += 2) { New-Object System.Drawing.PointF ($bracket[$i] * $s), ($bracket[$i + 1] * $s) }
        $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # BITMAPINFOHEADER (height doubled: XOR image + AND mask), bottom-up BGRA pixels, then an empty AND mask.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [Math]::Ceiling($size / 32) * 4
        $bw.Write((New-Object byte[] ($maskRow * $size)))
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

$images = foreach ($size in $sizes) { , (Draw-Icon $size) }

$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $file
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)     # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                     # ICONDIRENTRY per size
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($png in $images) { $w.Write($png) }
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $out)).Path + "\Screengrabber.ico", $file.ToArray())
"Wrote $out ($($file.Length) bytes, sizes: $($sizes -join ', '))"
