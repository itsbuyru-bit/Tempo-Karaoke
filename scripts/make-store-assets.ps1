<#
.SYNOPSIS
    Генерирует логотипы для Microsoft Store: 300×300, 150×150, 71×71,
    1080×1080, 2160×2160 и постеры 9:16 (720×1080, 1440×2160).

.DESCRIPTION
    Берёт большую иконку приложения (ico/AppIco_PNG_03.png), если она есть
    локально, иначе 256×256 из Assets. Все изображения — только иконка и текст,
    без скриншотов и чужих обложек.

.EXAMPLE
    .\make-store-assets.ps1
#>
[CmdletBinding()]
param(
    [string]$IconPath,
    [string]$OutputDir
)

Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDir) { $OutputDir = Join-Path $root 'poster\store' }
if (-not $IconPath) {
    $large = Join-Path $root 'ico\AppIco_PNG_03.png'
    $IconPath = if (Test-Path -LiteralPath $large) { $large } else { Join-Path $root 'src\TempoKaraoke\Assets\AppIcon-256.png' }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$titleFontName = 'Segoe UI Semibold'
$bodyFontName = 'Segoe UI'

function New-Canvas([int]$w, [int]$h, [switch]$Transparent) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    if (-not $Transparent) {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 14, 9, 26),
            [System.Drawing.Color]::FromArgb(255, 43, 14, 64),
            20)
        $g.FillRectangle($bg, $rect)
        $bg.Dispose()
    }

    return @{ Bitmap = $bmp; Graphics = $g }
}

function Save-Png($canvas, [string]$path) {
    $canvas.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Graphics.Dispose()
    $canvas.Bitmap.Dispose()
    Write-Host "  $path"
}

$icon = [System.Drawing.Image]::FromFile($IconPath)

# --- Прозрачные плитки: только иконка ---------------------------------------
foreach ($size in 300, 150, 71) {
    $canvas = New-Canvas $size $size -Transparent
    $canvas.Graphics.DrawImage($icon, 0, 0, $size, $size)
    Save-Png $canvas (Join-Path $OutputDir "store-logo-$size.png")
}

# --- Квадратные промо-картинки 1:1 ------------------------------------------
foreach ($size in 1080, 2160) {
    $canvas = New-Canvas $size $size
    $g = $canvas.Graphics
    $iconSize = [int]($size * 0.52)
    $g.DrawImage($icon, [int](($size - $iconSize) / 2), [int]($size * 0.18), $iconSize, $iconSize)

    $title = New-Object System.Drawing.Font $titleFontName, ($size * 0.10), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $sub = New-Object System.Drawing.Font $bodyFontName, ($size * 0.042), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 245, 255))
    $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 190, 180, 210))
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center

    $g.DrawString('Tempo Karaoke', $title, $white, (New-Object System.Drawing.RectangleF 0, ($size * 0.73), $size, ($size * 0.12)), $format)
    $g.DrawString('Smart video karaoke player for PC', $sub, $muted, (New-Object System.Drawing.RectangleF 0, ($size * 0.85), $size, ($size * 0.08)), $format)

    $title.Dispose(); $sub.Dispose(); $white.Dispose(); $muted.Dispose(); $format.Dispose()
    Save-Png $canvas (Join-Path $OutputDir "store-poster-square-$size.png")
}

# --- Постеры 9:16 -----------------------------------------------------------
foreach ($size in @(@(720, 1080), @(1440, 2160))) {
    $w = $size[0]; $h = $size[1]
    $canvas = New-Canvas $w $h
    $g = $canvas.Graphics

    $iconSize = [int]($w * 0.56)
    $g.DrawImage($icon, [int](($w - $iconSize) / 2), [int]($h * 0.16), $iconSize, $iconSize)

    $title = New-Object System.Drawing.Font $titleFontName, ($w * 0.115), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $sub = New-Object System.Drawing.Font $bodyFontName, ($w * 0.048), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $features = New-Object System.Drawing.Font $bodyFontName, ($w * 0.038), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 245, 255))
    $muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 190, 180, 210))
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center

    $g.DrawString('Tempo Karaoke', $title, $white, (New-Object System.Drawing.RectangleF 0, ($h * 0.56), $w, ($h * 0.09)), $format)
    $g.DrawString('Smart video karaoke player for PC', $sub, $muted, (New-Object System.Drawing.RectangleF 0, ($h * 0.66), $w, ($h * 0.05)), $format)
    $g.DrawString("Local video library`nPhone remote with QR`nSmart search and queue", $features, $muted, (New-Object System.Drawing.RectangleF 0, ($h * 0.74), $w, ($h * 0.16)), $format)

    $title.Dispose(); $sub.Dispose(); $features.Dispose(); $white.Dispose(); $muted.Dispose(); $format.Dispose()
    Save-Png $canvas (Join-Path $OutputDir "store-poster-9x16-${w}x${h}.png")
}

$icon.Dispose()
Write-Host "Готово: $OutputDir"
