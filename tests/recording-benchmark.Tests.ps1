[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\recording-benchmark-common.ps1')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\recording-sandbox-checks.ps1')
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporary ('keepclip-recording-benchmark-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$script:checks = 0

function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Reject([scriptblock]$Action) {
    $caught = $false
    try { $null = & $Action } catch { $caught = $true }
    Check $caught 'Unsafe or stale manifest was accepted.'
}

function Save-Manifest($Manifest) {
    $Manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture 'manifest.json') -Encoding UTF8
}

try {
    $board = [pscustomobject]@{ paintsPerSecond=64 }
    $vfr = Get-CaptureTargetSummary ([pscustomobject]@{ frameCount=320; duplicateFrames=0; droppedFrames=0 }) 320 60 10 $board
    Check ($vfr.noArtificialDuplicates -and -not $vfr.freshFrameTargetMet -and -not $vfr.passed) '320 samples without artificial duplicates cannot pass the 60 FPS check.'
    $legacy = Get-CaptureTargetSummary ([pscustomobject]@{ frameCount=600; duplicateFrames=280; droppedFrames=0 }) 600 60 10 $board
    Check ($legacy.newOutputFrames -eq 320 -and -not $legacy.noArtificialDuplicates -and -not $legacy.passed) 'CFR padding must not count as fresh frames.'
    $fast = Get-CaptureTargetSummary ([pscustomobject]@{ frameCount=600; duplicateFrames=0; droppedFrames=0 }) 600 60 10 $board
    Check $fast.passed 'A sufficiently fast scene delivering 600 unpadded frames should meet the ten-second 60 FPS diagnostic.'
    $media = @(2.02,1.98,2.01,1.99,1.0 | ForEach-Object { [pscustomobject]@{ media=@{ format=@{ duration=$_ } } } })
    Check (Get-ReplayTimingSummary $media 9).passed 'Regular two-second segments and a final second should preserve a nine-second clip.'
    $media[0].media.format.duration = 3.1
    $media[1].media.format.duration = 0.90
    $irregular = Get-ReplayTimingSummary $media 9
    Check (-not $irregular.passed -and $irregular.irregularSegments -eq 2) 'Compensating segment errors must fail even when the total duration is correct.'
    Check (-not (Get-ReplayTimingSummary @() 10).passed) 'An empty buffer cannot pass the timing check.'
    $phase = @{
        label='<script>alert("fixture")</script>'; fps=60; frameCount=320; baseline=$false
        target=$vfr; progress=@{ duplicateFrames=0 }; measurement=@{ cpuPercentSandboxCapacity=6 }
    }
    $replay = @{ segmentTiming=@{ passed=$true; seconds=@(2,2,2,2,2); totalSeconds=10 }; savedClip=@{ passed=$true; seconds=10; file='fixture.mp4'; frameTimes=@{count=320} } }
    $reportData = @{ mode='GdiVfrProbe'; status='failed'; durationSeconds=10; captures=@($phase); vfrReplay=$replay }
    $reportInput = Join-Path $fixture 'results.json'
    $reportFile = Join-Path $fixture 'report.html'
    $generateReport = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\recording-sandbox-report.ps1'
    [IO.File]::WriteAllText((Join-Path $fixture 'fixture.mp4'), 'link fixture')
    $reportData | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportInput -Encoding UTF8
    & $generateReport -ResultPath $reportInput
    $html = [IO.File]::ReadAllText($reportFile)
    Check ($html -match 'class="pass"' -and $html -match 'class="fail"' -and $html -match 'href="fixture.mp4"') 'The offline report lost the mixed outcome or the local clip.'
    Check ($html -notmatch '<script' -and $html -match '&lt;script&gt;' -and $html -match 'href="results.json"') 'The report injected phase markup or omitted the JSON.'
    $replay.savedClip.file = '../outside.mp4'
    $reportData | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportInput -Encoding UTF8
    & $generateReport -ResultPath $reportInput
    Check ([IO.File]::ReadAllText($reportFile) -notmatch 'outside\.mp4|href="fixture\.mp4"') 'The report linked outside its own clip directory.'
    $progress = Read-RecordingProgress "frame=2`nfps=2.0`nspeed=N/A`nprogress=continue`nframe=300`nfps=29.97`nout_time_us=10000000`nspeed=0.99x`ndup_frames=2`ndrop_frames=1`nprogress=end`n"
    Check ($progress.frames -eq 300 -and $progress.completed) 'Final progress did not replace earlier snapshots.'
    Check ($progress.reportedFps -eq 29.97 -and $progress.mediaSeconds -eq 10 -and $progress.speed -eq 0.99) 'Progress values were not parsed independently of locale.'
    Check ($progress.duplicatedFrames -eq 2 -and $progress.droppedFrames -eq 1) 'Duplicated or dropped frames were lost.'
    $unknown = Read-RecordingProgress "frame=N/A`nspeed=Infinity`nprogress=continue"
    Check ($null -eq $unknown.frames -and $null -eq $unknown.speed -and -not $unknown.completed) 'Unknown progress was presented as a numeric result.'
    Check ((Quote-RecordingArgument 'path with spaces') -eq '"path with spaces"') 'Spaces are not quoted.'
    Check ((Quote-RecordingArgument 'path\') -eq '"path\\"') 'Trailing backslash is not escaped before the quote.'
    Check ((Quote-RecordingArgument 'a"b') -eq '"a\"b"') 'Embedded quote is not escaped.'

    Check ((Get-RecordingOutputBytes $fixture) -eq 0) 'An empty probe or null-output baseline should have zero output bytes.'
    [IO.File]::WriteAllBytes((Join-Path $fixture 'seg_000000.ts'), [byte[]]@(1,2,3))
    [IO.File]::WriteAllBytes((Join-Path $fixture 'seg_000001.ts'), [byte[]]@(4,5))
    [IO.File]::WriteAllText((Join-Path $fixture 'unrelated.log'), 'not video output')
    Check ((Get-RecordingOutputBytes $fixture) -eq 5) 'Output bytes should include only generated video segments.'

    $source = Join-Path $fixture 'ReplayCaptureCommand.cs'
    [IO.File]::WriteAllText($source, 'Synthetic command source')
    $manifest = [pscustomobject]@{ schemaVersion=1; sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; profiles=@(
        [pscustomobject]@{ name='1080p30'; width=1920; height=1080; fps=30; quality='high'; encoders=@([pscustomobject]@{ name='libx264'; arguments=@('-c:v','libx264','-preset','veryfast','-crf','20','-g','30') }) },
        [pscustomobject]@{ name='1080p60'; width=1920; height=1080; fps=60; quality='high'; encoders=@([pscustomobject]@{ name='libx264'; arguments=@('-c:v','libx264','-preset','veryfast','-crf','20','-g','60') }) }
    ) }
    $manifestPath = Join-Path $fixture 'manifest.json'
    Save-Manifest $manifest
    Check ((Read-RecordingManifest $manifestPath $source).profiles.Count -eq 2) 'A valid bounded manifest was rejected.'
    $hash = $manifest.sourceSha256
    $manifest.sourceSha256 = 'stale'
    Save-Manifest $manifest
    Reject { Read-RecordingManifest $manifestPath $source }
    $manifest.sourceSha256 = $hash
    $original = $manifest.profiles[0].encoders[0].arguments
    $manifest.profiles[0].encoders[0].arguments = @('-c:v','libx264','-i','desktop','-g','30')
    Save-Manifest $manifest
    Reject { Read-RecordingManifest $manifestPath $source }
    $manifest.profiles[0].encoders[0].arguments = @('-c:v','libx264','-preset','veryfast','-g','60')
    Save-Manifest $manifest
    Reject { Read-RecordingManifest $manifestPath $source }
    $manifest.profiles[0].encoders[0].arguments = $original
    $manifest.profiles[0].width = 7680
    Save-Manifest $manifest
    Reject { Read-RecordingManifest $manifestPath $source }
    $manifest.profiles[0].width = 1920
    $manifest.profiles[0].encoders[0].arguments = @('-c:v','libx264','-preset','veryfast','-g','30','-g','30')
    Save-Manifest $manifest
    Reject { Read-RecordingManifest $manifestPath $source }

    $shell = (Get-Process -Id $PID).Path
    $command = "[Console]::WriteLine('frame=3'); [Console]::WriteLine('speed=1.0x'); [Console]::WriteLine('progress=end'); [Console]::Error.WriteLine('synthetic diagnostic'); exit 7"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $result = Invoke-RecordingProcess $shell @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) $fixture 10 4
    Check ($result.exitCode -eq 7 -and -not $result.timedOut) 'Nonzero child exit code was hidden.'
    Check ($result.progress.frames -eq 3 -and $result.progress.completed -and $result.stderr -match 'synthetic diagnostic') 'Process streams were not drained and parsed.'
    Check ($result.wallSeconds -gt 0 -and $null -ne $result.cpuSeconds -and $result.peakWorkingSetMiB -gt 0 -and $result.samples -gt 0) 'Process CPU, time or memory measurements are missing.'
    $expectedPercent = [math]::Round(100 * $result.cpuSeconds / $result.wallSeconds / 4,2)
    Check ([math]::Abs($result.cpuMachinePercent - $expectedPercent) -lt 0.05) 'CPU percent is not normalized to the logical processor count.'

    $command = '[Console]::WriteLine($PID); Start-Sleep -Seconds 30'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $timeout = Invoke-RecordingProcess $shell @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) $fixture 1 4
    Check ($timeout.timedOut -and $timeout.wallSeconds -lt 8) 'Child process timeout was not enforced.'
    $childId = [int]([IO.File]::ReadAllText((Join-Path $fixture 'progress.log')).Trim())
    $childAlive = $false
    try { $child = [Diagnostics.Process]::GetProcessById($childId); try { $childAlive = -not $child.HasExited } finally { $child.Dispose() } } catch [ArgumentException] { }
    Check (-not $childAlive) 'Timed-out synthetic child process was left running.'
    Write-Host "PASS: $script:checks recording benchmark helper checks (manifest constraints, progress, CPU normalization, process errors and timeout cleanup)."
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith($temporary.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
