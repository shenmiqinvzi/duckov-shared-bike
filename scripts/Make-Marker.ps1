param(
    [string]$SourcePath = '',
    [string]$DestinationPath = ''
)

# 地图标记图：把站点徽章原图裁到内容边界，去掉四周空白，让图标在小尺寸下占满画布。
# 徽章本体不动色、不改形——地图上看到的就是设计稿本身。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Join-Path $projectRoot 'artifacts\art\marker-source.png' }
if ([string]::IsNullOrWhiteSpace($DestinationPath)) { $DestinationPath = Join-Path $projectRoot 'mod\marker.png' }
$SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path

$bitmap = New-Object System.Drawing.Bitmap $SourcePath
try {
    $minX = $bitmap.Width; $minY = $bitmap.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if ($bitmap.GetPixel($x, $y).A -ge 16) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw 'Marker source has no visible pixels.' }

    $contentWidth = $maxX - $minX + 1
    $contentHeight = $maxY - $minY + 1
    $pad = 1
    $width = $contentWidth + 2 * $pad
    $height = $contentHeight + 2 * $pad
    $image = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $destinationRect = New-Object System.Drawing.Rectangle $pad, $pad, $contentWidth, $contentHeight
        $sourceRect = New-Object System.Drawing.Rectangle $minX, $minY, $contentWidth, $contentHeight
        $graphics.DrawImage($bitmap, $destinationRect, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        $image.Save($DestinationPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $image.Dispose()
    }
}
finally {
    $bitmap.Dispose()
}
Write-Host "Marker: $DestinationPath ($width x $height, cropped from $([System.IO.Path]::GetFileName($SourcePath)))"
