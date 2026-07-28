# Generates the app icon (flat Fluent-style, drawn geometrically) and packs a
# multi-size .ico (PNG-compressed entries) into src/RightMenu.App/Assets.
# Deterministic and re-runnable; no external assets required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\RightMenu.App\Assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null

function New-RoundedPath([System.Drawing.RectangleF]$rect, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $size / 256.0   # design units: 256 grid

    # Background: rounded square, vertical blue gradient
    $bgRect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $bgPath = New-RoundedPath $bgRect (58 * $s)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect, [System.Drawing.Color]::FromArgb(255, 41, 98, 197),
        [System.Drawing.Color]::FromArgb(255, 16, 52, 110), 90.0)
    $g.FillPath($bgBrush, $bgPath)

    # Context menu panel: white rounded rect (upper-left biased)
    $panelRect = New-Object System.Drawing.RectangleF((52 * $s), (56 * $s), (128 * $s), (140 * $s))
    $panelPath = New-RoundedPath $panelRect (14 * $s)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $panelPath)

    # Menu rows: 3 lines; second row is the accent (selected) row
    $rowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 176, 190, 210))
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 41, 98, 197))
    $rowX = 70 * $s; $rowW = 92 * $s; $rowH = 12 * $s; $rowR = 6 * $s
    foreach ($row in @(
            @{ Y = 80;  Brush = $rowBrush;    W = $rowW },
            @{ Y = 118; Brush = $accentBrush; W = $rowW },
            @{ Y = 156; Brush = $rowBrush;    W = 64 * $s })) {
        $r = New-Object System.Drawing.RectangleF($rowX, ($row.Y * $s), $row.W, $rowH)
        $p = New-RoundedPath $r $rowR
        $g.FillPath($row.Brush, $p)
        $p.Dispose()
    }

    # Terminal badge: dark rounded square, bottom-right, with ">_" drawn as strokes
    $badgeRect = New-Object System.Drawing.RectangleF((132 * $s), (140 * $s), (84 * $s), (76 * $s))
    $badgePath = New-RoundedPath $badgeRect (14 * $s)
    $badgeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 13, 27, 48))
    $g.FillPath($badgeBrush, $badgePath)

    $stroke = [Math]::Max(1.0, 9 * $s)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    # chevron '>'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF((150 * $s), (160 * $s))),
            (New-Object System.Drawing.PointF((166 * $s), (176 * $s))),
            (New-Object System.Drawing.PointF((150 * $s), (192 * $s)))))
    # underscore '_'
    $g.DrawLine($pen, (176 * $s), (196 * $s), (198 * $s), (196 * $s))

    $pen.Dispose(); $badgeBrush.Dispose(); $badgePath.Dispose()
    $accentBrush.Dispose(); $rowBrush.Dispose(); $white.Dispose()
    $panelPath.Dispose(); $bgBrush.Dispose(); $bgPath.Dispose(); $g.Dispose()
    return $bmp
}

# Render each ICO size natively (crisper than downscaling at 16/24px)
$sizes = @(256, 64, 48, 32, 24, 16)
$pngBlobs = @{}
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs[$sz] = $ms.ToArray()
    $ms.Dispose()
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Pack ICO: ICONDIR + ICONDIRENTRY[] + PNG payloads (PNG entries valid since Vista)
$icoPath = Join-Path $assets 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $blob = $pngBlobs[$sz]
    $dim = if ($sz -eq 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)   # width, height (0 = 256)
    $bw.Write([byte]0); $bw.Write([byte]0)         # colors, reserved
    $bw.Write([uint16]1); $bw.Write([uint16]32)    # planes, bitcount
    $bw.Write([uint32]$blob.Length); $bw.Write([uint32]$offset)
    $offset += $blob.Length
}
foreach ($sz in $sizes) { $bw.Write($pngBlobs[$sz]) }
$bw.Dispose(); $fs.Dispose()

Write-Host "written: $icoPath ($((Get-Item $icoPath).Length) bytes) + app.png" -ForegroundColor Green
