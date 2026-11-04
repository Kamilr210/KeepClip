[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CommandManifest,
    [ValidateRange(5,30)][int]$DurationSeconds = 10,
    [ValidateRange(2,5)][int]$Repeats = 2,
    [switch]$Include1440p,
    [switch]$CompareNvencP4,
    [switch]$IncludeSyntheticAudio
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'recording-benchmark-common.ps1')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This benchmark measures the Windows FFmpeg binaries used by KeepClip.' }
$manifest = Read-RecordingManifest $CommandManifest (Join-Path $repo 'KeepClip\Infrastructure\ReplayCaptureCommand.cs')
$ffmpeg = Join-Path $repo 'tools\bin\ffmpeg.exe'
if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf)) { throw 'Missing tools/bin/ffmpeg.exe. The benchmark does not download tools.' }
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$results = Join-Path $repo ('TestResults\recording-benchmark\' + $runId)
New-Item -ItemType Directory -Path $results -Force | Out-Null
$logical = [Environment]::ProcessorCount
$runs = New-Object 'Collections.Generic.List[object]'
$probes = New-Object 'Collections.Generic.List[object]'
$exitCode = 0
$errorText = ''
$startedUtc = [DateTime]::UtcNow.ToString('o')

function Encoder-Arguments($Encoder) { return @($Encoder.arguments | ForEach-Object { [string]$_ }) }

function Run-Case($Profile, $Encoder, [int]$Repeat, [switch]$Probe) {
    $name = $Profile.name + '-' + $Encoder.name + '-' + $(if ($Probe) { 'probe' } else { [string]$Repeat })
    $directory = Join-Path $results $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $duration = if ($Probe) { 1 } else { $DurationSeconds }
    $width = if ($Probe) { 320 } else { [int]$Profile.width }
    $height = if ($Probe) { 240 } else { [int]$Profile.height }
    $fps = [int]$Profile.fps
    $frames = if ($Probe) { 3 } else { $fps * $duration }
    $arguments = @('-hide_banner','-loglevel','error','-nostdin','-stats_period','0.5','-progress','pipe:1')
    if (-not $Probe) { $arguments += '-re' }
    $arguments += @('-f','lavfi','-i',("testsrc2=size={0}x{1}:rate={2}:duration={3},format=bgra" -f $width,$height,$fps,$duration))
    if ($IncludeSyntheticAudio -and -not $Probe) { $arguments += @('-re','-f','lavfi','-i',("sine=frequency=440:sample_rate=48000:duration={0}" -f $duration)) }
    $arguments += @('-map','0:v','-vf',("scale={0}:{1}:force_original_aspect_ratio=decrease,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2,format=nv12" -f $width,$height),'-frames:v',[string]$frames)
    if ($IncludeSyntheticAudio -and -not $Probe) { $arguments += @('-map','1:a','-c:a','aac','-b:a','192k') } else { $arguments += '-an' }
    if ($Encoder.name -eq 'baseline') { $arguments += @('-c:v','wrapped_avframe','-f','null','-') }
    else {
        $arguments += Encoder-Arguments $Encoder
        if ($Probe) { $arguments += @('-f','null','-') }
        else { $arguments += @('-f','segment','-segment_time','2','-reset_timestamps','1','-segment_format','mpegts',(Join-Path $directory 'seg_%06d.ts')) }
    }
    [pscustomobject]@{ executable = $ffmpeg; arguments = $arguments } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'command.json') -Encoding UTF8
    Write-Host ("[{0}] {1}" -f $(if ($Probe) { 'probe' } else { 'measure' }),$name)
    $measurement = Invoke-RecordingProcess $ffmpeg $arguments $directory $(if ($Probe) { 15 } else { $duration * 4 + 15 }) $logical
    $valid = $measurement.exitCode -eq 0 -and -not $measurement.timedOut -and $measurement.progress.completed -and $measurement.progress.frames -eq $frames -and $null -ne $measurement.cpuSeconds
    $size = Get-RecordingOutputBytes $directory
    $status = if ($valid) { 'measured' } elseif ($Probe -and $Encoder.name -ne 'libx264') { 'unavailable' } else { 'failed' }
    return [pscustomobject]@{
        profile = $Profile.name; encoder = $Encoder.name; repeat = $Repeat; status = $status; directory = $name
        width = $width; height = $height; targetFps = $fps; expectedFrames = $frames; outputBytes = $(if ($null -eq $size) { 0L } else { [long]$size })
        framesPerWallSecond = $(if ($null -eq $measurement.progress.frames) { $null } else { [math]::Round($measurement.progress.frames / $measurement.wallSeconds,2) })
        measurement = $measurement
    }
}

try {
    $profiles = @($manifest.profiles | Where-Object { $_.name -ne '1440p60' -or $Include1440p })
    if ($Include1440p -and @($profiles | Where-Object name -eq '1440p60').Count -eq 0) { throw 'Manifest does not include requested 1440p60 profile.' }
    $available = @{}
    foreach ($encoder in $profiles[0].encoders) {
        $probe = Run-Case $profiles[0] $encoder 0 -Probe
        $probes.Add($probe)
        $available[$encoder.name] = $probe.status -eq 'measured'
        if ($probe.status -eq 'failed') { throw ('Production CPU encoder probe failed. See ' + $probe.directory + '/stderr.log') }
    }
    for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
        foreach ($profile in $profiles) {
            $cases = @([pscustomobject]@{ name='baseline'; arguments=@() }) + @($profile.encoders | Where-Object { $available[$_.name] })
            if ($CompareNvencP4 -and $available.ContainsKey('h264_nvenc') -and $available['h264_nvenc']) {
                $original = @($profile.encoders | Where-Object name -eq 'h264_nvenc')[0]
                $comparison = Encoder-Arguments $original
                $preset = [Array]::IndexOf($comparison, '-preset')
                if ($preset -lt 0) { throw 'NVENC comparison requires a production preset in the manifest.' }
                $comparison[$preset + 1] = 'p4'
                $cases += [pscustomobject]@{ name='h264_nvenc-p4-comparison'; arguments=$comparison }
            }
            if ($repeat % 2 -eq 0) { [Array]::Reverse($cases) }
            foreach ($encoder in $cases) {
                $run = Run-Case $profile $encoder $repeat
                $runs.Add($run)
                if ($run.status -ne 'measured') { $exitCode = 1 }
                Write-Host ("{0}: {1}; CPU {2}% machine / {3} cores, RAM peak {4} MiB, speed {5}x" -f $run.directory,$run.status,$run.measurement.cpuMachinePercent,$run.measurement.cpuCoreEquivalents,$run.measurement.peakWorkingSetMiB,$run.measurement.progress.speed)
            }
        }
    }
} catch {
    $exitCode = 1
    $errorText = $_.Exception.Message
    Write-Host $errorText -ForegroundColor Red
} finally {
    $limitations = @('Only synthetic testsrc2 video is used. No desktop, cursor, game, system audio or microphone is captured.', 'These results measure generation, BGRA/NV12 conversion, encoding and output writes. They do not measure desktop-capture copies, WASAPI mixing, game FPS or cursor flicker.', 'CPU machine percent = FFmpeg CPU seconds / wall seconds / logical processor count * 100. Core equivalents = CPU seconds / wall seconds.', 'Input is paced with -re. Startup and shutdown are included. Slow encoding can extend a sample; compare wall time and speed with the target duration.', 'Hardware availability comes from short local synthetic probes. An unavailable encoder is not a measured result.', 'Synthetic content compresses differently from real games. Samples are sequential, alternate order and cannot establish a universal performance threshold.', 'Peak memory is the largest process peak working set observed. Process samples are taken every 200 ms. GPU utilization and VRAM are not measured.', 'The baseline includes the same generator and conversion but no H.264 encoding or segment writes; do not interpret its subtraction as pure encoder-only cost.')
    $report = [pscustomobject]@{ schemaVersion=1; runId=$runId; startedUtc=$startedUtc; finishedUtc=[DateTime]::UtcNow.ToString('o'); exitCode=$exitCode; error=$errorText; logicalProcessors=$logical; durationSeconds=$DurationSeconds; repeats=$Repeats; syntheticAudio=[bool]$IncludeSyntheticAudio; commandSourceSha256=$manifest.sourceSha256; ffmpegSha256=(Get-FileHash -LiteralPath $ffmpeg -Algorithm SHA256).Hash; probes=@($probes.ToArray()); runs=@($runs.ToArray()); limitations=$limitations }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $results 'results.json') -Encoding UTF8
    Copy-Item -LiteralPath $CommandManifest -Destination (Join-Path $results 'command-manifest.json')
    $markdown = @('# KeepClip synthetic recording benchmark', '', "Run: $runId. Exit code: $exitCode. Logical processors: $logical. Requested sample duration: $DurationSeconds seconds; repeats: $Repeats.", '', '| Profile | Encoder | Repeat | Result | CPU machine % | CPU cores | RAM peak MiB | Wall s | Frames | Speed | Output MiB |', '| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    $rows = foreach ($run in $runs) {
        $m = $run.measurement
        $cells = @($run.profile,$run.encoder,$run.repeat,$run.status,$m.cpuMachinePercent,$m.cpuCoreEquivalents,$m.peakWorkingSetMiB,[math]::Round($m.wallSeconds,3),$m.progress.frames,$m.progress.speed,[math]::Round($run.outputBytes / 1MB,2))
        $markdown += '| ' + ($cells -join ' | ') + ' |'
        '<tr>' + (($cells | ForEach-Object { '<td>' + [Net.WebUtility]::HtmlEncode([string]$_) + '</td>' }) -join '') + '</tr>'
    }
    $markdown += @('', '## Encoder probes')
    foreach ($probe in $probes) { $markdown += ('- ' + $probe.encoder + ': ' + $probe.status + '. Details: ' + $probe.directory + '/stderr.log') }
    $markdown += @('', '## Limits and interpretation') + @($limitations | ForEach-Object { '- ' + $_ })
    if ($errorText) { $markdown += @('', 'Failure: ' + $errorText) }
    $markdown | Set-Content -LiteralPath (Join-Path $results 'summary.md') -Encoding UTF8
    $html = '<!doctype html><html lang="en"><meta charset="utf-8"><title>KeepClip recording benchmark</title><style>body{font:15px system-ui;margin:2rem;background:#0b111a;color:#edf3f9}table{border-collapse:collapse;width:100%}th,td{padding:.5rem;text-align:right;border-bottom:1px solid #354454}td:first-child,td:nth-child(2){text-align:left}a{color:#7ae9bc}li{margin:.5rem 0}</style><h1>KeepClip: synthetic recording benchmark</h1><p>Exit code: ' + $exitCode + '. Run: ' + $runId + '. Logical processors: ' + $logical + '.</p><p>This is not a desktop recording or a measurement of game FPS.</p><table><thead><tr><th>Profile</th><th>Encoder</th><th>Repeat</th><th>Result</th><th>CPU machine %</th><th>CPU cores</th><th>RAM peak MiB</th><th>Wall s</th><th>Frames</th><th>Speed</th><th>Output MiB</th></tr></thead><tbody>' + ($rows -join '') + '</tbody></table><h2>Encoder probes</h2><ul>' + (($probes | ForEach-Object { '<li>' + [Net.WebUtility]::HtmlEncode($_.encoder + ': ' + $_.status) + ' <a href="' + $_.directory + '/stderr.log">log</a></li>' }) -join '') + '</ul><h2>Limits and interpretation</h2><ul>' + (($limitations | ForEach-Object { '<li>' + [Net.WebUtility]::HtmlEncode($_) + '</li>' }) -join '') + '</ul><p>' + [Net.WebUtility]::HtmlEncode($errorText) + '</p><p><a href="results.json">Raw measurements</a> | <a href="summary.md">Markdown summary</a></p></html>'
    [IO.File]::WriteAllText((Join-Path $results 'report.html'), $html, (New-Object Text.UTF8Encoding($false)))
    Write-Host ('Benchmark report: ' + (Join-Path $results 'report.html'))
}
exit $exitCode
