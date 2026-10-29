param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $projectRoot 'artifacts\package\DuckovSharedBike'
$gameRoot = (Resolve-Path -LiteralPath $GamePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Duckov.exe'))) {
    throw 'This is not an Escape from Duckov installation.'
}
if (Get-Process -Name Duckov -ErrorAction SilentlyContinue) {
    throw 'Please close Escape from Duckov before installing the mod.'
}

$modsRoot = [System.IO.Path]::GetFullPath((Join-Path $gameRoot 'Duckov_Data\Mods'))
$destination = [System.IO.Path]::GetFullPath((Join-Path $modsRoot 'DuckovSharedBike'))
if (-not $destination.StartsWith($modsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid mod installation path.'
}
$names = @('DuckovSharedBike.dll','DuckovSharedBike.pdb','info.ini','preview.png','prototype.json','station-layout.json','README.txt','0Harmony.dll','LICENSE-Harmony.txt','THIRD-PARTY.md')
foreach ($name in $names) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "Package file missing: $name. Run scripts\Build.ps1 first."
    }
}

foreach ($path in @($modsRoot,$destination)) {
    if (Test-Path -LiteralPath $path) {
        $entry = Get-Item -LiteralPath $path
        if (-not $entry.PSIsContainer -or ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "Installation target must be a regular directory: $path"
        }
    }
}
if (Test-Path -LiteralPath $destination) {
    $oldInfo = Join-Path $destination 'info.ini'
    if (-not (Test-Path -LiteralPath $oldInfo) -or
        -not (Select-String -LiteralPath $oldInfo -Pattern '^name\s*=\s*DuckovSharedBike\s*$' -Quiet)) {
        throw 'Existing destination is not our mod. Refusing to overwrite it.'
    }
    $backup = Join-Path $projectRoot ('artifacts\backups\before-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    foreach ($name in $names) {
        $oldFile = Join-Path $destination $name
        if (Test-Path -LiteralPath $oldFile) { Copy-Item -LiteralPath $oldFile -Destination (Join-Path $backup $name) }
    }
    Write-Host "Previous mod files backed up to: $backup"
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in $names) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $destination $name) -Force
}
foreach ($name in $names) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath (Join-Path $destination $name) -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw "Installed file hash mismatch: $name" }
}
Write-Host "Installed and verified: $destination"
Write-Host 'Start the game, enable Shared Bike in the Mods menu; see the included README.txt for the first playtest.'
