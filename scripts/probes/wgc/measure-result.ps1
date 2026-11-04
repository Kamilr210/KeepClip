[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$result = Get-Content -Raw -LiteralPath (Join-Path $Directory 'results.json') | ConvertFrom-Json
if ($result.status -ne 'measured') { return [pscustomobject]@{ status=$result.status; target=$result.target; interval=$result.minUpdateInterval; freshFrameTargetMet=$null; error=$result.error } }
$frames = @(foreach ($frame in (Get-Content -Raw -LiteralPath (Join-Path $Directory 'frames.json') | ConvertFrom-Json)) { $frame })
$seen = [Collections.Generic.HashSet[int]]::new()
$previous = $null
$lastMarker = $null
$invalid = 0
$regressions = 0
$badTimestamps = 0
$maxGap = 0.0
foreach ($frame in $frames) {
    if ($null -ne $previous) {
        if ($frame.TimestampTicks -le $previous.TimestampTicks) { $badTimestamps++ }
        $maxGap = [Math]::Max($maxGap, $frame.ArrivalSeconds - $previous.ArrivalSeconds)
    }
    if ($null -eq $frame.Marker) { $invalid++ }
    else {
        if ($null -ne $lastMarker -and $frame.Marker -lt $lastMarker) { $regressions++ }
        [void]$seen.Add($frame.Marker)
        $lastMarker = $frame.Marker
    }
    $previous = $frame
}
# Every phase runs ten seconds after the first frame. Allow the same one-second margin as the GDI probe.
$seconds = 10
$targetFps = 60
$span = if ($frames.Count -ge 2) { $frames[-1].ArrivalSeconds - $frames[0].ArrivalSeconds } else { 0 }
$producer = $result.capture.producerFps
$measured = $seen.Count -ge ($seconds - 1) * $targetFps -and $span -ge ($seconds - 1) -and $maxGap -lt 1 -and $producer -ge $targetFps -and $invalid -eq 0 -and $regressions -eq 0 -and $badTimestamps -eq 0
[pscustomobject]@{
    status='measured'; target=$result.target; interval=$result.minUpdateInterval; freshFrameTargetMet=$measured
    targetFps=$targetFps; observationSeconds=$seconds; startupAllowanceSeconds=1; frames=$frames.Count; distinctMarkers=$seen.Count
    freshFpsOverDuration=$seen.Count / $seconds; producerFps=$producer; invalidMarkers=$invalid; markerRegressions=$regressions
    timestampErrors=$badTimestamps; arrivalSpanSeconds=$span; maxArrivalGapSeconds=$maxGap
}
