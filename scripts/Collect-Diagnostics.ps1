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

# 1.0.16 起日志写到 Unity 用户数据目录，避免污染创意工坊上传内容；旧版本仍写在模组目录。
$logCandidates = @()
$appInfo = Join-Path $GamePath 'Duckov_Data\app.info'
if (Test-Path -LiteralPath $appInfo -PathType Leaf) {
    $lines = @(Get-Content -LiteralPath $appInfo | Where-Object { $_.Trim() -ne '' })
    if ($lines.Count -ge 2) {
        $logCandidates += Join-Path (Join-Path (Join-Path (Join-Path $env:USERPROFILE 'AppData\LocalLow') $lines[0].Trim()) $lines[1].Trim()) 'DuckovSharedBike\logs\bootstrap.log'
    }
}
$logCandidates += Join-Path $modFolder 'logs\bootstrap.log'

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$diagnosticsRoot = Join-Path $projectRoot 'artifacts\diagnostics'
$folder = Join-Path $diagnosticsRoot $stamp
New-Item -ItemType Directory -Path $folder -Force | Out-Null
foreach ($name in @('info.ini', 'prototype.json', 'station-layout.json')) {
    $source = Join-Path $modFolder $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $folder ([System.IO.Path]::GetFileName($name)))
    }
}
$copiedLog = $false
foreach ($candidate in $logCandidates) {
    if ($copiedLog) { break }
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        Copy-Item -LiteralPath $candidate -Destination (Join-Path $folder 'bootstrap.log')
        $copiedLog = $true
    }
}
if (-not $copiedLog) {
    '未找到 bootstrap.log：模组尚未在本次安装中运行过，或文件日志被禁用。' |
        Set-Content -LiteralPath (Join-Path $folder 'bootstrap.log') -Encoding utf8
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
