# Make-Handoff.ps1 - build a clean package for someone else to upload to the Workshop.
#
# The helper's account must OWN the game (family-shared libraries cannot publish).
# This script:
#   * copies artifacts\package\DuckovSharedBike (written by Build.ps1)
#   * drops DuckovSharedBike.pdb
#   * removes any publishedFileId line from info.ini  (otherwise their client would
#     try to update OUR item 3834463519 and get a permission error)
#   * copies docs\HANDOFF-README.txt next to the folder (outside, so it is never uploaded)
#   * zips the result and prints SHA256 + size
param(
    [string]$PackagePath = (Join-Path $PSScriptRoot '..\artifacts\package\DuckovSharedBike'),
    [string]$OutputDir   = (Join-Path $PSScriptRoot '..\artifacts\handoff'),
    [string]$GuidePath   = (Join-Path $PSScriptRoot '..\docs\HANDOFF-README.txt')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$message) { throw "handoff: $message" }

$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $PackagePath)) { Fail "package folder not found: $PackagePath" }
if (-not (Test-Path -LiteralPath $GuidePath))   { Fail "guide not found: $GuidePath" }

$infoPath = Join-Path $PackagePath 'info.ini'
if (-not (Test-Path -LiteralPath $infoPath)) { Fail "info.ini not found in $PackagePath" }

$utf8 = New-Object System.Text.UTF8Encoding($false)
$iniText = [IO.File]::ReadAllText($infoPath, $utf8)
$version = [regex]::Match($iniText, '(?m)^[ \t]*version[ \t]*=[ \t]*(\S+)[ \t]*$').Groups[1].Value
if (-not $version) { Fail 'cannot read version from info.ini' }

$name = "DuckovSharedBike-HANDOFF-$version"
$stageRoot = [IO.Path]::GetFullPath($OutputDir)
$stage = Join-Path $stageRoot $name
$modDir = Join-Path $stage 'DuckovSharedBike'
$zipPath = Join-Path $stageRoot "$name.zip"

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Path $modDir -Force | Out-Null

# 1. copy payload, skipping symbols and any leftover logs
$removed = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $PackagePath -File | ForEach-Object {
    if ($_.Extension -ieq '.pdb') { $removed.Add($_.Name); return }
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modDir $_.Name)
}
$staleDirs = Get-ChildItem -LiteralPath $PackagePath -Directory | Where-Object { $_.Name -ieq 'logs' }
foreach ($dir in $staleDirs) { $removed.Add($dir.Name + ' (folder)') }

# 2. strip publishedFileId so the helper creates a brand-new item
$targetInfo = Join-Path $modDir 'info.ini'
$text = [IO.File]::ReadAllText($targetInfo, $utf8)
$stripped = [regex]::Replace($text, '(?im)^\s*publishedFileId\s*=.*(\r?\n)?', '')
if ($stripped -ne $text) {
    [IO.File]::WriteAllText($targetInfo, $stripped, $utf8)
    Write-Output 'Stripped publishedFileId from info.ini (helper will create a new item).'
} else {
    Write-Output 'No publishedFileId in info.ini (already clean).'
}

# 3. guide outside the mod folder so it never gets uploaded
Copy-Item -LiteralPath $GuidePath -Destination (Join-Path $stage 'HANDOFF-README.txt')

# 4. verify
$files = @(Get-ChildItem -LiteralPath $modDir -File | Sort-Object Name)
Write-Output ''
Write-Output "Mod folder : $modDir"
Write-Output ("Files      : {0}" -f $files.Count)
foreach ($file in $files) {
    Write-Output ("  {0,-26} {1,9:N0} B" -f $file.Name, $file.Length)
}
if ($removed.Count -gt 0) { Write-Output ("Dropped    : " + ($removed -join ', ')) }

$checkText = [IO.File]::ReadAllText($targetInfo, $utf8)
if ($checkText -match '(?im)^\s*publishedFileId\s*=') { Fail 'publishedFileId still present in the copied info.ini' }
$checkVersion = [regex]::Match($checkText, '(?m)^[ \t]*version[ \t]*=[ \t]*(\S+)[ \t]*$').Groups[1].Value
if ($checkVersion -ne $version) { Fail "version mismatch in copied info.ini ($checkVersion vs $version)" }
if (-not (Test-Path -LiteralPath (Join-Path $modDir 'DuckovSharedBike.dll'))) { Fail 'DuckovSharedBike.dll missing from payload' }
if (-not (Test-Path -LiteralPath (Join-Path $modDir 'marker.png'))) { Fail 'marker.png missing from payload' }

# 5. zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)

$zip = Get-Item -LiteralPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Output ''
Write-Output "Package    : $zipPath"
Write-Output ("Size       : {0:N0} B ({1:N2} MB)" -f $zip.Length, ($zip.Length / 1MB))
Write-Output "SHA256     : $hash"
Write-Output ''
Write-Output "Send this zip to the helper together with docs\HANDOFF-README.txt (already inside)."
Write-Output "After they upload, ask for the item URL and run: scripts\Sync-WorkshopId.ps1 -ItemId <id>"
