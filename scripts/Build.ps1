param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\DuckovSharedBike\DuckovSharedBike.csproj'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'

& dotnet build $project --configuration Release --nologo --verbosity minimal "-p:DuckovPath=$GamePath"
if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed.' }

$projectVersion = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$infoVersion = ((Get-Content -LiteralPath (Join-Path $projectRoot 'mod\info.ini') -Encoding UTF8 | Where-Object { $_ -match '^version=' }) -split '=',2)[1].Trim()
if ($projectVersion -ne $infoVersion) { throw 'Project and info.ini versions do not match.' }
$preview = Join-Path $projectRoot 'mod\preview.png'
& (Join-Path $PSScriptRoot 'Make-Preview.ps1')

$buildOutput = Join-Path $projectRoot 'src\DuckovSharedBike\bin\Release\netstandard2.1'
$packageParent = Join-Path $projectRoot 'artifacts\package'
$package = Join-Path $packageParent 'DuckovSharedBike'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$files = @(
    @{ Source = (Join-Path $buildOutput 'DuckovSharedBike.dll'); Name = 'DuckovSharedBike.dll' },
    @{ Source = (Join-Path $buildOutput 'DuckovSharedBike.pdb'); Name = 'DuckovSharedBike.pdb' },
    @{ Source = (Join-Path $projectRoot 'mod\info.ini'); Name = 'info.ini' },
    @{ Source = $preview; Name = 'preview.png' },
    @{ Source = (Join-Path $projectRoot 'mod\prototype.json'); Name = 'prototype.json' },
    @{ Source = (Join-Path $projectRoot 'mod\station-layout.json'); Name = 'station-layout.json' },
    @{ Source = (Join-Path $projectRoot 'mod\README.txt'); Name = 'README.txt' },
    @{ Source = (Join-Path $projectRoot 'vendor\Harmony\0Harmony.dll'); Name = '0Harmony.dll' },
    @{ Source = (Join-Path $projectRoot 'vendor\Harmony\LICENSE.txt'); Name = 'LICENSE-Harmony.txt' },
    @{ Source = (Join-Path $projectRoot 'vendor\Harmony\PROVENANCE.md'); Name = 'THIRD-PARTY.md' }
)
foreach ($file in $files) {
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $package $file.Name) -Force
}

$unexpected = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -notin $files.Name })
if ($unexpected.Count -gt 0) { throw 'Package contains unexpected files; inspect it before distributing.' }
$dlls = @(Get-ChildItem -LiteralPath $package -Filter '*.dll' -File)
if ($dlls.Count -ne 2 -or @($dlls | Where-Object { $_.Name -notin @('DuckovSharedBike.dll','0Harmony.dll') }).Count -ne 0) {
    throw 'Package must contain only our mod and Harmony DLLs, not game libraries.'
}
$archive = Join-Path $projectRoot ("artifacts\DuckovSharedBike-$projectVersion.zip")
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
Write-Host "Package: $package"
Write-Host "Archive: $archive"
