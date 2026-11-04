[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'scripts\quality-common.ps1')
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporary ('keepclip-quality-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$shell = (Get-Process -Id $PID).Path
$script:passed = 0

function Expect-Failure([scriptblock]$Action) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    if (-not $failed) { throw 'Expected a failing QA check to be rejected.' }
    $script:passed++
}

function Write-Trx([int]$Total, [int]$Executed, [int]$Passed, [string]$Outcome = 'Completed') {
    [IO.File]::WriteAllText((Join-Path $fixture 'results.trx'), '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="' + $Outcome + '"><Counters total="' + $Total + '" executed="' + $Executed + '" passed="' + $Passed + '" /></ResultSummary></TestRun>')
}

try {
    $log = Join-Path $fixture 'command.log'
    Invoke-QualityCommand $shell @('-NoProfile', '-NonInteractive', '-Command', 'Write-Output ''synthetic success''; exit 0') $log
    if ((Get-Content -LiteralPath $log -Raw) -notmatch 'synthetic success') { throw 'Successful command output was not logged.' }
    $script:passed++
    Expect-Failure { Invoke-QualityCommand $shell @('-NoProfile', '-NonInteractive', '-Command', 'Write-Output ''synthetic failure''; exit 17') $log }
    if ((Get-Content -LiteralPath $log -Raw) -notmatch 'synthetic failure') { throw 'Failing command output was not logged.' }
    $script:passed++
    Expect-Failure { Invoke-QualityCommand (Join-Path $fixture 'missing.exe') @() $log }
    $corruptExecutable = Join-Path $fixture 'corrupt.exe'
    [IO.File]::WriteAllText($corruptExecutable, 'Synthetic invalid executable, not a program.')
    $LASTEXITCODE = 0
    Expect-Failure { Invoke-QualityCommand $corruptExecutable @() $log }
    Expect-Failure { Read-QualityTestResult (Join-Path $fixture 'missing.trx') }
    $trx = Join-Path $fixture 'results.trx'
    Write-Trx 2 2 2
    $result = Read-QualityTestResult $trx
    if ($result.total -ne 2 -or $result.passed -ne 2) { throw 'Passing test counters were misread.' }
    $script:passed++
    Write-Trx 0 0 0
    Expect-Failure { Read-QualityTestResult $trx }
    Write-Trx 2 2 1
    Expect-Failure { Read-QualityTestResult $trx }
    Write-Trx 2 1 1
    Expect-Failure { Read-QualityTestResult $trx }
    Write-Trx 2 2 2 'Failed'
    Expect-Failure { Read-QualityTestResult $trx }
    [IO.File]::WriteAllText($trx, '<TestRun />')
    Expect-Failure { Read-QualityTestResult $trx }
    [IO.File]::WriteAllText($trx, '<TestRun')
    Expect-Failure { Read-QualityTestResult $trx }

    $coverage = Join-Path $fixture 'coverage.cobertura.xml'
    Expect-Failure { Write-QualityCoverage $coverage $fixture }
    [IO.File]::WriteAllText($coverage, '<coverage lines-valid="0" lines-covered="0" />')
    Expect-Failure { Write-QualityCoverage $coverage $fixture }
    $validCoverage = '<coverage lines-valid="3" lines-covered="2" branches-valid="2" branches-covered="1"><packages><package name="KeepClip"><classes><class filename="Workers/Scanner.cs"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class><class filename="Workers/Scanner.cs"><lines><line number="1" hits="3"/></lines></class><class filename="Unsafe&lt;script&gt;.cs"><lines><line number="3" hits="1"/></lines></class></classes></package></packages></coverage>'
    [IO.File]::WriteAllText($coverage, $validCoverage)
    $report = Write-QualityCoverage $coverage $fixture
    $scanner = @($report.files | Where-Object { $_.file -eq 'Workers/Scanner.cs' })
    if ($report.linePercent -ne 66.67 -or $scanner.Count -ne 1 -or $scanner[0].total -ne 2 -or $scanner[0].covered -ne 1) { throw 'Coverage aggregation double-counted source lines or misread hits.' }
    $script:passed++
    $html = Get-Content -LiteralPath (Join-Path $fixture 'coverage.html') -Raw
    if ($html -match '<script>' -or $html -notmatch '&lt;script&gt;') { throw 'Coverage filenames were not HTML-escaped.' }
    $script:passed++
    foreach ($name in @('coverage-summary.md', 'coverage-summary.json', 'coverage.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $fixture $name) -PathType Leaf)) { throw "Missing coverage output $name." }
    }
    $script:passed++
    [IO.File]::WriteAllText($coverage, $validCoverage.Replace('name="KeepClip"', 'name="KeepClip.Tests"'))
    Expect-Failure { Write-QualityCoverage $coverage $fixture }
    $search = Join-Path $fixture 'attachments'
    New-Item -ItemType Directory -Path $search | Out-Null
    Expect-Failure { Find-QualityCoverage $search }
    $attachment = Join-Path $search 'coverage.cobertura.xml'
    $copyDirectory = Join-Path $search 'In\machine'
    New-Item -ItemType Directory -Path $copyDirectory -Force | Out-Null
    $copy = Join-Path $copyDirectory 'coverage.cobertura.xml'
    [IO.File]::WriteAllText($attachment, $validCoverage)
    [IO.File]::WriteAllText($copy, $validCoverage)
    if ((Find-QualityCoverage $search) -ne $attachment) { throw 'Identical VSTest attachment copies were not deduplicated.' }
    $script:passed++
    [IO.File]::WriteAllText($copy, $validCoverage.Replace('lines-covered="2"', 'lines-covered="1"'))
    Expect-Failure { Find-QualityCoverage $search }
    Write-Host "PASS: $script:passed QA harness checks (native exit codes, missing executables/results, zero/failed/skipped tests, malformed XML, coverage aggregation and HTML escaping)."
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith($temporary.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
