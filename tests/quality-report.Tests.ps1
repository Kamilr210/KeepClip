[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$generator = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\quality-report.ps1'
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporary ('keepclip-report-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$script:passed = 0

function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:passed++
}

function New-Case([string]$Name, [int]$ExitCode, [object[]]$Steps) {
    $directory = Join-Path $fixture $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $summary = @{ runId = $Name; mode = 'Quick'; filter = ''; startedUtc = '2026-10-29T12:00:00Z'; finishedUtc = '2026-10-29T12:00:05Z'; exitCode = $ExitCode; steps = $Steps; tests = @(); coverage = $null }
    [IO.File]::WriteAllText((Join-Path $directory 'summary.json'), ($summary | ConvertTo-Json -Depth 8), $utf8)
    return $directory
}

function Write-TestFile([string]$Directory, [string]$Relative, [string]$Content) {
    $path = Join-Path $Directory $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, $Content, $utf8)
}

function Generate([string]$Directory, [switch]$ExpectIssue) {
    $caught = $false
    try { $null = & $generator -ResultsDirectory $Directory } catch { $caught = $true; if (-not $ExpectIssue) { throw } }
    Check ($caught -eq [bool]$ExpectIssue) 'Generator did not distinguish incomplete artifacts from a normal failed QA run.'
    foreach ($name in @('report.html', 'report-data.json', 'repair-prompt.txt')) { Check (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf) ('Missing report output: ' + $name) }
    return ([IO.File]::ReadAllText((Join-Path $Directory 'report-data.json')) | ConvertFrom-Json)
}

function Trx([string]$Results, [int]$Total) {
    [xml]$resultsXml = '<Results>' + $Results + '</Results>'
    $outcomes = @($resultsXml.Results.ChildNodes | ForEach-Object { $_.GetAttribute('outcome') })
    $passed = @($outcomes | Where-Object { $_ -eq 'Passed' }).Count
    $failed = @($outcomes | Where-Object { $_ -eq 'Failed' }).Count
    $skipped = @($outcomes | Where-Object { $_ -eq 'NotExecuted' }).Count
    $executed = $outcomes.Count - $skipped
    $outcome = if ($failed) { 'Failed' } else { 'Completed' }
    return '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>' + $Results + '</Results><TestDefinitions><UnitTest id="a"><TestMethod className="KeepClip.Tests.ScannerTests"/></UnitTest></TestDefinitions><ResultSummary outcome="' + $outcome + '"><Counters total="' + $Total + '" executed="' + $executed + '" passed="' + $passed + '" failed="' + $failed + '" notExecuted="' + $skipped + '"/></ResultSummary></TestRun>'
}

try {
    $case = New-Case 'mixed-trx' 1 @(@{ name='test-release'; status='failed'; seconds=2; detail='A test failed'; log='test-release.log' })
    Write-TestFile $case 'test-release.log' 'Synthetic run output'
    $results = '<UnitTestResult testId="a" testName="Keeps files" outcome="Passed" duration="00:00:00.125"/><UnitTestResult testId="b" testName="Rejects &lt;/script&gt;&lt;img src=x onerror=alert(1)&gt;" outcome="Failed" duration="00:00:00.25"><Output><ErrorInfo><Message>Expected unchanged file &amp; got removal</Message><StackTrace>at Test() in C:\synthetic\ScannerTests.cs:line 42</StackTrace></ErrorInfo></Output></UnitTestResult><UnitTestResult testId="c" testName="Unavailable hardware" outcome="NotExecuted"/> '
    Write-TestFile $case 'release-tests\results.trx' (Trx $results 3)
    $model = Generate $case
    Check ($model.exitCode -eq 1 -and $model.overallStatus -eq 'failed') 'Failed QA was presented as passed.'
    Check ($model.totals.total -eq 3 -and $model.totals.passed -eq 1 -and $model.totals.failed -eq 1 -and $model.totals.skipped -eq 1) 'Individual TRX outcomes were lost.'
    $failed = @($model.tests | Where-Object status -eq 'failed')[0]
    Check ($failed.sourceFile -eq 'C:\synthetic\ScannerTests.cs' -and $failed.sourceLine -eq 42) 'The actual stack-trace location was not extracted.'
    Check ($model.tests[0].module -eq 'KeepClip.Tests.ScannerTests' -and $model.tests[0].durationSeconds -eq 0.125) 'TRX class or invariant duration was lost.'
    Check ($model.repairPrompt -match 'ScannerTests.cs:42' -and $model.repairPrompt -match 'Expected unchanged file' -and $model.repairPrompt -notmatch 'Test: Release / Keeps files') 'Repair prompt omitted failure evidence or included passing test noise.'
    $html = [IO.File]::ReadAllText((Join-Path $case 'report.html'))
    Check (-not $html.Contains('</script><img src=x onerror=alert(1)>') -and $html.Contains('\u003c/script\u003e')) 'A test name can break out of embedded report JSON.'
    Check ($model.steps[0].log -eq 'test-release.log' -and $model.steps[0].logText -eq 'Synthetic run output') 'Log links or embedded output are not portable.'

    $case = New-Case 'node-results' 1 @(@{ name='node-tests'; status='failed'; seconds=1; detail='Node failed'; log=$null })
    Write-TestFile $case 'node-tests.xml' '<testsuites><testcase name="direct test" classname="test" time="0.005"/><testsuite name="nested"><testcase name="failed node" classname="test" time="0.1"><failure message="wrong value">at check (file:///C:/synthetic/node.test.mjs:12:3)</failure></testcase><testcase name="skipped node"><skipped message="fixture reason"/></testcase></testsuite></testsuites>'
    $model = Generate $case
    Check ($model.totals.total -eq 3 -and $model.totals.failed -eq 1 -and $model.totals.skipped -eq 1) 'Direct and nested Node JUnit testcases were not both parsed.'
    $failed = @($model.tests | Where-Object status -eq 'failed')[0]
    Check ($failed.sourceLine -eq 12 -and $failed.sourceFile -match 'synthetic[\\/]node.test.mjs$') 'Node stack-trace file URI was not decoded.'
    Check ([string]::IsNullOrEmpty($model.tests[0].sourceFile) -and $null -eq $model.tests[0].sourceLine) 'A source location was invented for a passing test.'

    $case = New-Case 'green' 0 @(@{ name='test-release'; status='passed'; seconds=1; detail=''; log=$null })
    Write-TestFile $case 'release-tests\results.trx' (Trx '<UnitTestResult testId="a" testName="green test" outcome="Passed"/>' 1)
    $model = Generate $case
    Check ($model.overallStatus -eq 'passed' -and $model.issues.Count -eq 0) 'A complete passing run was not shown as passed.'
    Check ($model.repairPrompt -notmatch 'Napraw wykryte' -and $model.totals.passed -eq 1) 'Passing run requests unnecessary fixes.'

    $case = New-Case 'missing-trx' 0 @(@{ name='test-release'; status='passed'; seconds=1; detail=''; log=$null })
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'incomplete' -and $model.issues.Count -eq 1 -and $model.exitCode -eq 0) 'Missing TRX was hidden or the original QA exit code was rewritten.'

    $case = New-Case 'malformed-trx' 1 @(@{ name='test-release'; status='failed'; seconds=1; detail='No complete results'; log=$null })
    Write-TestFile $case 'release-tests\results.trx' '<TestRun'
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'failed' -and $model.issues.Count -gt 0) 'Malformed TRX was silently accepted.'

    $case = New-Case 'mismatched-counters' 0 @(@{ name='test-release'; status='passed'; seconds=1; detail=''; log=$null })
    Write-TestFile $case 'release-tests\results.trx' (Trx '<UnitTestResult testName="partial result" outcome="Passed"/>' 2)
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'incomplete' -and $model.totals.total -eq 1) 'Partial TRX was presented as a complete pass.'

    $greenTrx = Trx '<UnitTestResult testName="green test" outcome="Passed"/>' 1
    foreach ($contradiction in @('executed', 'passed', 'failed', 'aborted-outcome', 'failed-outcome')) {
        $case = New-Case ('contradictory-' + $contradiction) 0 @(@{ name='test-release'; status='passed'; seconds=1; detail=''; log=$null })
        $trx = switch ($contradiction) {
            'executed' { $greenTrx.Replace('executed="1"', 'executed="0"') }
            'passed' { $greenTrx.Replace('passed="1"', 'passed="0"') }
            'failed' { $greenTrx.Replace('failed="0"', 'failed="1"') }
            'aborted-outcome' { $greenTrx.Replace('outcome="Completed"', 'outcome="Aborted"') }
            'failed-outcome' { $greenTrx.Replace('outcome="Completed"', 'outcome="Failed"') }
        }
        Write-TestFile $case 'release-tests\results.trx' $trx
        $model = Generate $case -ExpectIssue
        Check ($model.overallStatus -eq 'incomplete' -and $model.repairPrompt -notmatch 'Nie wykryto bledow') ('Contradictory TRX was presented as success: ' + $contradiction)
    }

    $case = New-Case 'completed-with-failure' 1 @(@{ name='test-release'; status='failed'; seconds=1; detail='Test failed'; log=$null })
    $trx = (Trx '<UnitTestResult testName="failed test" outcome="Failed"/>' 1).Replace('outcome="Failed"><Counters', 'outcome="Completed"><Counters')
    Write-TestFile $case 'release-tests\results.trx' $trx
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'failed' -and $model.totals.failed -eq 1) 'Contradictory run outcome hid an actual failing test.'

    $case = New-Case 'empty-steps' 0 @()
    Write-TestFile $case 'release-tests\results.trx' $greenTrx
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'incomplete' -and $model.repairPrompt -notmatch 'Nie wykryto bledow') 'A summary with no steps produced a success prompt.'

    $case = New-Case 'unknown-step-status' 0 @(@{ name='test-release'; status='mystery'; seconds=1; detail=''; log=$null })
    Write-TestFile $case 'release-tests\results.trx' $greenTrx
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -eq 'incomplete' -and $model.repairPrompt -notmatch 'Nie wykryto bledow') 'An unknown step status produced a success prompt.'

    $case = New-Case 'compiler-failure' 1 @(@{ name='build-release-analyzers'; status='failed'; seconds=1; detail='Compiler failed'; log='compile.log' }, @{ name='test-release'; status='blocked'; seconds=0; detail='Build failed'; log=$null })
    Write-TestFile $case 'compile.log' 'C:\synthetic\Worker.cs(17,4): error CS4014: Because this call is not awaited'
    $model = Generate $case
    Check ($model.overallStatus -eq 'failed' -and $model.totals.total -eq 0 -and $model.steps[0].locations[0].line -eq 17) 'Build failure or compiler location was hidden behind zero tests.'
    Check ($model.repairPrompt -match 'CS4014' -and $model.repairPrompt -match 'Worker.cs:17') 'Repair prompt is missing the compiler diagnostic.'

    Write-TestFile $fixture 'outside.log' 'PRIVATE-SYNTHETIC-MARKER'
    $case = New-Case 'outside-log' 1 @(@{ name='build-release-analyzers'; status='failed'; seconds=1; detail='Bad log path'; log=(Join-Path $fixture 'outside.log') })
    $model = Generate $case -ExpectIssue
    Check ($model.steps[0].logText -notmatch 'PRIVATE-SYNTHETIC-MARKER' -and $model.repairPrompt -notmatch 'PRIVATE-SYNTHETIC-MARKER') 'Report read an artifact outside its run directory.'

    $case = New-Case 'dtd-entity' 1 @(@{ name='test-release'; status='failed'; seconds=1; detail='Bad XML'; log=$null })
    Write-TestFile $case 'release-tests\results.trx' ('<!DOCTYPE TestRun [<!ENTITY private SYSTEM "' + (Join-Path $fixture 'outside.log') + '">]><TestRun>&private;</TestRun>')
    $model = Generate $case -ExpectIssue
    Check ($model.repairPrompt -notmatch 'PRIVATE-SYNTHETIC-MARKER' -and $model.issues.Count -gt 0) 'External XML entities were not rejected.'

    $case = New-Case 'broken-summary' 1 @()
    Write-TestFile $case 'summary.json' '{broken'
    $model = Generate $case -ExpectIssue
    Check ($model.overallStatus -ne 'passed' -and $model.issues.Count -gt 0) 'Invalid summary did not produce a diagnostic report.'

    Write-Host "PASS: $script:passed visual QA report checks (individual TRX/JUnit outcomes, failure evidence, incomplete artifacts, safe JSON and path/XML boundaries)."
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith($temporary.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}