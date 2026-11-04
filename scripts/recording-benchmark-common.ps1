Set-StrictMode -Version Latest

function Read-RecordingManifest([string]$Path, [string]$SourcePath) {
    if ((Get-Item -LiteralPath $Path).Length -gt 1048576) { throw 'Recording command manifest exceeds 1 MiB.' }
    $manifest = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) { throw 'Unsupported recording command manifest.' }
    if ($manifest.sourceSha256 -ne (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash) { throw 'Recording command source changed. Export a fresh manifest from ReplayCaptureCommand.' }
    $expected = @{ '1080p30' = @(1920,1080,30); '1080p60' = @(1920,1080,60); '1440p60' = @(2560,1440,60) }
    $seen = @{}
    foreach ($profile in $manifest.profiles) {
        if (-not $expected.ContainsKey($profile.name) -or $seen.ContainsKey($profile.name)) { throw 'Unexpected or duplicate benchmark profile.' }
        $seen[$profile.name] = $true
        $size = $expected[$profile.name]
        if ($profile.width -ne $size[0] -or $profile.height -ne $size[1] -or $profile.fps -ne $size[2] -or $profile.quality -ne 'high') { throw 'Profile dimensions, fps or quality differ from the bounded benchmark contract.' }
        $encoders = @{}
        foreach ($encoder in $profile.encoders) {
            if ($encoder.name -notin @('libx264','h264_nvenc','h264_amf','h264_qsv') -or $encoders.ContainsKey($encoder.name)) { throw 'Unexpected or duplicate encoder in manifest.' }
            $encoders[$encoder.name] = $true
            $arguments = @($encoder.arguments)
            if ($arguments.Count -lt 6 -or $arguments.Count % 2 -ne 0) { throw 'Encoder arguments must be complete option/value pairs.' }
            $options = @{}
            for ($i = 0; $i -lt $arguments.Count; $i += 2) {
                $option = [string]$arguments[$i]
                $value = [string]$arguments[$i + 1]
                if ($option -notin @('-c:v','-preset','-rc','-qp','-bf','-usage','-quality','-qp_i','-qp_p','-qp_b','-global_quality','-crf','-g') -or $options.ContainsKey($option) -or $value -notmatch '^[A-Za-z0-9_]+$') { throw 'Unsafe or unsupported encoder option in manifest.' }
                $options[$option] = $value
            }
            if ($options['-c:v'] -ne $encoder.name -or $options['-g'] -ne [string]$profile.fps) { throw 'Manifest encoder or GOP does not match its profile.' }
        }
        if (-not $encoders.ContainsKey('libx264')) { throw 'Every profile must include the production CPU encoder.' }
    }
    if (-not $seen.ContainsKey('1080p30') -or -not $seen.ContainsKey('1080p60')) { throw 'Manifest must include 1080p30 and 1080p60.' }
    return $manifest
}

function Quote-RecordingArgument([string]$Value) {
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Read-RecordingProgress([string]$Text) {
    $values = @{}
    foreach ($line in $Text -split '\r?\n') {
        if ($line -match '^([a-z_]+)=(.*)$') { $values[$matches[1]] = $matches[2].Trim() }
    }
    $result = [ordered]@{ frames = $null; reportedFps = $null; speed = $null; mediaSeconds = $null; duplicatedFrames = $null; droppedFrames = $null; completed = ($values['progress'] -eq 'end') }
    foreach ($field in @(@('frame','frames'),@('fps','reportedFps'),@('speed','speed'),@('out_time_us','mediaSeconds'),@('dup_frames','duplicatedFrames'),@('drop_frames','droppedFrames'))) {
        if (-not $values.ContainsKey($field[0])) { continue }
        $number = 0.0
        if ([double]::TryParse(($values[$field[0]] -replace 'x$',''), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) {
            $result[$field[1]] = if ($field[0] -eq 'out_time_us') { $number / 1000000 } else { $number }
        }
    }
    return [pscustomobject]$result
}

function Get-RecordingOutputBytes([string]$Directory) {
    $total = 0L
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter 'seg_*.ts' -File) { $total += $file.Length }
    return $total
}

function Invoke-RecordingProcess([string]$Executable, [string[]]$Arguments, [string]$Directory, [int]$TimeoutSeconds, [int]$LogicalProcessors) {
    if ($LogicalProcessors -lt 1) { throw 'Logical processor count must be positive.' }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = ($Arguments | ForEach-Object { Quote-RecordingArgument $_ }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $samples = New-Object 'Collections.Generic.List[object]'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    $timedOut = $false
    $cpu = $null
    $peak = 0L
    try {
        if (-not $process.Start()) { throw 'Could not start benchmark process.' }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.HasExited) {
            $process.Refresh()
            try {
                $cpu = $process.TotalProcessorTime.TotalSeconds
                $peak = [math]::Max($peak, $process.PeakWorkingSet64)
                $samples.Add([pscustomobject]@{ wallSeconds = [math]::Round($clock.Elapsed.TotalSeconds,3); cpuSeconds = $cpu; workingSetBytes = $process.WorkingSet64; peakWorkingSetBytes = $peak })
            } catch [InvalidOperationException] { }
            if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                try { $process.Kill() } catch [InvalidOperationException] { }
                break
            }
            $null = $process.WaitForExit(200)
        }
        if (-not $process.WaitForExit(5000)) { throw 'Benchmark process did not terminate after timeout.' }
        $clock.Stop()
        try { $cpu = $process.TotalProcessorTime.TotalSeconds; $peak = [math]::Max($peak, $process.PeakWorkingSet64) } catch [InvalidOperationException] { }
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $Directory 'progress.log'), $output, (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText((Join-Path $Directory 'stderr.log'), $errors, (New-Object Text.UTF8Encoding($false)))
        $samples.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Directory 'samples.json') -Encoding UTF8
        $cores = if ($null -eq $cpu) { $null } else { $cpu / $clock.Elapsed.TotalSeconds }
        return [pscustomobject]@{
            exitCode = $process.ExitCode; timedOut = $timedOut; wallSeconds = $clock.Elapsed.TotalSeconds; cpuSeconds = $cpu
            cpuCoreEquivalents = $(if ($null -eq $cores) { $null } else { [math]::Round($cores,3) })
            cpuMachinePercent = $(if ($null -eq $cores) { $null } else { [math]::Round(100 * $cores / $LogicalProcessors,2) })
            peakWorkingSetMiB = [math]::Round($peak / 1MB,2); samples = $samples.Count
            progress = Read-RecordingProgress $output; stderr = $errors
        }
    } finally {
        if ($started -and -not $process.HasExited) { try { $process.Kill(); $null = $process.WaitForExit(5000) } catch [InvalidOperationException] { } }
        $process.Dispose()
    }
}