<#
.SYNOPSIS
    Генерирует баннер для README (docs/assets/banner.png) из иконки приложения.

.DESCRIPTION
    Без сторонних обложек и скриншотов библиотеки: только иконка, название и слоган.
    Требует только System.Drawing (входит в Windows PowerShell).
#>
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root 'docs\assets\banner.png'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$w = 1280; $h = 420
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Фон: тёмный градиент
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 14, 9, 26), [System.Drawing.Color]::FromArgb(255, 43, 14, 64), 20)
$g.FillRectangle($bg, $rect)

# Свечение вокруг иконки
for ($i = 8; $i -ge 1; $i--) {
    $size = 240 + ($i * 26)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10, 233, 30, 140))
    $g.FillEllipse($brush, 200 - ($size / 2) + 60, ($h / 2) - ($size / 2), $size, $size)
    $brush.Dispose()
}

# Иконка приложения
$iconPath = Join-Path $root 'src\TempoKaraoke\Assets\AppIcon-256.png'
$icon = [System.Drawing.Image]::FromFile($iconPath)
$g.DrawImage($icon, 80, 90, 240, 240)

# Текст
$fontTitle = New-Object System.Drawing.Font 'Segoe UI Semibold', 58, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontSub = New-Object System.Drawing.Font 'Segoe UI', 26, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontFeatures = New-Object System.Drawing.Font 'Segoe UI', 20, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 245, 245, 255))
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 190, 180, 210))

$g.DrawString('Tempo Karaoke', $fontTitle, $white, 370, 120)
$g.DrawString('Умный караоке-плеер для ПК', $fontSub, $muted, 376, 210)
$g.DrawString('Локальная медиатека  ·  Умный поиск  ·  Веб-пульт с телефона', $fontFeatures, $muted, 376, 258)

# Нижняя линия-акцент
$accent = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle 0, ($h - 6), $w, 6), [System.Drawing.Color]::FromArgb(255, 90, 200, 250), [System.Drawing.Color]::FromArgb(255, 233, 30, 140), 0)
$g.FillRectangle($accent, 0, $h - 6, $w, 6)

$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $icon.Dispose(); $bg.Dispose(); $accent.Dispose()

Write-Host "Баннер: $out"
