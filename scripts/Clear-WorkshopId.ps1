#Requires -Version 5.1
<#
.SYNOPSIS
  Remove a broken publishedFileId so the next upload creates a fresh Workshop item.

.DESCRIPTION
  If an upload left a phantom item id (the game did create an id but the submit failed, or the
  item was deleted on the Workshop website), every later upload tries to UPDATE that dead item
  and fails again. This clears the id in both the installed info.ini and mod\info.ini.

  Close Duckov first - the game rewrites info.ini on every upload attempt.

.PARAMETER GamePath
  Escape from Duckov installation folder.

.PARAMETER KeepRepo
  Leave mod\info.ini untouched (only fix the installed copy).
#>
param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov',
    [switch]$KeepRepo
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if (Get-Process -Name Duckov -ErrorAction SilentlyContinue) {
    throw 'Duckov is running. Close the game first: it rewrites info.ini when an upload starts.'
}

$targets = @(Join-Path $GamePath 'Duckov_Data\Mods\DuckovSharedBike\info.ini')
if (-not $KeepRepo) { $targets += (Join-Path $projectRoot 'mod\info.ini') }

foreach ($path in $targets) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Write-Warning "Not found, skipped: $path"
        continue
    }
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $match = [regex]::Match($text, '(?m)^\s*publishedFileId\s*=\s*\d+\s*\r?\n?')
    if (-not $match.Success) {
        Write-Host "No publishedFileId to remove: $path"
        continue
    }
    $cleared = $text.Remove($match.Index, $match.Length)
    [System.IO.File]::WriteAllText($path, $cleared, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Removed publishedFileId from $path"
}

Write-Host ''
Write-Host 'Next upload will create a NEW Workshop item. Do not run scripts\Sync-WorkshopId.ps1'
Write-Host 'until that upload succeeded, otherwise the dead id comes back.'
