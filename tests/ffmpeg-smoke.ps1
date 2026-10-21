[CmdletBinding()]
param([string]$ToolsBin,[Parameter(Mandatory=$true)][string]$WorkDir,[switch]$CheckHardware)
$ErrorActionPreference='Stop'
if (-not $ToolsBin) { $ToolsBin=Join-Path (Split-Path $PSScriptRoot) 'tools\bin' }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$WorkDir=[IO.Path]::GetFullPath($WorkDir)
$Ffmpeg=Join-Path $ToolsBin 'ffmpeg.exe'
$Ffprobe=Join-Path $ToolsBin 'ffprobe.exe'
$script:Results=@()
function Run-Tool($Exe,$Name,[string[]]$Arguments,[switch]$AllowFailure) {
    $out=Join-Path $WorkDir ($Name+'.stdout.txt')
    $err=Join-Path $WorkDir ($Name+'.stderr.txt')
    $quoted=@($Arguments|ForEach-Object {'"'+$_.Replace('"','\"')+'"'})
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$Exe
    $psi.Arguments=$quoted -join ' '
    $psi.UseShellExecute=$false
    $psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true
    $psi.RedirectStandardError=$true
    $proc=New-Object Diagnostics.Process
    $proc.StartInfo=$psi
    if(-not $proc.Start()){throw "Cannot start $Name"}
    $stdout=$proc.StandardOutput.ReadToEndAsync()
    $stderr=$proc.StandardError.ReadToEndAsync()
    if (-not $proc.WaitForExit(60000)) { $proc.Kill(); throw "Timeout: $Name" }
    $code=$proc.ExitCode
    [IO.File]::WriteAllText($out,$stdout.GetAwaiter().GetResult())
    [IO.File]::WriteAllText($err,$stderr.GetAwaiter().GetResult())
    $proc.Dispose()
    $script:Results+=@{test=$Name;exitCode=$code;status=$(if($code -eq 0){'pass'}else{'unavailable'})}
    if ($code -ne 0 -and -not $AllowFailure) { throw "$Name failed ($code): $(Get-Content -LiteralPath $err -Raw)" }
    return $code
}
function FF($Name,[string[]]$Arguments,[switch]$AllowFailure) { Run-Tool $Ffmpeg $Name (@('-y','-hide_banner','-loglevel','error')+$Arguments) -AllowFailure:$AllowFailure }
$null=Run-Tool $Ffmpeg 'version' @('-version')
if ((Get-Content (Join-Path $WorkDir 'version.stdout.txt') -Raw) -notmatch 'ffmpeg version 9\.0\.2-KeepClip-source-build-1') { throw 'Unexpected FFmpeg build.' }
$null=Run-Tool $Ffmpeg 'encoders' @('-hide_banner','-encoders')
$encoders=Get-Content (Join-Path $WorkDir 'encoders.stdout.txt') -Raw
foreach($encoder in @('libx264','h264_nvenc','h264_amf','h264_qsv','aac')) { if($encoders -notmatch ('\b'+$encoder+'\b')){throw "Missing $encoder"} }
$source=Join-Path $WorkDir 'synthetic.mp4'
$null=FF 'cpu-recording' @('-f','lavfi','-i','testsrc2=size=640x360:rate=30:duration=6','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=6','-c:v','libx264','-preset','veryfast','-crf','18','-g','30','-pix_fmt','yuv420p','-c:a','aac','-b:a','128k','-shortest',$source)
$null=Run-Tool $Ffprobe 'probe' @('-v','error','-show_streams','-show_format','-of','json',$source)
$probe=Get-Content (Join-Path $WorkDir 'probe.stdout.txt') -Raw|ConvertFrom-Json
if (@($probe.streams|Where-Object {$_.codec_name -eq 'h264'}).Count -ne 1 -or @($probe.streams|Where-Object {$_.codec_name -eq 'aac'}).Count -ne 1 -or [double]$probe.format.duration -lt 5.9) { throw 'Invalid recorded video/audio.' }
$null=FF 'segmentation' @('-i',$source,'-c','copy','-f','segment','-segment_time','2','-reset_timestamps','1','-segment_format','mpegts',(Join-Path $WorkDir 'segment_%03d.ts'))
$segments=@(Get-ChildItem -LiteralPath $WorkDir -Filter 'segment_*.ts'|Sort-Object Name)
if($segments.Count -lt 3){throw 'Recording segmentation failed.'}
$concat=Join-Path $WorkDir 'concat.txt'
[IO.File]::WriteAllLines($concat,@($segments|ForEach-Object {"file '"+$_.FullName.Replace('\','/')+"'"}),(New-Object Text.UTF8Encoding($false)))
$null=FF 'replay-remux' @('-f','concat','-safe','0','-i',$concat,'-c','copy','-movflags','+faststart',(Join-Path $WorkDir 'replay.mp4'))
$cut=Join-Path $WorkDir 'cut.mp4'
$null=FF 'cut-compression' @('-ss','1','-to','4','-i',$source,'-c:v','libx264','-preset','veryfast','-b:v','220k','-maxrate','220k','-bufsize','440k','-c:a','aac','-b:a','128k','-movflags','+faststart',$cut)
if ((Get-Item -LiteralPath $cut).Length -gt 1000000){throw 'Compression size exceeds test limit.'}
$pcm=Join-Path $WorkDir 'normalized.s16le'
$null=FF 'transcription-audio' @('-i',$source,'-af','dynaudnorm=f=150:g=15','-vn','-ac','1','-ar','16000','-f','s16le',$pcm)
if ((Get-Item -LiteralPath $pcm).Length -lt 190000){throw 'Transcription audio is incomplete.'}
$null=FF 'thumbnail' @('-i',$source,'-ss','1','-frames:v','1','-vf','scale=320:-2','-q:v','2',(Join-Path $WorkDir 'thumbnail.jpg'))
if ($CheckHardware) {
    foreach($encoder in @('h264_nvenc','h264_amf','h264_qsv')) {
        $null=FF $encoder @('-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=2','-c:v',$encoder,'-pix_fmt','yuv420p',(Join-Path $WorkDir ($encoder+'.mp4'))) -AllowFailure
    }
    $null=FF 'cuda-playback-proxy' @('-hwaccel','cuda','-hwaccel_output_format','cuda','-i',$source,'-vf','scale_cuda=320:-2','-c:v','h264_nvenc','-preset','p4','-c:a','aac',(Join-Path $WorkDir 'cuda-proxy.mp4')) -AllowFailure
}
$script:Results|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $WorkDir 'results.json') -Encoding UTF8
Write-Host "FFmpeg synthetic tests passed. Hardware availability (if checked): $WorkDir/results.json"
