[CmdletBinding()]
param(
    [ValidateSet('Quick', 'Full')][string]$Mode = 'Quick',
    [string]$Filter,
    [switch]$IncludeFfmpeg,
    [switch]$IncludeDesktop,
    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'quality-common.ps1')
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$results = Join-Path $repo ('TestResults\quality\' + $runId)
New-Item -ItemType Directory -Path $results -Force | Out-Null
$script:steps = New-Object 'Collections.Generic.List[object]'
$script:testRuns = New-Object 'Collections.Generic.List[object]'
$script:coverage = $null
$started = [DateTime]::UtcNow
$script:currentLog = $null
$exitCode = 1
$shell = (Get-Process -Id $PID).Path
$testProject = Join-Path $repo 'tests\KeepClip.Tests\KeepClip.Tests.csproj'

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    $script:currentLog = Join-Path $results ($Name + '.log')
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Write-Host "`n[$Name]" -ForegroundColor Cyan
    try {
        & $Action
        $script:steps.Add([pscustomobject]@{ name = $Name; status = 'passed'; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 2); detail = ''; log = $script:currentLog })
        return $true
    } catch {
        $detail = $_.Exception.Message
        Add-Content -LiteralPath $script:currentLog -Value $detail
        Write-Host $detail -ForegroundColor Red
        $script:steps.Add([pscustomobject]@{ name = $Name; status = 'failed'; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 2); detail = $detail; log = $script:currentLog })
        return $false
    }
}

function Skip-Step([string]$Name, [string]$Reason, [switch]$Blocked) {
    $status = if ($Blocked) { 'blocked' } else { 'not-requested' }
    $script:steps.Add([pscustomobject]@{ name = $Name; status = $status; seconds = 0; detail = $Reason; log = $null })
    Write-Host "[$Name] $status`: $Reason"
}

function Run([string]$Program, [string[]]$Arguments) {
    Invoke-QualityCommand -FilePath $Program -Arguments $Arguments -LogPath $script:currentLog
}

function Test-Variant([string]$Name, [string]$Artifacts, [string[]]$Properties, [string]$TestFilter, [switch]$CollectCoverage) {
    $directory = Join-Path $results $Name
    $settings = Join-Path $repo $(if ($CollectCoverage) { 'tests\quality.coverage.runsettings' } else { 'tests\quality.runsettings' })
    $arguments = @('test', $testProject, '-c', 'Release', '--no-build', '--no-restore', '--artifacts-path', $Artifacts, '--nologo', '--verbosity', 'minimal', '--settings', $settings, '--results-directory', $directory, '--logger', 'trx;LogFileName=results.trx', '--blame-hang-timeout', '2m', '--blame-hang-dump-type', 'none') + $Properties
    if ($TestFilter) { $arguments += @('--filter', $TestFilter) }
    if ($CollectCoverage) { $arguments += @('--collect', 'XPlat Code Coverage') }
    Run 'dotnet' $arguments
    $result = Read-QualityTestResult (Join-Path $directory 'results.trx')
    $script:testRuns.Add([pscustomobject]@{ name = $Name; total = $result.total; passed = $result.passed; path = $result.path })
}

Push-Location $repo
try {
    $ready = Invoke-Step 'prerequisites' {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'KeepClip QA requires Windows and the .NET Windows Desktop runtime.' }
        if ($Mode -eq 'Full' -and $Filter) { throw '-Filter is supported only in Quick mode. Full always runs the complete headless suite.' }
        foreach ($command in @('dotnet', 'node')) { $null = Get-Command $command -ErrorAction Stop }
        foreach ($path in @('KeepClip\packages.lock.json', 'tests\KeepClip.Tests\packages.lock.json')) {
            if (-not (Test-Path -LiteralPath (Join-Path $repo $path) -PathType Leaf)) { throw "Missing $path. Restore reviewed dependencies with --use-lock-file before running QA." }
        }
        Run 'dotnet' @('--version')
        Run 'node' @('--version')
    }
    $properties = @('-p:QualityCheck=true', '-p:RunFfmpegTests=false', '-p:RunDesktopTests=false', '-p:PublicRelease=false')
    $artifacts = Join-Path $results 'build\release'
    $restore = $false
    $build = $false
    if ($ready) {
        $restore = Invoke-Step 'restore-audit' {
            $arguments = @('restore', 'KeepClip.slnx', '--locked-mode', '--force', '--artifacts-path', $artifacts, '--nologo', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low', '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905') + $properties
            if ($Mode -eq 'Full') { $arguments += '--no-http-cache' }
            Run 'dotnet' $arguments
        }
    } else { Skip-Step 'restore-audit' 'Prerequisites failed.' -Blocked }
    if ($restore) {
        $build = Invoke-Step 'build-release-analyzers' { Run 'dotnet' (@('build', 'KeepClip.slnx', '-c', 'Release', '--no-restore', '--artifacts-path', $artifacts, '--nologo', '--verbosity', 'minimal', '--disable-build-servers', '-warnaserror') + $properties) }
    } else { Skip-Step 'build-release-analyzers' 'Restore or dependency audit failed.' -Blocked }
    if ($build) {
        $null = Invoke-Step 'test-release' { Test-Variant 'release-tests' $artifacts $properties $Filter -CollectCoverage:($Mode -eq 'Full') }
    } else { Skip-Step 'test-release' 'Release build failed.' -Blocked }
    if ($Mode -eq 'Full') {
        if ($build) {
            $null = Invoke-Step 'coverage-report' {
                $coveragePath = Find-QualityCoverage (Join-Path $results 'release-tests')
                $script:coverage = Write-QualityCoverage $coveragePath $results
                Write-Host "KeepClip line coverage: $($script:coverage.linePercent)% ($($script:coverage.coveredLines)/$($script:coverage.totalLines))."
            }
        } else { Skip-Step 'coverage-report' 'Release build failed.' -Blocked }
        if ($restore) {
            $null = Invoke-Step 'dependency-license-consistency' { Run $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'check-dependencies.ps1'), '-AssetsFile', (Join-Path $artifacts 'obj\KeepClip\project.assets.json')) }
        } else { Skip-Step 'dependency-license-consistency' 'Restore failed.' -Blocked }
        $publicProperties = @('-p:QualityCheck=true', '-p:RunFfmpegTests=false', '-p:RunDesktopTests=false', '-p:PublicRelease=true')
        $publicArtifacts = Join-Path $results 'build\public'
        $publicRestore = $false
        if ($ready -and $restore) {
            $publicRestore = Invoke-Step 'restore-public' { Run 'dotnet' (@('restore', 'KeepClip.slnx', '--locked-mode', '--artifacts-path', $publicArtifacts, '--nologo', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low', '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905') + $publicProperties) }
        } else { Skip-Step 'restore-public' 'Prerequisites or dependency audit failed.' -Blocked }
        $publicBuild = $false
        if ($publicRestore) {
            $publicBuild = Invoke-Step 'build-public-analyzers' { Run 'dotnet' (@('build', 'KeepClip.slnx', '-c', 'Release', '--no-restore', '--artifacts-path', $publicArtifacts, '--nologo', '--verbosity', 'minimal', '--disable-build-servers', '-warnaserror') + $publicProperties) }
        } else { Skip-Step 'build-public-analyzers' 'PublicRelease restore failed.' -Blocked }
        if ($publicBuild) {
            $null = Invoke-Step 'test-public' { Test-Variant 'public-tests' $publicArtifacts $publicProperties '' }
        } else { Skip-Step 'test-public' 'PublicRelease build failed.' -Blocked }
    }
    $null = Invoke-Step 'frontend-translations' { Run 'node' @('tests/i18n-check.js') }
    $null = Invoke-Step 'node-tests' {
        $nodeTests = @(Get-ChildItem -LiteralPath (Join-Path $repo 'tests') -File | Where-Object { $_.Name -match '\.test\.(mjs|cjs|js)$' } | Sort-Object Name | ForEach-Object { $_.FullName })
        if ($nodeTests.Count -eq 0) { throw 'No Node test files were found.' }
        Run 'node' (@('--test', '--test-reporter=spec', '--test-reporter-destination=stdout', '--test-reporter=junit', ('--test-reporter-destination=' + (Join-Path $results 'node-tests.xml'))) + $nodeTests)
    }
    if ($Mode -eq 'Full') {
        $null = Invoke-Step 'oauth-packaging' { Run $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', 'tests/oauth-packaging.Tests.ps1') }
        $null = Invoke-Step 'qa-harness-tests' { Run $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', 'tests/quality-runner.Tests.ps1') }
        $null = Invoke-Step 'qa-report-tests' { Run $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', 'tests/quality-report.Tests.ps1') }
        $null = Invoke-Step 'qa-recording-tools-tests' { Run $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', 'tests/recording-benchmark.Tests.ps1') }
    }
    foreach ($category in @('FFmpeg', 'Desktop')) {
        $requested = if ($category -eq 'FFmpeg') { $IncludeFfmpeg } else { $IncludeDesktop }
        if (-not $requested) { Skip-Step ($category.ToLowerInvariant() + '-tests') "Use -Include$category to enable native integration tests."; continue }
        if (-not $ready -or -not $restore) { Skip-Step ($category.ToLowerInvariant() + '-tests') 'Prerequisites or restore failed.' -Blocked; continue }
        $null = Invoke-Step ($category.ToLowerInvariant() + '-tests') {
            foreach ($tool in @('ffmpeg.exe', 'ffprobe.exe')) {
                if (-not (Test-Path -LiteralPath (Join-Path $repo ('tools\bin\' + $tool)) -PathType Leaf)) { throw "Missing tools/bin/$tool. Native tests do not download or replace tools." }
            }
            $nativeArtifacts = Join-Path $results ('build\' + $category.ToLowerInvariant())
            $nativeProperties = @('-p:QualityCheck=true', '-p:PublicRelease=false', ('-p:RunFfmpegTests=' + ($category -eq 'FFmpeg').ToString().ToLowerInvariant()), ('-p:RunDesktopTests=' + ($category -eq 'Desktop').ToString().ToLowerInvariant()))
            Run 'dotnet' (@('restore', 'KeepClip.slnx', '--locked-mode', '--artifacts-path', $nativeArtifacts, '--nologo', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low', '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905') + $nativeProperties)
            Run 'dotnet' (@('build', 'KeepClip.slnx', '-c', 'Release', '--no-restore', '--artifacts-path', $nativeArtifacts, '--nologo', '--verbosity', 'minimal', '--disable-build-servers', '-warnaserror') + $nativeProperties)
            Test-Variant ($category.ToLowerInvariant() + '-tests') $nativeArtifacts $nativeProperties ('Category=' + $category)
        }
    }
    Skip-Step 'installer-and-hardware' 'Installer staging/signing/publishing, GPU capture, Whisper model downloads and live cloud accounts are outside this safe QA run; release.yml retains packaging checks.'
    $exitCode = if (@($script:steps | Where-Object { $_.status -in @('failed', 'blocked') }).Count -eq 0) { 0 } else { 1 }
} catch {
    $script:steps.Add([pscustomobject]@{ name = 'runner'; status = 'failed'; seconds = 0; detail = $_.Exception.Message; log = $null })
    Write-Host $_.Exception.Message -ForegroundColor Red
    $exitCode = 1
} finally {
    Pop-Location
    $report = [pscustomobject]@{ runId = $runId; mode = $Mode; filter = $Filter; startedUtc = $started.ToString('o'); finishedUtc = [DateTime]::UtcNow.ToString('o'); exitCode = $exitCode; steps = @($script:steps.ToArray()); tests = @($script:testRuns.ToArray()); coverage = $script:coverage }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding UTF8
    try {
        $null = & (Join-Path $PSScriptRoot 'quality-report.ps1') -ResultsDirectory $results
    } catch {
        $exitCode = 1
        $script:steps.Add([pscustomobject]@{ name = 'visual-report'; status = 'failed'; seconds = 0; detail = $_.Exception.Message; log = $null })
        $report.exitCode = $exitCode
        $report.steps = @($script:steps.ToArray())
        $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding UTF8
        Write-Host "Visual report failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $markdown = @('# KeepClip quality check', '', "Result: **$status**. Mode: **$Mode**. Run: $runId.", '', '| Check | Result | Seconds | Detail |', '| --- | --- | ---: | --- |')
    foreach ($step in $script:steps) { $markdown += "| $($step.name) | $($step.status) | $($step.seconds) | $($step.detail.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')) |" }
    $markdown += @('', 'Artifacts: report.html (interactive results), repair-prompt.txt, report-data.json, per-step logs, TRX, Node JUnit XML and (Full) coverage.html, coverage-summary.md/json and Cobertura XML. Build output is isolated in this run directory.', '', 'Not-requested checks are explicitly outside this run and are not reported as passed.')
    $markdown | Set-Content -LiteralPath (Join-Path $results 'summary.md') -Encoding UTF8
    if ($env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8 }
    Write-Host "`nQA $status ($Mode). Report: $results\summary.md"
    if ($OpenReport) {
        try { & (Join-Path $PSScriptRoot 'open-quality-report.ps1') -ResultsDirectory $results }
        catch { Write-Warning "Could not open the report automatically: $($_.Exception.Message)" }
    }
}
exit $exitCode
