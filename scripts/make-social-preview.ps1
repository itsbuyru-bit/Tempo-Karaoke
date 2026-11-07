<#
.SYNOPSIS
    Generates the GitHub social preview image (1280x640) for Tempo Karaoke.

.DESCRIPTION
    Uses only the app icon, the English library screenshot and System.Drawing.
    Output: docs/assets/social-preview.png
#>
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root 'docs\assets\social-preview.png'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$w = 1280; $h = 640
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

# Typographic characters are built from code points so the script stays ASCII-only.
$dot = [string][char]0x00B7
$dash = [string][char]0x2014

function New-RoundPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-SoftGlow($graphics, [single]$cx, [single]$cy, [single]$radius, [System.Drawing.Color]$color) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $radius, $cy - $radius, $radius * 2, $radius * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $brush.CenterPoint = New-Object System.Drawing.PointF $cx, $cy
    $brush.CenterColor = $color
    $brush.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, $color.R, $color.G, $color.B))
    $graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

# Background: deep purple gradient
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 10, 6, 20), [System.Drawing.Color]::FromArgb(255, 38, 12, 58), 18)
$g.FillRectangle($bg, $rect)

# Soft brand glows
Add-SoftGlow $g 1160 560 520 ([System.Drawing.Color]::FromArgb(70, 233, 30, 140))
Add-SoftGlow $g 120 40 420 ([System.Drawing.Color]::FromArgb(60, 56, 189, 248))
Add-SoftGlow $g 140 150 150 ([System.Drawing.Color]::FromArgb(80, 233, 30, 140))

# Subtle dot grid on the left
$dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(22, 255, 255, 255))
for ($x = 56; $x -lt 620; $x += 32) {
    for ($y = 56; $y -lt 600; $y += 32) {
        $g.FillEllipse($dotBrush, $x, $y, 2, 2)
    }
}
$dotBrush.Dispose()

# App icon
$iconPath = Join-Path $root 'site\assets\appicon.png'
$icon = [System.Drawing.Image]::FromFile($iconPath)
$g.DrawImage($icon, 76, 86, 128, 128)

$fontEyebrow = New-Object System.Drawing.Font 'Segoe UI Semibold', 17, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontTitle = New-Object System.Drawing.Font 'Segoe UI Semibold', 64, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontTag = New-Object System.Drawing.Font 'Segoe UI', 27, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontFeat = New-Object System.Drawing.Font 'Segoe UI', 21, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fontMeta = New-Object System.Drawing.Font 'Segoe UI', 16, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)

$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 247, 246, 255))
$muted = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 198, 187, 216))
$dim = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 168, 154, 194))
$pink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 61, 154))
$blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 90, 200, 250))

$g.DrawString("WINDOWS  $dot  OPEN SOURCE  $dot  MIT", $fontEyebrow, $blue, 78, 232)
$g.DrawString('Tempo Karaoke', $fontTitle, $white, 72, 258)
$g.DrawString('Smart karaoke player for PC', $fontTag, $muted, 76, 352)

$features = @(
    "Local video library  $dot  instant search",
    'Live queue everyone can control',
    "Phone web remote  $dot  scan and sing"
)
$y = 410
foreach ($line in $features) {
    $g.FillEllipse($pink, 80, $y + 10, 8, 8)
    $g.DrawString($line, $fontFeat, $muted, 102, $y)
    $y += 42
}

$g.DrawString("WinUI 3   $dot   LibVLC   $dot   .NET", $fontMeta, $dim, 78, 552)

# Screenshot frame
$fx = 655; $fy = 68; $fw = 620; $fh = 504; $fr = 18
$frame = New-RoundPath $fx $fy $fw $fh $fr

# Shadow
for ($i = 14; $i -ge 1; $i--) {
    $shadow = New-RoundPath ($fx - $i) ($fy - $i + 10) ($fw + $i * 2) ($fh + $i * 2) ($fr + $i)
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(8, 0, 0, 0))
    $g.FillPath($shadowBrush, $shadow)
    $shadowBrush.Dispose(); $shadow.Dispose()
}

# Screenshot: content area only, so the cards stay readable
$shotPath = Join-Path $root 'site\assets\screenshots\library.png'
$shot = [System.Drawing.Image]::FromFile($shotPath)
$srcRect = New-Object System.Drawing.RectangleF 512, 0, 1426, 1160
$scale = [Math]::Max($fw / $srcRect.Width, $fh / $srcRect.Height)
$dw = $srcRect.Width * $scale; $dh = $srcRect.Height * $scale
$dx = $fx + ($fw - $dw) / 2; $dy = $fy + ($fh - $dh) / 2
$destRect = New-Object System.Drawing.RectangleF ([single]$dx), ([single]$dy), ([single]$dw), ([single]$dh)

$state = $g.Save()
$g.SetClip($frame)
$g.DrawImage($shot, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Restore($state)

# Gradient border around the frame
$borderRect = New-Object System.Drawing.Rectangle $fx, $fy, $fw, $fh
$borderBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($borderRect, [System.Drawing.Color]::FromArgb(255, 90, 200, 250), [System.Drawing.Color]::FromArgb(255, 255, 61, 154), 20)
$borderPen = New-Object System.Drawing.Pen $borderBrush, 2
$g.DrawPath($borderPen, $frame)

# Bottom accent line
$accentRect = New-Object System.Drawing.Rectangle 0, ($h - 8), $w, 8
$accent = New-Object System.Drawing.Drawing2D.LinearGradientBrush($accentRect, [System.Drawing.Color]::FromArgb(255, 90, 200, 250), [System.Drawing.Color]::FromArgb(255, 233, 30, 140), 0)
$g.FillRectangle($accent, $accentRect)

$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose(); $bmp.Dispose()
$icon.Dispose(); $shot.Dispose()
$bg.Dispose(); $accent.Dispose(); $borderBrush.Dispose(); $borderPen.Dispose(); $frame.Dispose()
$white.Dispose(); $muted.Dispose(); $dim.Dispose(); $pink.Dispose(); $blue.Dispose()
$fontEyebrow.Dispose(); $fontTitle.Dispose(); $fontTag.Dispose(); $fontFeat.Dispose(); $fontMeta.Dispose()

Write-Host "Social preview: $out"
