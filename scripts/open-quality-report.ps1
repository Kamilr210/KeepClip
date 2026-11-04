[CmdletBinding()]
param([string]$ResultsDirectory)

$ErrorActionPreference = 'Stop'
$qualityRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults\quality'))
if (-not $ResultsDirectory) {
    if (-not (Test-Path -LiteralPath $qualityRoot -PathType Container)) { throw 'No QA results yet. Run Testy.cmd first.' }
    $latest = Get-ChildItem -LiteralPath $qualityRoot -Directory | Sort-Object CreationTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'No QA results yet. Run Testy.cmd first.' }
    $ResultsDirectory = $latest.FullName
}
$directory = [IO.Path]::GetFullPath($ResultsDirectory)
if (-not $directory.StartsWith($qualityRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The report must be inside this repository TestResults/quality directory.' }
$reportPath = Join-Path $directory 'report.html'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "The latest run has no HTML report yet. It may still be running or report generation failed. Results: $directory" }
Start-Process -FilePath $reportPath
