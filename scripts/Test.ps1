$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
& dotnet run --project (Join-Path $projectRoot 'tests\SharedBike.Domain.Tests\SharedBike.Domain.Tests.csproj') --configuration Release --verbosity quiet -- --report (Join-Path $projectRoot 'artifacts\verification\domain-tests.json')
if ($LASTEXITCODE -ne 0) { throw 'Rental business-rule tests failed.' }
