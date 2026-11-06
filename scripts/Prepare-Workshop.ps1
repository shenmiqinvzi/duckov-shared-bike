#Requires -Version 5.1
<#
.SYNOPSIS
  Prepare the installed mod folder for the in-game Steam Workshop upload.

.DESCRIPTION
  The game's uploader packs the whole mod folder verbatim (SetItemContent(path)), so anything
  sitting in Duckov_Data\Mods\DuckovSharedBike ends up public. This script checks the installed
  folder against the build package, drops the debug symbols (they carry absolute local paths and
  are useless to subscribers), reports anything unexpected and prints what the Workshop will get.

  Run it after scripts\Build.ps1 and scripts\Install.ps1, then upload from inside the game.

.PARAMETER GamePath
  Escape from Duckov installation folder.

.PARAMETER KeepSymbols
  Leave DuckovSharedBike.pdb in place (only useful when you want symbolised crash reports).
#>
param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov',
    [switch]$KeepSymbols
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'artifacts\package\DuckovSharedBike'
$gameRoot = (Resolve-Path -LiteralPath $GamePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Duckov.exe'))) {
    throw 'This is not an Escape from Duckov installation.'
}

$modFolder = [System.IO.Path]::GetFullPath((Join-Path $gameRoot 'Duckov_Data\Mods\DuckovSharedBike'))
if (-not (Test-Path -LiteralPath $modFolder -PathType Container)) {
    throw "Mod folder not found: $modFolder. Run scripts\Install.ps1 first."
}

$shipped = @('DuckovSharedBike.dll','DuckovSharedBike.pdb','info.ini','preview.png','marker.png',
    'prototype.json','station-layout.json','README.txt','0Harmony.dll','LICENSE-Harmony.txt','THIRD-PARTY.md')

# 1. Installed copy must match the package, otherwise you would publish an older build than the one you tested.
foreach ($name in $shipped) {
    if ($name -eq 'DuckovSharedBike.pdb') { continue }
    $packaged = Join-Path $source $name
    $installed = Join-Path $modFolder $name
    if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) { throw "Installed file missing: $name" }
    if ((Get-FileHash -LiteralPath $packaged -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash) {
        throw "Installed $name does not match the package. Re-run scripts\Install.ps1."
    }
}

# 2. Symbols: 63 KB of absolute E:\javaproject\... paths for no subscriber benefit.
$pdb = Join-Path $modFolder 'DuckovSharedBike.pdb'
$removedPdb = $false
if (-not $KeepSymbols -and (Test-Path -LiteralPath $pdb -PathType Leaf)) {
    Remove-Item -LiteralPath $pdb -Force
    $removedPdb = $true
}

# 3. Anything else in the folder is published too. Delete known junk, report the rest.
$extras = @(Get-ChildItem -LiteralPath $modFolder -Force | Where-Object { $_.Name -notin $shipped })
foreach ($extra in $extras) {
    if ($extra.PSIsContainer -and $extra.Name -eq 'logs') {
        Remove-Item -LiteralPath $extra.FullName -Recurse -Force
        Write-Host "Removed stale runtime logs: $($extra.FullName)"
        continue
    }
    Write-Warning "Unexpected item will be uploaded as-is: $($extra.Name)"
}

# 4. Report what Steam will receive.
$infoPath = Join-Path $modFolder 'info.ini'
$infoText = [System.IO.File]::ReadAllText($infoPath, [System.Text.Encoding]::UTF8)
$version = if ($infoText -match '(?m)^\s*version\s*=\s*(\S+)\s*$') { $Matches[1] } else { 'unknown' }
$idMatch = [regex]::Match($infoText, '(?m)^\s*publishedFileId\s*=\s*(\d+)\s*$')
$itemId = if ($idMatch.Success) { $idMatch.Groups[1].Value } else { '' }

Add-Type -AssemblyName System.Drawing
$preview = Join-Path $modFolder 'preview.png'
$previewSize = ''
try {
    $image = [System.Drawing.Image]::FromFile($preview)
    $previewSize = "$($image.Width)x$($image.Height)"
    $image.Dispose()
} catch { $previewSize = 'unreadable' }

$files = @(Get-ChildItem -LiteralPath $modFolder -File -Recurse | Sort-Object FullName)
$total = ($files | Measure-Object -Property Length -Sum).Sum

Write-Host ''
Write-Host "Workshop content folder: $modFolder"
Write-Host "Version: $version    Preview: $previewSize    Total: $('{0:N2}' -f ($total / 1MB)) MB"
Write-Host ''
$files | ForEach-Object {
    Write-Host ('  {0,-26} {1,9:N0}  {2}' -f $_.Name, $_.Length, $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
}
Write-Host ''
if ($itemId) {
    Write-Host "Workshop item: UPDATE of $itemId"
    Write-Host "  https://steamcommunity.com/sharedfiles/filedetails/?id=$itemId"
} else {
    Write-Host 'Workshop item: NEW entry (info.ini has no publishedFileId)'
}
if ($removedPdb) { Write-Host 'Removed DuckovSharedBike.pdb before upload.' }
Write-Host ''
Write-Host 'Next: start Duckov (Steam online, same account) -> Mods menu -> the Shared Bike entry -> upload button -> Upload.'
Write-Host 'After it succeeds the game writes publishedFileId into the installed info.ini;'
Write-Host 'run scripts\Sync-WorkshopId.ps1 to copy that id back into mod\info.ini.'
