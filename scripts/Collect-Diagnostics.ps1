param(
    [string]$GamePath = 'E:\Steam\steamapps\common\Escape from Duckov'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$modFolder = Join-Path $GamePath 'Duckov_Data\Mods\DuckovSharedBike'
if (-not (Test-Path -LiteralPath (Join-Path $modFolder 'info.ini') -PathType Leaf) -or
    -not (Select-String -LiteralPath (Join-Path $modFolder 'info.ini') -Pattern '^name\s*=\s*DuckovSharedBike\s*$' -Quiet)) {
    throw 'Shared Bike is not installed at the supplied game path.'
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$diagnosticsRoot = Join-Path $projectRoot 'artifacts\diagnostics'
$folder = Join-Path $diagnosticsRoot $stamp
New-Item -ItemType Directory -Path $folder -Force | Out-Null
foreach ($name in @('info.ini','prototype.json','station-layout.json','logs\bootstrap.log')) {
    $source = Join-Path $modFolder $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $folder ([System.IO.Path]::GetFileName($name)))
    }
}
@'
共享单车问题记录
地图：
操作：
预期：
实际现象：
是否重复发生：
其他同时启用的模组：
请填写以上内容，再把压缩包与截图（如有）用于反馈。
本包仅收集共享单车配置和模组日志，没有游戏存档。
'@ | Set-Content -LiteralPath (Join-Path $folder '问题描述.txt') -Encoding utf8
$archive = Join-Path $diagnosticsRoot ("SharedBike-diagnostics-$stamp.zip")
Compress-Archive -LiteralPath $folder -DestinationPath $archive
Write-Host "Diagnostic package: $archive"
