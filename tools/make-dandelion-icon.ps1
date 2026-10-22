param([string]$OutIco, [string]$OutPreview)
Add-Type -AssemblyName System.Drawing

function New-DandelionBitmap([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $S / 256.0

    # badge: dusk-blue circle
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $S)),
        [System.Drawing.Color]::FromArgb(255, 58, 110, 165), [System.Drawing.Color]::FromArgb(255, 32, 72, 112))
    $g.FillEllipse($bg, 4*$k, 4*$k, 248*$k, 248*$k)

    $cx = 124*$k; $cy = 104*$k

    # stem, gently curved
    $stem = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 126, 196, 112), [Math]::Max(1.5, 9*$k))
    $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
    $g.DrawBezier($stem, $cx, $cy, $cx + 6*$k, $cy + 60*$k, $cx - 14*$k, $cy + 100*$k, $cx + 4*$k, 236*$k)

    # seed head: rays with a fluffy tip each
    $rayPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 255, 255, 255), [Math]::Max(1.0, 2.6*$k))
    $tuft   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
    $haze   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55, 255, 255, 255))
    $R = 74*$k
    $g.FillEllipse($haze, $cx - $R - 6*$k, $cy - $R - 6*$k, 2*($R + 6*$k), 2*($R + 6*$k))
    $n = 40
    for ($i = 0; $i -lt $n; $i++) {
        $a = 2 * [Math]::PI * $i / $n + 0.06 * [Math]::Sin($i * 1.7)
        $rayLen = $R * (0.9 + 0.1 * [Math]::Cos($i * 2.3))
        $x = $cx + [Math]::Cos($a) * $rayLen; $y = $cy + [Math]::Sin($a) * $rayLen
        $g.DrawLine($rayPen, $cx + [Math]::Cos($a) * 10*$k, $cy + [Math]::Sin($a) * 10*$k, $x, $y)
        $t = [Math]::Max(1.2, 7*$k)
        $g.FillEllipse($tuft, $x - $t, $y - $t, 2*$t, 2*$t)
    }
    # center
    $core = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 196, 170, 90))
    $g.FillEllipse($core, $cx - 13*$k, $cy - 13*$k, 26*$k, 26*$k)

    # two seeds drifting away (only where there's room to see them)
    if ($S -ge 48) {
        foreach ($d in @(@(204, 58, -0.7), @(222, 112, -0.35))) {
            $sx = $d[0]*$k; $sy = $d[1]*$k; $ang = $d[2]
            $len = 22*$k
            $ex = $sx - [Math]::Cos($ang) * $len; $ey = $sy - [Math]::Sin($ang) * $len
            $g.DrawLine($rayPen, $sx, $sy, $ex, $ey)
            for ($j = -2; $j -le 2; $j++) {
                $fa = $ang + $j * 0.35
                $g.DrawLine($rayPen, $sx, $sy, $sx + [Math]::Cos($fa) * 9*$k, $sy + [Math]::Sin($fa) * 9*$k)
            }
        }
    }
    $g.Dispose()
    return $bmp
}

# Render large once, downscale for small frames (cleaner than drawing tiny).
$master = New-DandelionBitmap 256
$master.Save($OutPreview, [System.Drawing.Imaging.ImageFormat]::Png)

$frames = @()
foreach ($s in 16, 24, 32, 48, 64, 256) {
    if ($s -eq 256) { $b = $master } else {
        $b = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($b)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'AntiAlias'
        $g.DrawImage($master, 0, 0, $s, $s); $g.Dispose()
    }
    $ms = New-Object System.IO.MemoryStream
    if ($s -eq 256) {
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # classic DIB frame (32bpp BGRA bottom-up + 1bpp AND mask): loads everywhere, incl. .NET's Icon class
        $w = New-Object System.IO.BinaryWriter($ms)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]0); $w.Write([int]($s * $s * 4 + $maskStride * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
        for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) {
            $c = $b.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
        for ($y = $s - 1; $y -ge 0; $y--) {
            $row = New-Object byte[] $maskStride
            for ($x = 0; $x -lt $s; $x++) { if ($b.GetPixel($x, $y).A -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
            $w.Write($row)
        }
        $w.Flush()
    }
    $frames += ,@($s, $ms.ToArray())
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes, $($frames.Count) sizes)"
