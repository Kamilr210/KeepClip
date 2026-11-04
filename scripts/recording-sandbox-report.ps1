[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ResultPath)

$ErrorActionPreference = 'Stop'
# Interrupted runs and older probes can lack a phase or a saved clip.
Set-StrictMode -Off
$inputFile = Get-Item -LiteralPath $ResultPath
$run = [IO.File]::ReadAllText($inputFile.FullName) | ConvertFrom-Json
$pl = [Globalization.CultureInfo]::GetCultureInfo('pl-PL')
function Escape($Value) { [Net.WebUtility]::HtmlEncode([string]$Value) }
function Number($Value, [int]$Places = 0) {
    if ($null -eq $Value) { return '—' }
    ([double]$Value).ToString(('F' + $Places), $pl)
}
function Badge($Passed) {
    if ($null -eq $Passed) { return '<span class="skip">Brak pomiaru</span>' }
    if ($Passed -eq $true) { return '<span class="pass">Zaliczony</span>' }
    return '<span class="fail">Niezaliczony</span>'
}

$phases = foreach ($capture in $run.captures) {
    $replay = $capture.vfrReplay
    if (-not $replay -and $run.mode -eq 'GdiVfrProbe' -and -not $capture.baseline) { $replay = $run.vfrReplay }
    $clip = $replay.savedClip
    $file = [string]$clip.file
    $clipLink = ''
    if ($file -cmatch '^[A-Za-z0-9_-]+\.mp4\z' -and (Test-Path -LiteralPath (Join-Path $inputFile.DirectoryName $file) -PathType Leaf)) {
        $clipLink = '<p><a href="' + (Escape ([Uri]::EscapeDataString($file))) + '">Otwórz zapisany klip MP4</a></p>'
    }
    $rate = if ($null -ne $capture.target.newOutputFrames -and $run.durationSeconds -gt 0) { $capture.target.newOutputFrames / $run.durationSeconds } else { $null }
    $segmentInfo = if ($replay.segmentTiming) { "$(Number $replay.segmentTiming.seconds.Count) segmentów · $(Number $replay.segmentTiming.totalSeconds 3) s łącznie" } else { '—' }
    $clipInfo = if ($clip) { "$(Number $clip.seconds 3) s · $(Number $clip.frameTimes.count) klatek" } else { '—' }
    $testInfo = if ($capture.baseline) { 'Próba porównawcza' } else { 'Test nagrywania' }
    @"
<section><p class="eyebrow">$testInfo · $(Escape $capture.encoder)</p>
<h2>$(Escape $capture.label)</h2>
<p><b>$(Number $capture.frameCount)</b> klatek zapisanych · <b>$(Number $capture.progress.duplicateFrames)</b> duplikatów dodanych przez FFmpeg · <b>$(Number $capture.measurement.cpuPercentSandboxCapacity 2)%</b> CPU procesu FFmpeg</p>
<table><thead><tr><th>Kontrola</th><th>Wynik</th><th>Pomiar</th></tr></thead><tbody>
<tr><td>Bez sztucznych duplikatów</td><td>$(Badge $capture.target.noArtificialDuplicates)</td><td>$(Number $capture.progress.duplicateFrames) dodanych</td></tr>
<tr><td>Segmenty bufora</td><td>$(Badge $replay.segmentTiming.passed)</td><td>$segmentInfo</td></tr>
<tr><td>Dekodowanie i ciągłość MP4</td><td>$(Badge $clip.passed)</td><td>$clipInfo</td></tr>
<tr><td>Tempo $(Number $capture.fps) klatek/s</td><td>$(Badge $capture.target.passed)</td><td>$(Number $rate 1) klatek/s bez dopełniania</td></tr>
</tbody></table>$clipLink</section>
"@
}
$failure = if ($run.error) { '<p class="error">' + (Escape $run.error) + '</p>' } else { '' }
$html = @"
<!doctype html><html lang="pl"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>KeepClip · test nagrywania w Sandboxie</title><style>
*{box-sizing:border-box} body{margin:0;background:#f2f5f8;color:#192636;font:16px/1.55 system-ui,sans-serif} main{max-width:54rem;margin:auto;padding:1.5rem}
h1,h2,p{margin:.45rem 0} h1{font-size:clamp(1.6rem,4vw,2.3rem)} h2{font-size:1.25rem} section{padding:1.15rem;margin:1rem 0;background:white;border:1px solid #cdd8e2;border-radius:.6rem;overflow-x:auto}
.eyebrow{font-size:.85rem;color:#495d70} table{width:100%;margin-top:1rem;border-collapse:collapse;font-variant-numeric:tabular-nums} th,td{text-align:left;padding:.45rem;border-bottom:1px solid #d9e2ea} th{font-size:.9rem}
.pass,.fail,.skip{display:inline-block;border-radius:.35rem;padding:.1rem .55rem;font-weight:650}.pass{color:#125333;background:#d9f3e3}.fail{color:#912428;background:#fce2e3}.skip{color:#46515c;background:#edf0f3} a{color:#164e80}.error{white-space:pre-wrap;overflow-wrap:anywhere}
</style><main><p class="eyebrow">KeepClip · Windows Sandbox</p><h1>Test nagrywania</h1>
<p>Wynik całego przebiegu $(Badge $(if ($run.status -eq 'passed') { $true } elseif ($run.status -eq 'failed') { $false } else { $null }))</p>
<p>$(Escape $run.startedUtc) · $(Number $run.durationSeconds) s na fazę · <a href="results.json">Dane pomiarowe JSON</a></p>$failure
$($phases -join [Environment]::NewLine)
<p>Tempo obliczono z liczby zapisanych klatek po odjęciu duplikatów FFmpeg i czasu próby. CPU obejmuje tylko proces FFmpeg. Migotanie kursora ocenia obserwator.</p></main></html>
"@
[IO.File]::WriteAllText((Join-Path $inputFile.DirectoryName 'report.html'), $html, [Text.UTF8Encoding]::new($false))
