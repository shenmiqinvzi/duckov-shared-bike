param(
    # 256 = old square thumbnail, 2 = 512px (Steam Workshop recommends at least 512x512 for the preview).
    [ValidateRange(1, 4)]
    [int]$Scale = 2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$previewPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'mod\preview.png'
$infoPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'mod\info.ini'
$version = ((Get-Content -LiteralPath $infoPath -Encoding UTF8 | Where-Object { $_ -match '^version=' }) -split '=',2)[1].Trim()
$size = 256 * $Scale
$image = New-Object System.Drawing.Bitmap $size,$size
$graphics = [System.Drawing.Graphics]::FromImage($image)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(89,232,180)),7
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240,244,250))
$mint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(89,232,180))
$titleFont = New-Object System.Drawing.Font 'Arial',19,([System.Drawing.FontStyle]::Bold)
$smallFont = New-Object System.Drawing.Font 'Arial',10,([System.Drawing.FontStyle]::Regular)
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $graphics.ScaleTransform($Scale, $Scale)
    $graphics.Clear([System.Drawing.Color]::FromArgb(24,34,43))
    $graphics.DrawString('DUCKOV', $titleFont, $white, 24, 20)
    $graphics.DrawString('SHARED BIKE', $smallFont, $mint, 26, 54)
    $graphics.DrawEllipse($pen, 24, 144, 62, 62)
    $graphics.DrawEllipse($pen, 166, 144, 62, 62)
    $graphics.DrawLine($pen, 55, 175, 95, 119)
    $graphics.DrawLine($pen, 95, 119, 132, 175)
    $graphics.DrawLine($pen, 132, 175, 55, 175)
    $graphics.DrawLine($pen, 95, 119, 170, 119)
    $graphics.DrawLine($pen, 170, 119, 132, 175)
    $graphics.DrawLine($pen, 162, 98, 197, 175)
    $graphics.DrawLine($pen, 160, 96, 191, 96)
    $graphics.DrawLine($pen, 95, 119, 89, 104)
    $graphics.DrawLine($pen, 74, 102, 106, 102)
    $graphics.DrawString("PLAYTEST  $version", $smallFont, $white, 26, 225)
    $image.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $smallFont.Dispose()
    $titleFont.Dispose()
    $mint.Dispose()
    $white.Dispose()
    $pen.Dispose()
    $graphics.Dispose()
    $image.Dispose()
}
Write-Host "Preview: $previewPath ($size x $size)"
