function Get-CaptureTargetSummary($Progress, [long]$DecodedFrames, [int]$Fps, [int]$Duration, $BoardActivity) {
    $available = $null -ne $Progress.frameCount -and $null -ne $Progress.duplicateFrames -and $null -ne $Progress.droppedFrames -and $Progress.frameCount -ge 0 -and $Progress.duplicateFrames -ge 0 -and $Progress.droppedFrames -ge 0
    $sourceEstimate = if ($available) { $Progress.frameCount - $Progress.duplicateFrames + $Progress.droppedFrames } else { $null }
    $newFrames = if ($available) { $DecodedFrames - $Progress.duplicateFrames } else { $null }
    $expected = $Fps * $Duration
    $freshTarget = $available -and $newFrames -ge [Math]::Max(1, $expected - $Fps) -and $newFrames -le $expected + $Fps
    $noDuplicates = $available -and $Progress.duplicateFrames -eq 0
    $sceneReady = $BoardActivity.paintsPerSecond -ge $Fps
    return [pscustomobject]@{
        countersAvailable=$available; estimatedInputFrames=$sourceEstimate; inputEstimateMethod='encoded progress frames - dup_frames + drop_frames; not an independent input counter'
        newOutputFrames=$newFrames; expectedFrames=$expected; startupFrameAllowance=$Fps; freshFrameTargetMet=$freshTarget
        noArtificialDuplicates=$noDuplicates; sourceSceneFastEnough=$sceneReady
        passed=($freshTarget -and $noDuplicates -and $sceneReady)
    }
}

function Get-ReplayTimingSummary($Media, [int]$Duration) {
    $durations = @($Media | ForEach-Object { [double]$_.media.format.duration })
    $expected = [int][Math]::Ceiling($Duration / 2.0)
    $irregular = 0
    for ($i = 0; $i -lt $durations.Count; $i++) {
        $wanted = [Math]::Min(2, $Duration - 2 * $i)
        if ($wanted -le 0 -or [Math]::Abs($durations[$i] - $wanted) -gt 0.20) { $irregular++ }
    }
    $sum = [double](($durations | Measure-Object -Sum).Sum)
    return [pscustomobject]@{ seconds=$durations; totalSeconds=$sum; expectedCount=$expected; irregularSegments=$irregular; segmentToleranceSeconds=0.20; totalToleranceSeconds=0.30; passed=($durations.Count -eq $expected -and $irregular -eq 0 -and [Math]::Abs($sum - $Duration) -le 0.30) }
}
