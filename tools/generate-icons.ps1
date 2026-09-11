# 生成 CliManager 应用 Logo 与默认图标资源。
# 产物（输出至 src/CliManager.App/Assets/）：
#   appicon.ico        多尺寸应用图标（exe / 安装包 / 快捷方式）
#   logo.png           256px 应用 Logo（窗口标题栏、软件内显示）
#   default-tool.png   默认工具图标（应用内展示，内嵌资源）
#   default-tool.ico   默认工具图标（随程序分发，写入注册表供右键菜单显示）
#   default-folder.png 默认文件夹图标（应用内展示）
#   default-folder.ico 默认文件夹图标（随程序分发）
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\CliManager.App\Assets")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

function New-RoundedPath {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-GradientBrush {
    param([single]$size, [string]$from, [string]$to)
    $p1 = New-Object System.Drawing.Point -ArgumentList 0, 0
    $p2 = New-Object System.Drawing.Point -ArgumentList $size, $size
    $c1 = [System.Drawing.ColorTranslator]::FromHtml($from)
    $c2 = [System.Drawing.ColorTranslator]::FromHtml($to)
    return New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $p1, $p2, $c1, $c2
}

function Invoke-Graphics {
    param([object]$action, [int]$size)
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    try {
        & $action $g $size
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

function Draw-Glyph {
    param($g, [single]$size, [single]$fontSize, [string]$colorFrom, [string]$colorTo, [single]$offsetYRatio)
    $font = New-Object System.Drawing.Font -ArgumentList "Consolas", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-GradientBrush $size $colorFrom $colorTo
    try {
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rectY = $size * $offsetYRatio
        $rect = New-Object System.Drawing.RectangleF -ArgumentList 0, $rectY, $size, $size
        $g.DrawString(">_", $font, $brush, $rect, $format)
    }
    finally {
        $font.Dispose()
        $brush.Dispose()
    }
}

function Get-FontSize {
    param([int]$s, [single]$largeRatio, [single]$smallRatio)
    if ($s -ge 48) {
        return [single]($s * $largeRatio)
    }
    return [single]($s * $smallRatio)
}

# ---- 应用 Logo：深色圆角方块 + 渐变描边 + 青色终端提示符 ----
function New-AppIconBitmap {
    param([int]$size)
    Invoke-Graphics {
        param($g, [int]$s)
        $pad = [Math]::Max(1.0, $s * 0.03)
        $inner = $s - ($pad * 2)
        $radius = $inner * 0.24

        $bgPath = New-RoundedPath $pad $pad $inner $inner $radius
        try {
            $bgBrush = New-GradientBrush $s "#1E293B" "#0B1220"
            $g.FillPath($bgBrush, $bgPath)
            $bgBrush.Dispose()
        }
        finally {
            $bgPath.Dispose()
        }

        $borderPath = New-RoundedPath $pad $pad $inner $inner $radius
        try {
            $borderWidth = [Math]::Max(1.0, $s * 0.045)
            $borderPen = New-Object System.Drawing.Pen -ArgumentList (New-GradientBrush $s "#22D3EE" "#2563EB"), $borderWidth
            $g.DrawPath($borderPen, $borderPath)
            $borderPen.Dispose()
        }
        finally {
            $borderPath.Dispose()
        }

        $fontSize = Get-FontSize $s 0.56 0.62
        Draw-Glyph $g $s $fontSize "#67E8F9" "#60A5FA" -0.02
    } $size
}

# ---- 默认工具图标：浅色圆角芯片 + 深灰终端提示符（右键菜单中未识别图标的统一占位） ----
function New-DefaultToolBitmap {
    param([int]$size)
    Invoke-Graphics {
        param($g, [int]$s)
        $pad = [Math]::Max(1.0, $s * 0.06)
        $inner = $s - ($pad * 2)
        $radius = $inner * 0.24

        $chipPath = New-RoundedPath $pad $pad $inner $inner $radius
        try {
            $fillColor = [System.Drawing.ColorTranslator]::FromHtml("#EDF2F7")
            $fill = New-Object System.Drawing.SolidBrush -ArgumentList $fillColor
            $g.FillPath($fill, $chipPath)
            $fill.Dispose()

            $strokeColor = [System.Drawing.ColorTranslator]::FromHtml("#8FA3B8")
            $stroke = New-Object System.Drawing.SolidBrush -ArgumentList $strokeColor
            $strokeWidth = [Math]::Max(1.0, $s * 0.05)
            $pen = New-Object System.Drawing.Pen -ArgumentList $stroke, $strokeWidth
            $g.DrawPath($pen, $chipPath)
            $pen.Dispose()
            $stroke.Dispose()
        }
        finally {
            $chipPath.Dispose()
        }

        $fontSize = Get-FontSize $s 0.52 0.58
        Draw-Glyph $g $s $fontSize "#5B6B7C" "#475569" -0.01
    } $size
}

# ---- 默认文件夹图标：蓝灰双色调文件夹（未配置图标的文件夹统一占位） ----
function New-DefaultFolderBitmap {
    param([int]$size)
    Invoke-Graphics {
        param($g, [int]$s)

        $tabPath = New-RoundedPath ($s * 0.12) ($s * 0.20) ($s * 0.34) ($s * 0.20) ($s * 0.05)
        try {
            $tabColor = [System.Drawing.ColorTranslator]::FromHtml("#7A93AD")
            $tabBrush = New-Object System.Drawing.SolidBrush -ArgumentList $tabColor
            $g.FillPath($tabBrush, $tabPath)
            $tabBrush.Dispose()
        }
        finally {
            $tabPath.Dispose()
        }

        $bodyPath = New-RoundedPath ($s * 0.10) ($s * 0.28) ($s * 0.80) ($s * 0.56) ($s * 0.07)
        try {
            $bodyColor = [System.Drawing.ColorTranslator]::FromHtml("#A8BFD6")
            $bodyBrush = New-Object System.Drawing.SolidBrush -ArgumentList $bodyColor
            $g.FillPath($bodyBrush, $bodyPath)
            $bodyBrush.Dispose()
        }
        finally {
            $bodyPath.Dispose()
        }

        $flapPath = New-RoundedPath ($s * 0.10) ($s * 0.40) ($s * 0.80) ($s * 0.44) ($s * 0.07)
        try {
            $flapColor = [System.Drawing.ColorTranslator]::FromHtml("#C4D6E8")
            $flapBrush = New-Object System.Drawing.SolidBrush -ArgumentList $flapColor
            $g.FillPath($flapBrush, $flapPath)
            $flapBrush.Dispose()

            $outlineColor = [System.Drawing.ColorTranslator]::FromHtml("#7A93AD")
            $outline = New-Object System.Drawing.SolidBrush -ArgumentList $outlineColor
            $outlineWidth = [Math]::Max(1.0, $s * 0.025)
            $pen = New-Object System.Drawing.Pen -ArgumentList $outline, $outlineWidth
            $g.DrawPath($pen, $flapPath)
            $pen.Dispose()
            $outline.Dispose()
        }
        finally {
            $flapPath.Dispose()
        }
    } $size
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$bmp)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

# 将多张位图打包为 PNG 压缩条目的多尺寸 .ico（Windows 10 1809+ 完整支持）
function Write-IcoFile {
    param([string]$path, [System.Drawing.Bitmap[]]$bitmaps)
    $pngBlobs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($bmp in $bitmaps) {
        $pngBlobs.Add((Get-PngBytes $bmp))
    }

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter -ArgumentList $ms
    $bw.Write([uint16]0)                          # reserved
    $bw.Write([uint16]1)                          # type: icon
    $bw.Write([uint16]$pngBlobs.Count)            # image count

    $offset = 6 + (16 * $pngBlobs.Count)
    for ($i = 0; $i -lt $pngBlobs.Count; $i++) {
        $size = $bitmaps[$i].Width
        $dimByte = $size
        if ($size -ge 256) {
            $dimByte = 0
        }
        $bw.Write([byte]$dimByte)                 # width
        $bw.Write([byte]$dimByte)                 # height
        $bw.Write([byte]0)                        # palette colors
        $bw.Write([byte]0)                        # reserved
        $bw.Write([uint16]1)                      # color planes
        $bw.Write([uint16]32)                     # bits per pixel
        $bw.Write([uint32]$pngBlobs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $pngBlobs[$i].Length
    }

    foreach ($blob in $pngBlobs) {
        $bw.Write($blob)
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
    $bw.Dispose()
}

# ---- 生成应用 Logo ----
$appSizes = @(16, 24, 32, 48, 64, 128, 256)
$appBitmaps = @()
foreach ($s in $appSizes) {
    $appBitmaps += New-AppIconBitmap $s
}
Write-IcoFile (Join-Path $OutDir "appicon.ico") $appBitmaps
$logoPath = Join-Path $OutDir "logo.png"
$appBitmaps[-1].Save($logoPath, [System.Drawing.Imaging.ImageFormat]::Png)

# ---- 生成默认图标 ----
$defaultSizes = @(16, 24, 32, 48, 64)

$toolBitmaps = @()
foreach ($s in $defaultSizes) {
    $toolBitmaps += New-DefaultToolBitmap $s
}
Write-IcoFile (Join-Path $OutDir "default-tool.ico") $toolBitmaps
$toolBitmaps[-1].Save((Join-Path $OutDir "default-tool.png"), [System.Drawing.Imaging.ImageFormat]::Png)

$folderBitmaps = @()
foreach ($s in $defaultSizes) {
    $folderBitmaps += New-DefaultFolderBitmap $s
}
Write-IcoFile (Join-Path $OutDir "default-folder.ico") $folderBitmaps
$folderBitmaps[-1].Save((Join-Path $OutDir "default-folder.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# ---- 清理 ----
foreach ($bmp in ($appBitmaps + $toolBitmaps + $folderBitmaps)) {
    $bmp.Dispose()
}

Write-Host "✅ 图标资源已生成至 $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name, Length -AutoSize
