#Requires -Version 5.1
<#
.SYNOPSIS
  Compare the IL of one type between two builds of DuckovSharedBike.dll.

.DESCRIPTION
  Used to check a reconstructed source file against a known-good binary: dumps
  every method of the type with tools\AssemblyInspector, normalises the listing
  (branch targets, closure class numbering, parameter names) and reports
  methods that exist on only one side plus the first differing instructions.

.PARAMETER OldAssembly
  Reference build, e.g. artifacts\tmp\v1015\DuckovSharedBike\DuckovSharedBike.dll

.PARAMETER NewAssembly
  Build under test, e.g. src\DuckovSharedBike\bin\Release\netstandard2.1\DuckovSharedBike.dll

.PARAMETER TypeName
  Fully qualified type name to compare.

.PARAMETER ReportPath
  Optional file for the full report.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OldAssembly,
    [Parameter(Mandatory = $true)][string] $NewAssembly,
    [string] $TypeName = 'DuckovSharedBike.BikeWorldController',
    [string] $ReportPath = 'artifacts\verification\il-diff.txt'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$inspector = Join-Path $root 'tools\AssemblyInspector\AssemblyInspector.csproj'

function Get-IlDump {
    param([string] $Assembly, [string] $Type)

    $pattern = '^' + [regex]::Escape($Type) + '$'
    $lines = & dotnet run --project $inspector -c Release -- $Assembly $pattern --il '.*' 2>&1
    $methods = [ordered]@{}
    $current = $null
    foreach ($line in $lines) {
        $text = [string] $line
        if ($text -match '^\s*METHOD\s+(.+?)\s*$') {
            $current = $Matches[1]
            $methods[$current] = New-Object System.Collections.Generic.List[string]
            continue
        }
        if ($null -eq $current) { continue }
        if ($text -match '^\s*IL_[0-9a-fA-F]+:\s*(.*?)\s*$') {
            $methods[$current].Add($Matches[1])
        }
    }
    return $methods
}

function Get-NormalizedIl {
    param($Method)

    $out = New-Object System.Collections.Generic.List[string]
    foreach ($instruction in $Method) {
        $text = $instruction
        # Branch targets move when instructions are inserted; compare structure only.
        $text = [regex]::Replace($text, 'IL_[0-9a-fA-F]{4}', 'IL#')
        # Compiler-generated closure numbering shifts with unrelated edits.
        $text = [regex]::Replace($text, 'c__DisplayClass\d+_\d+', 'c__DisplayClass#')
        $text = [regex]::Replace($text, '(<[^>]+>)b__\d+_\d+', '$1b__#')
        # Cached lambda delegates: <>9__128_1 -> <>9__#
        $text = [regex]::Replace($text, '<>9__\d+_\d+', '<>9__#')
        $out.Add($text)
    }
    return , $out
}

$old = Get-IlDump -Assembly $OldAssembly -Type $TypeName
$new = Get-IlDump -Assembly $NewAssembly -Type $TypeName

$report = New-Object System.Collections.Generic.List[string]
$changed = New-Object System.Collections.Generic.List[string]
$onlyOld = New-Object System.Collections.Generic.List[string]
$onlyNew = New-Object System.Collections.Generic.List[string]

foreach ($name in $old.Keys) {
    if (-not $new.Contains($name)) { $onlyOld.Add($name); continue }
    $a = Get-NormalizedIl -Method $old[$name]
    $b = Get-NormalizedIl -Method $new[$name]
    if ($a.Count -eq $b.Count) {
        $same = $true
        for ($i = 0; $i -lt $a.Count; $i++) { if ($a[$i] -ne $b[$i]) { $same = $false; break } }
        if ($same) { continue }
    }
    $changed.Add($name)
    $report.Add('=== CHANGED: ' + $name + (' (old {0} instrs, new {1} instrs)' -f $a.Count, $b.Count))
    $report.Add('  removed: ' + (($a | Where-Object { $b -notcontains $_ } | Select-Object -Unique | Select-Object -First 12) -join ' | '))
    $report.Add('  added:   ' + (($b | Where-Object { $a -notcontains $_ } | Select-Object -Unique | Select-Object -First 12) -join ' | '))
    $limit = [Math]::Max($a.Count, $b.Count)
    $shown = 0
    for ($i = 0; $i -lt $limit -and $shown -lt 12; $i++) {
        $left = if ($i -lt $a.Count) { $a[$i] } else { '<missing>' }
        $right = if ($i -lt $b.Count) { $b[$i] } else { '<missing>' }
        if ($left -ne $right) {
            $report.Add(('  - {0}' -f $left))
            $report.Add(('  + {0}' -f $right))
            $shown++
        }
    }
    $report.Add('')
}

foreach ($name in $new.Keys) {
    if (-not $old.Contains($name)) { $onlyNew.Add($name) }
}

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add(('Old: {0} ({1} methods)' -f $OldAssembly, $old.Count))
$summary.Add(('New: {0} ({1} methods)' -f $NewAssembly, $new.Count))
$summary.Add(('Changed: {0}; only in old: {1}; only in new: {2}' -f $changed.Count, $onlyOld.Count, $onlyNew.Count))
foreach ($name in $onlyOld) { $summary.Add('  ONLY OLD  ' + $name) }
foreach ($name in $onlyNew) { $summary.Add('  ONLY NEW  ' + $name) }
foreach ($name in $changed) { $summary.Add('  CHANGED   ' + $name) }

$directory = Split-Path -Parent $ReportPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
Set-Content -LiteralPath $ReportPath -Value ($summary + '' + $report) -Encoding UTF8

$summary | ForEach-Object { Write-Output $_ }
Write-Output ('Report: ' + $ReportPath)
