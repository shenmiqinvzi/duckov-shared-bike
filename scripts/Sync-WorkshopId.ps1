#Requires -Version 5.1
<#
.SYNOPSIS
  Copy the Workshop item id the game wrote into the installed info.ini back into mod\info.ini.

.DESCRIPTION
  After the first upload the game appends publishedFileId to the info.ini inside
  Duckov_Data\Mods\DuckovSharedBike. scripts\Install.ps1 preserves it across upgrades, but the
  repository still needs it so a fresh clone does not publish a duplicate item. This script
  makes the two agree and prints the public URL.

.PARAMETER GamePath
  Escape from Duckov installation folder.

.PARAMETER RepoInfo
  Repository copy of info.ini (default mod\info.ini).
#>
param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov',
    [string]$RepoInfo
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $RepoInfo) { $RepoInfo = Join-Path $projectRoot 'mod\info.ini' }

$installedInfo = Join-Path $GamePath 'Duckov_Data\Mods\DuckovSharedBike\info.ini'
if (-not (Test-Path -LiteralPath $installedInfo -PathType Leaf)) {
    throw "Installed info.ini not found: $installedInfo"
}
if (-not (Test-Path -LiteralPath $RepoInfo -PathType Leaf)) {
    throw "Repository info.ini not found: $RepoInfo"
}

$installedText = [System.IO.File]::ReadAllText($installedInfo, [System.Text.Encoding]::UTF8)
$match = [regex]::Match($installedText, '(?m)^\s*publishedFileId\s*=\s*(\d+)\s*$')
if (-not $match.Success) {
    throw 'The installed info.ini has no publishedFileId yet. Upload the mod in game first.'
}
$itemId = $match.Groups[1].Value

$repoText = [System.IO.File]::ReadAllText($RepoInfo, [System.Text.Encoding]::UTF8)
$existing = [regex]::Match($repoText, '(?m)^\s*publishedFileId\s*=\s*(\d+)\s*$')
if ($existing.Success) {
    if ($existing.Groups[1].Value -eq $itemId) {
        Write-Host "mod\info.ini already points at Workshop item $itemId"
    } else {
        $repoText = [regex]::Replace($repoText, '(?m)^\s*publishedFileId\s*=\s*\d+\s*$', "publishedFileId=$itemId")
        [System.IO.File]::WriteAllText($RepoInfo, $repoText, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Replaced Workshop item id $($existing.Groups[1].Value) with $itemId"
    }
} else {
    $trimmed = $repoText.TrimEnd("`r", "`n")
    [System.IO.File]::WriteAllText($RepoInfo, $trimmed + "`npublishedFileId=$itemId`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Added Workshop item id $itemId to mod\info.ini"
}

Write-Host "https://steamcommunity.com/sharedfiles/filedetails/?id=$itemId"
Write-Host 'Commit mod\info.ini so future builds keep updating the same Workshop entry.'
