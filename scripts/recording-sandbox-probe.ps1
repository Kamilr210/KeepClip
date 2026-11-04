[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][Alias('OutputDirectory')][string]$ResultsDirectory,
    [Alias('InputDirectory')][string]$ToolsDirectory = $PSScriptRoot,
    [string]$ManifestPath,
    [ValidateRange(3,12)][int]$DurationSeconds = 8,
    [switch]$ControlsOnly,
    [switch]$DdaProbe,
    [switch]$GdiFixProbe,
    [switch]$GdiVfrProbe
)

$ErrorActionPreference = 'Stop'
$modes = @('ControlsOnly','DdaProbe','GdiFixProbe','GdiVfrProbe' | Where-Object { $PSBoundParameters[$_] })
if ($modes.Count -gt 1) { throw 'Choose only one probe mode.' }
$mode = if ($modes.Count) { $modes[0] } else { 'Capture' }
if ($mode -in @('DdaProbe','GdiFixProbe','GdiVfrProbe') -and $DurationSeconds -gt 10) { throw 'Candidate capture probes are limited to 10 seconds per phase.' }
$productionCapture = $mode -eq 'Capture'
$compareGdi = $GdiFixProbe -or $GdiVfrProbe
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::UserName.Equals('WDAGUtilityAccount', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'This probe runs only inside a fresh Windows Sandbox as WDAGUtilityAccount. Host desktop capture is prohibited.'
}
$tools = [IO.Path]::GetFullPath($ToolsDirectory).TrimEnd('\')
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory).TrimEnd('\')
if ($resultsRoot.Equals($tools, [StringComparison]::OrdinalIgnoreCase) -or $resultsRoot.StartsWith($tools + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a separate dedicated results folder, not the read-only staging folder.' }
if (-not $ManifestPath) { $ManifestPath = Join-Path $tools 'manifest.json' }
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported staging manifest.' }
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'recording-sandbox-checks.ps1', 'recording-sandbox-report.ps1', [IO.Path]::GetFileName($PSCommandPath))) {
    $entry = @($manifest.files | Where-Object { $_.name -eq $name })
    if ($entry.Count -ne 1) { throw "Missing or duplicate staging manifest entry: $name" }
}
foreach ($entry in $manifest.files) {
    $name = [string]$entry.name
    if ([string]::IsNullOrWhiteSpace($name) -or $name -ne [IO.Path]::GetFileName($name) -or $name -in @('.', '..')) { throw 'Manifest entries must be simple filenames.' }
    $path = Join-Path $tools $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Staging hash mismatch: $name" }
}
. (Join-Path $tools 'recording-sandbox-checks.ps1')
$run = Join-Path $resultsRoot ('sandbox-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
$script:board = $null
$script:runs = New-Object 'Collections.Generic.List[object]'
$started = [DateTime]::UtcNow
$logicalProcessors = [Environment]::ProcessorCount
$result = [ordered]@{
    schemaVersion = 1; startedUtc = $started.ToString('o'); finishedUtc = $null
    environment = 'Windows Sandbox'; user = [Environment]::UserName; logicalProcessors = $logicalProcessors
    durationSeconds = $DurationSeconds; capture = $(if ($DdaProbe) { 'ddagrab' } elseif ($ControlsOnly) { 'isolation-controls' } else { 'gdigrab' }); drawMouse = $false; audio = $false
    mode = $mode
    limitations = @('Synthetic Sandbox desktop only; this is not game FPS.', 'CPU metrics are per FFmpeg process, include startup and exclude compositor, audio and game costs.', 'Cursor flags are metadata and do not detect visible cursor flicker.', 'No microphone, host desktop, account, network or production data is used.', 'vGPU availability does not imply hardware encoder availability.')
    stagingManifest = $manifest; adapters = @(); display = $null; encoderProbes = @(); captures = @(); controls = @(); error = $null
}

function Quote-Argument([string]$Value) {
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Read-ProbeProgress([string]$Output) {
    $values = @{}
    foreach ($line in ($Output -split '\r?\n')) {
        if ($line -match '^(frame|fps|out_time_us|speed|progress|dup_frames|drop_frames)=(.*)$') { $values[$matches[1]] = $matches[2].Trim() }
    }
    $frames = 0L
    $validFrames = [long]::TryParse([string]$values['frame'], [ref]$frames)
    $microseconds = 0L
    $validTime = [long]::TryParse([string]$values['out_time_us'], [ref]$microseconds)
    $duplicates = 0L
    $drops = 0L
    $validDuplicates = [long]::TryParse([string]$values['dup_frames'], [ref]$duplicates)
    $validDrops = [long]::TryParse([string]$values['drop_frames'], [ref]$drops)
    return [pscustomobject]@{ frameCount=$(if ($validFrames) { $frames } else { $null }); outputSeconds=$(if ($validTime) { $microseconds / 1000000.0 } else { $null }); reportedFps=$values['fps']; speed=$values['speed']; state=$values['progress']; duplicateFrames=$(if ($validDuplicates) { $duplicates } else { $null }); droppedFrames=$(if ($validDrops) { $drops } else { $null }) }
}

function Get-FrameTimeSummary($Frames) {
    $values = New-Object 'Collections.Generic.List[double]'
    foreach ($frame in $Frames) {
        $value = 0.0
        if ([double]::TryParse([string]$frame.best_effort_timestamp_time, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$value) -and -not [double]::IsNaN($value) -and -not [double]::IsInfinity($value)) { $values.Add($value) }
    }
    $deltas = @()
    for ($i = 1; $i -lt $values.Count; $i++) { $deltas += $values[$i] - $values[$i - 1] }
    return [pscustomobject]@{
        count=$values.Count; firstSeconds=$(if ($values.Count) { $values[0] } else { $null }); lastSeconds=$(if ($values.Count) { $values[$values.Count-1] } else { $null })
        nonIncreasingCount=@($deltas | Where-Object { $_ -le 0 }).Count
        minimumDeltaSeconds=$(if ($deltas.Count) { ($deltas | Measure-Object -Minimum).Minimum } else { $null })
        maximumDeltaSeconds=$(if ($deltas.Count) { ($deltas | Measure-Object -Maximum).Maximum } else { $null })
    }
}

function Test-SavedReplay($Media, [long]$Frames, [int]$Duration, [string]$LogKey) {
    $clip = Join-Path $run ($LogKey + '.mp4')
    $list = Join-Path $run ($LogKey + '-concat.txt')
    $files = @($Media | ForEach-Object { "file '$($_.file)'" })
    [IO.File]::WriteAllLines($list, [string[]]$files, $utf8)
    $mux = Invoke-ProbeProcess ('mux-' + $LogKey) $ffmpeg @('-nostdin','-hide_banner','-loglevel','error','-f','concat','-safe','0','-i',$list,'-map','0:v:0','-c:v','copy','-f','mp4',$clip) 15
    if ($mux.measurement.exitCode -ne 0) { return [pscustomobject]@{ passed=$false; muxExitCode=$mux.measurement.exitCode; error=$mux.error } }
    $probe = Invoke-ProbeProcess ('clip-timing-' + $LogKey) $ffprobe @('-v','error','-select_streams','v:0','-count_frames','-show_frames','-show_entries','stream=nb_read_frames:frame=best_effort_timestamp_time:format=duration','-of','json',$clip) 10
    $decode = Invoke-ProbeProcess ('clip-decode-' + $LogKey) $ffmpeg @('-nostdin','-hide_banner','-loglevel','error','-xerror','-i',$clip,'-map','0:v:0','-fps_mode','passthrough','-enc_time_base','demux','-f','null','-') 10
    $details = if ($probe.measurement.exitCode -eq 0) { $probe.output | ConvertFrom-Json } else { $null }
    $times = Get-FrameTimeSummary $details.frames
    $seconds = [double]$details.format.duration
    $passed = $probe.measurement.exitCode -eq 0 -and $decode.measurement.exitCode -eq 0 -and $Frames -gt 1 -and $times.count -eq $Frames -and $times.nonIncreasingCount -eq 0 -and $times.maximumDeltaSeconds -le 0.20 -and [Math]::Abs($seconds - $Duration) -le 0.30
    return [pscustomobject]@{ file=($LogKey + '.mp4'); passed=$passed; seconds=$seconds; expectedFrames=$Frames; frameTimes=$times; muxExitCode=$mux.measurement.exitCode; probeExitCode=$probe.measurement.exitCode; decodeExitCode=$decode.measurement.exitCode; scope='Stream-copy concat of finalized test segments, no audio.' }
}

function Invoke-ProbeProcess([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $info.WorkingDirectory = $run
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $peak = 0L
    $samples = 0
    $memorySampleFailures = 0
    $cursorShowing = 0
    $cursorHidden = 0
    $cursorUnknown = 0
    $cursorTransitions = 0
    $previousCursor = -1
    $timedOut = $false
    $cancelled = $false
    $stdout = $null
    $stderr = $null
    $processStarted = $false
    try {
        if (-not $process.Start()) { throw "Cannot start $Name" }
        $processStarted = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(100)) {
            try { $process.Refresh(); $peak = [Math]::Max($peak, $process.WorkingSet64) }
            catch [InvalidOperationException] { $memorySampleFailures++ }
            catch [ComponentModel.Win32Exception] { $memorySampleFailures++ }
            $samples++
            if ($script:board) {
                $flags = [KeepClipSandboxProbe.Board]::CursorFlags()
                if ($flags -lt 0) { $cursorUnknown++ }
                elseif (($flags -band 1) -ne 0) { $cursorShowing++ }
                else { $cursorHidden++ }
                if ($previousCursor -ge 0 -and $flags -ge 0 -and $previousCursor -ne $flags) { $cursorTransitions++ }
                $previousCursor = $flags
                $cancelled = $script:board.WasClosed
            }
            $timedOut = $clock.Elapsed.TotalSeconds -gt $TimeoutSeconds
            if ($timedOut -or $cancelled) { $process.Kill(); break }
        }
        if (-not $process.WaitForExit(5000)) { throw "Owned process $Name did not terminate." }
        $clock.Stop()
        $cpuSeconds = $process.TotalProcessorTime.TotalSeconds
        $stdoutText = $stdout.GetAwaiter().GetResult()
        $stderrText = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $run ($Name + '.stdout.txt')), $stdoutText, $utf8)
        [IO.File]::WriteAllText((Join-Path $run ($Name + '.stderr.txt')), $stderrText, $utf8)
        $measurement = [pscustomobject]@{
            name = $Name; exitCode = $process.ExitCode; timedOut = $timedOut; cancelled = $cancelled
            wallSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3); cpuSeconds = [Math]::Round($cpuSeconds, 3)
            cpuEquivalentCores = [Math]::Round($cpuSeconds / [Math]::Max(0.001, $clock.Elapsed.TotalSeconds), 3)
            cpuPercentSandboxCapacity = [Math]::Round(100 * $cpuSeconds / [Math]::Max(0.001, $clock.Elapsed.TotalSeconds) / $logicalProcessors, 2)
            sampledPeakWorkingSetBytes = $peak; samples = $samples; memorySampleFailures = $memorySampleFailures
            cursorFlags = [pscustomobject]@{ showingSamples=$cursorShowing; hiddenSamples=$cursorHidden; unavailableSamples=$cursorUnknown; flagTransitions=$cursorTransitions; detectsVisibleFlicker=$false }
            executable = [IO.Path]::GetFileName($Executable); arguments = $Arguments
            stdout = $Name + '.stdout.txt'; stderr = $Name + '.stderr.txt'
        }
        $script:runs.Add($measurement)
        return [pscustomobject]@{ measurement=$measurement; output=$stdoutText; error=$stderrText }
    } finally {
        if ($processStarted) {
            try { if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) } } catch { }
        }
        $process.Dispose()
    }
}

try {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KeepClipSandboxProbe
{
    public sealed class Board : Form
    {
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Font heading = new Font("Segoe UI", 22, FontStyle.Bold);
        private readonly Font text = new Font("Segoe UI", 13);
        private string phase = "Przygotowanie testu";
        private long paintCount;
        private bool timerPeriodOwned;
        public volatile bool WasClosed;
        public uint TimerPeriodResult = uint.MaxValue;
        public long PaintCount { get { return Interlocked.Read(ref paintCount); } }
        public double ElapsedSeconds { get { return clock.Elapsed.TotalSeconds; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo { public int size; public int flags; public IntPtr cursor; public Point point; }
        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint period);
        public static int CursorFlags() { CursorInfo info = new CursorInfo(); info.size = Marshal.SizeOf(typeof(CursorInfo)); return GetCursorInfo(ref info) ? info.flags : -1; }

        public Board(bool highResolutionTimer)
        {
            if (highResolutionTimer) { TimerPeriodResult = timeBeginPeriod(1); timerPeriodOwned = TimerPeriodResult == 0; }
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.PrimaryScreen.Bounds;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(12, 21, 32);
            timer.Interval = highResolutionTimer ? 8 : 16;
            timer.Tick += delegate { Invalidate(); };
            Shown += delegate { timer.Start(); };
            FormClosed += delegate { WasClosed = true; timer.Stop(); };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        public void Phase(string value)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { phase = value; });
        }

        public void Finish()
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { Close(); });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Interlocked.Increment(ref paintCount);
            int shift = (int)(clock.ElapsedMilliseconds / 8 % 120);
            using (Brush grid = new SolidBrush(Color.FromArgb(28, 47, 66)))
                for (int y = 140; y < Height; y += 80)
                    for (int x = -120; x < Width; x += 120)
                        e.Graphics.FillRectangle(grid, x + shift, y, 54, 38);
            using (Brush accent = new SolidBrush(Color.FromArgb(110, 235, 182)))
            {
                int x = (int)((Math.Sin(clock.Elapsed.TotalSeconds) + 1) * Math.Max(1, Width - 180) / 2);
                e.Graphics.FillEllipse(accent, x, Math.Max(160, Height / 2), 160, 160);
                e.Graphics.DrawString("KeepClip - test nagrywania w Windows Sandbox", heading, accent, 28, 22);
            }
            e.Graphics.DrawString(phase + " | Esc: przerwij", text, Brushes.White, 30, 76);
            e.Graphics.DrawString("Wlasna plansza testowa. Bez mikrofonu. Kursor oceniaj wzrokowo; automat nie wykrywa migotania.", text, Brushes.White, 30, 105);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); heading.Dispose(); text.Dispose(); if (timerPeriodOwned) { timeEndPeriod(1); timerPeriodOwned = false; } }
            base.Dispose(disposing);
        }

        public static Board Start(bool highResolutionTimer)
        {
            TaskCompletionSource<Board> ready = new TaskCompletionSource<Board>();
            Thread thread = new Thread(delegate()
            {
                Board board = null;
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    board = new Board(highResolutionTimer);
                    board.Shown += delegate { ready.TrySetResult(board); };
                    Application.Run(board);
                }
                catch (Exception ex) { ready.TrySetException(ex); }
                finally { if (board != null) { board.WasClosed = true; board.Dispose(); } }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Task.Wait(5000)) throw new TimeoutException("Test board did not become visible.");
            return ready.Task.GetAwaiter().GetResult();
        }
    }
}
'@
    try { $result.adapters = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, VideoProcessor) }
    catch { $result.adaptersError = $_.Exception.Message }
    $screen = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $width = $screen.Width - ($screen.Width % 2)
    $height = $screen.Height - ($screen.Height % 2)
    $result.display = [ordered]@{ width=$width; height=$height }
    $ffmpeg = Join-Path $tools 'ffmpeg.exe'
    $ffprobe = Join-Path $tools 'ffprobe.exe'
    $version = Invoke-ProbeProcess 'ffmpeg-version' $ffmpeg @('-version') 6
    $result.ffmpegVersion = $version.output.Split([char]10)[0].Trim()
    if ($compareGdi -or $productionCapture) {
        $options = Invoke-ProbeProcess 'gdi-fix-options' $ffmpeg @('-hide_banner','-h','demuxer=gdigrab') 6
        $optionText = $options.output + "`n" + $options.error
        if ($options.measurement.exitCode -ne 0 -or $optionText -notmatch '\buse_captureblt\b' -or $optionText -notmatch '\bhigh_precision\b') { throw 'The staged FFmpeg does not expose both candidate GDI options: use_captureblt and high_precision.' }
    }
    $encodersToProbe = $(if ($productionCapture) { @('h264_nvenc', 'h264_amf', 'h264_qsv') } else { @() })
    foreach ($encoder in $encodersToProbe) {
        $probe = Invoke-ProbeProcess ('probe-' + $encoder) $ffmpeg @('-nostdin','-hide_banner','-loglevel','error','-f','lavfi','-i','color=black:s=320x240:r=30','-frames:v','3','-an','-c:v',$encoder,'-f','null','-') 6
        $result.encoderProbes += [pscustomobject]@{ encoder=$encoder; available=($probe.measurement.exitCode -eq 0 -and -not $probe.measurement.timedOut); result=$probe.measurement; error=$probe.error }
    }
    $script:board = [KeepClipSandboxProbe.Board]::Start([bool](-not $ControlsOnly))
    if ($ControlsOnly) {
        $controlFps = 60
        $norm = '[0:v]scale=' + $width + ':' + $height + ':force_original_aspect_ratio=decrease,pad=' + $width + ':' + $height + ':(ow-iw)/2:(oh-ih)/2,format=nv12[v]'
        $syntheticInput = 'testsrc2=size=' + $width + 'x' + $height + ':rate=' + $controlFps + ',format=bgra'
        $phases = @(
            [pscustomobject]@{ name='control-a-encode-only'; label='A: Kodowanie CPU bez pobierania ekranu'; source='testsrc2'; encoder='libx264'; arguments=@('-nostdin','-hide_banner','-loglevel','warning','-re','-f','lavfi','-i',$syntheticInput,'-filter_complex',$norm,'-map','[v]','-an','-c:v','libx264','-preset','veryfast','-crf','20','-g',([string]$controlFps),'-t',([string]$DurationSeconds),'-progress','pipe:1','-nostats','-f','null','-') },
            [pscustomobject]@{ name='control-b-gdi-only'; label='B: Stare GDI (CAPTUREBLT, zwykly timer), bez H264'; source='gdigrab'; encoder='wrapped_avframe'; arguments=@('-nostdin','-hide_banner','-loglevel','warning','-f','gdigrab','-framerate',([string]$controlFps),'-thread_queue_size','128','-draw_mouse','0','-offset_x','0','-offset_y','0','-video_size',($width.ToString()+'x'+$height),'-i','desktop','-map','0:v:0','-an','-c:v','wrapped_avframe','-t',([string]$DurationSeconds),'-progress','pipe:1','-nostats','-f','null','-') }
        )
        foreach ($phase in $phases) {
            if ($script:board.WasClosed) { throw 'The test board was closed; controls cancelled.' }
            $script:board.Phase(($phase.label + ' / ' + $DurationSeconds + ' sekund'))
            $phaseStarted = [DateTime]::UtcNow.ToString('o')
            $control = Invoke-ProbeProcess $phase.name $ffmpeg $phase.arguments ([Math]::Min(12, $DurationSeconds + 2))
            $progress = Read-ProbeProgress $control.output
            $expectedFrames = $controlFps * $DurationSeconds
            $frameCountInRange = $null -ne $progress.frameCount -and $progress.frameCount -ge [Math]::Max(1, $expectedFrames - $controlFps) -and $progress.frameCount -le $expectedFrames + $controlFps
            $result.controls += [pscustomobject]@{ name=$phase.name; label=$phase.label; source=$phase.source; encoder=$phase.encoder; fps=$controlFps; startedUtc=$phaseStarted; finishedUtc=[DateTime]::UtcNow.ToString('o'); measurement=$control.measurement; progress=$progress; error=$control.error; expectedFrames=$expectedFrames; frameCountAllowance=$controlFps; frameCountAllowanceReason='One second of live-source startup/stop timing'; frameCountInRange=$frameCountInRange; cursorAssessment='Manual observation required; cursor flags do not detect flicker.' }
            if ($control.measurement.timedOut -or $control.measurement.cancelled) { throw 'Control was interrupted or exceeded its bounded timeout.' }
        }
    }
    $captureCases = if ($GdiFixProbe) {
        @([pscustomobject]@{ name='gdi-a-legacy'; label='A: Stare GDI (CAPTUREBLT, zwykly timer)'; fps=60; captureBlt=1; highPrecision=0 }, [pscustomobject]@{ name='gdi-b-no-captureblt'; label='B: GDI bez CAPTUREBLT (zwykly timer)'; fps=60; captureBlt=0; highPrecision=0 }, [pscustomobject]@{ name='gdi-c-fixed'; label='C: GDI bez CAPTUREBLT + precyzyjny timer'; fps=60; captureBlt=0; highPrecision=1 })
    } elseif ($GdiVfrProbe) {
        @([pscustomobject]@{ name='gdi-a-legacy'; label='A: Stare GDI (CAPTUREBLT, CFR)'; fps=60; captureBlt=1; highPrecision=0 }, [pscustomobject]@{ name='gdi-c-vfr'; label='C: GDI bez CAPTUREBLT, zapis rzeczywistych klatek'; fps=60; captureBlt=0; highPrecision=1 })
    } elseif ($DdaProbe) { @([pscustomobject]@{ name='dda-60'; label='DDA bez sztucznych klatek'; fps=60 }) }
    elseif ($ControlsOnly) { @() }
    else { @([pscustomobject]@{ name='cpu-30'; label='GDI produkcyjne (bez CAPTUREBLT + precyzyjny timer)'; fps=30; captureBlt=0; highPrecision=1 }, [pscustomobject]@{ name='cpu-60'; label='GDI produkcyjne (bez CAPTUREBLT + precyzyjny timer)'; fps=60; captureBlt=0; highPrecision=1 }) }
    foreach ($captureCase in $captureCases) {
        $fps = [int]$captureCase.fps
        if ($script:board.WasClosed) { throw 'The test board was closed; capture cancelled.' }
        $sourceName = $captureCase.label
        $script:board.Phase(($sourceName + ' + x264 / ' + $fps + ' FPS / ' + $DurationSeconds + ' sekund'))
        $segmentFolder = $captureCase.name
        $logKey = if ($compareGdi) { $captureCase.name } else { [string]$fps }
        $advancedChecks = $DdaProbe -or $compareGdi -or $productionCapture
        $segments = Join-Path $run $segmentFolder
        New-Item -ItemType Directory -Path $segments | Out-Null
        $filter = '[0:v]scale=' + $width + ':' + $height + ':force_original_aspect_ratio=decrease,pad=' + $width + ':' + $height + ':(ow-iw)/2:(oh-ih)/2,format=nv12[v]'
        $gdiOptions = if ($compareGdi -or $productionCapture) { @('-use_captureblt',([string]$captureCase.captureBlt),'-high_precision',([string]$captureCase.highPrecision)) } else { @() }
        if ($productionCapture -or $captureCase.name -in @('gdi-c-fixed','gdi-c-vfr')) { $gdiOptions += @('-fflags','autobsf') }
        $vfr = $productionCapture -or $captureCase.name -eq 'gdi-c-vfr'
        $timingOptions = if ($vfr) { @('-fps_mode:v','passthrough','-enc_time_base:v','filter','-bf','0','-forced-idr','1','-force_key_frames:v','expr:gte(t,n_forced*2)') } else { @() }
        $captureArguments = @('-nostdin','-hide_banner','-loglevel','warning','-f','gdigrab','-framerate',([string]$fps),'-thread_queue_size','128','-draw_mouse','0','-offset_x','0','-offset_y','0','-video_size',($width.ToString()+'x'+$height)) + $gdiOptions + @('-i','desktop','-filter_complex',$filter,'-map','[v]','-an','-c:v','libx264','-preset','veryfast','-crf','20','-g',([string]$fps)) + $timingOptions + @('-t',([string]$DurationSeconds),'-progress','pipe:1','-nostats','-f','segment','-segment_time','2','-reset_timestamps','1','-segment_format','mpegts',(Join-Path $segments 'seg_%06d.ts'))
        if ($DdaProbe) {
            $ddaFilter = 'ddagrab=framerate=' + $fps + ':draw_mouse=0:dup_frames=0,hwdownload,format=bgra,' + $filter.Substring(5)
            $captureArguments = @('-nostdin','-hide_banner','-loglevel','warning','-filter_complex',$ddaFilter,'-map','[v]','-an','-c:v','libx264','-preset','veryfast','-crf','20','-g',([string]$fps),'-force_key_frames','expr:gte(t,n_forced*2)','-fps_mode','passthrough','-enc_time_base','filter','-t',([string]$DurationSeconds),'-progress','pipe:1','-nostats','-f','segment','-segment_time','2','-reset_timestamps','1','-segment_format','mpegts',(Join-Path $segments 'seg_%06d.ts'))
        }
        $paintCountBefore = $script:board.PaintCount
        $boardTimeBefore = $script:board.ElapsedSeconds
        $capture = Invoke-ProbeProcess ($(if ($DdaProbe) { 'capture-dda-' } else { 'capture-' }) + $logKey) $ffmpeg $captureArguments ([Math]::Min(12, $DurationSeconds + 2))
        $paintCount = $script:board.PaintCount - $paintCountBefore
        $boardSeconds = $script:board.ElapsedSeconds - $boardTimeBefore
        $boardActivity = [pscustomobject]@{ paints=$paintCount; seconds=$boardSeconds; paintsPerSecond=$paintCount / [Math]::Max(0.001, $boardSeconds); highResolutionTimerRequested=[bool](-not $ControlsOnly); timerPeriodResult=$script:board.TimerPeriodResult }
        $captureProgress = Read-ProbeProgress $capture.output
        if ($advancedChecks) { $script:board.Phase(('Sprawdzanie plikow: ' + $captureCase.label + ' (nagrywanie zatrzymane)')) }
        $media = @()
        foreach ($segment in @(Get-ChildItem -LiteralPath $segments -Filter 'seg_*.ts' -File | Sort-Object Name)) {
            $probeArguments = @('-v','error','-count_frames','-show_entries','format=duration,size:stream=codec_type,codec_name,width,height,avg_frame_rate,nb_read_frames','-of','json',$segment.FullName)
            if ($advancedChecks) { $probeArguments = @('-v','error','-count_frames','-show_frames','-show_entries','format=duration,size:stream=codec_type,codec_name,width,height,avg_frame_rate,nb_read_frames:frame=best_effort_timestamp_time,pkt_duration_time','-of','json',$segment.FullName) }
            $probe = Invoke-ProbeProcess ('media-' + $logKey + '-' + $segment.BaseName) $ffprobe $probeArguments 6
            $details = $null
            if ($probe.measurement.exitCode -eq 0) { try { $details = $probe.output | ConvertFrom-Json } catch { } }
            $frameTimes = $null
            if ($advancedChecks -and $details) { $frameTimes = Get-FrameTimeSummary $details.frames; $details.PSObject.Properties.Remove('frames') }
            $decodeArguments = @('-nostdin','-hide_banner','-loglevel','error','-xerror','-i',$segment.FullName,'-map','0:v:0','-an','-f','null','-')
            if ($advancedChecks) { $decodeArguments = @('-nostdin','-hide_banner','-loglevel','error','-xerror','-i',$segment.FullName,'-map','0:v:0','-an','-fps_mode','passthrough','-enc_time_base','demux','-f','null','-') }
            $decode = Invoke-ProbeProcess ('decode-' + $logKey + '-' + $segment.BaseName) $ffmpeg $decodeArguments 6
            $frameHashes = @()
            $hashExitCode = $null
            if ($advancedChecks) {
                $hashes = Invoke-ProbeProcess ('frame-hashes-' + $logKey + '-' + $segment.BaseName) $ffmpeg @('-nostdin','-hide_banner','-loglevel','error','-i',$segment.FullName,'-map','0:v:0','-an','-vf','scale=64:36,format=gray','-fps_mode','passthrough','-enc_time_base','demux','-f','framemd5','-') 6
                $hashExitCode = $hashes.measurement.exitCode
                $frameHashes = @($hashes.output -split '\r?\n' | Where-Object { $_ -match '^\s*\d+,' } | ForEach-Object { ($_ -split ',')[-1].Trim() } | Where-Object { $_ -match '^[0-9a-fA-F]{32}$' })
            }
            $media += [pscustomobject]@{ file=$segmentFolder+'/'+$segment.Name; bytes=$segment.Length; exitCode=$probe.measurement.exitCode; media=$details; error=$probe.error; decodeExitCode=$decode.measurement.exitCode; decodeTimedOut=$decode.measurement.timedOut; frameTimes=$frameTimes; frameHashExitCode=$hashExitCode; decodedFrameHashes=$frameHashes }
        }
        $frames = 0L
        $invalidMedia = 0
        foreach ($segmentResult in $media) {
            if ($null -eq $segmentResult.media) { $invalidMedia++; continue }
            $video = @($segmentResult.media.streams | Where-Object { $_.codec_type -eq 'video' -and $_.codec_name -eq 'h264' -and $_.width -eq $width -and $_.height -eq $height })
            if ($video.Count -ne 1 -or $video[0].nb_read_frames -notmatch '^\d+$') { $invalidMedia++; continue }
            $frames += [long]$video[0].nb_read_frames
        }
        $expectedFrames = $fps * $DurationSeconds
        $frameCountInRange = $frames -ge [Math]::Max(1, $expectedFrames - $fps) -and $frames -le $expectedFrames + $fps
        $distinctHashes = @($media | ForEach-Object { $_.decodedFrameHashes } | Sort-Object -Unique).Count
        $varyingContent = $advancedChecks -and $distinctHashes -gt 1
        $target = Get-CaptureTargetSummary $captureProgress $frames $fps $DurationSeconds $boardActivity
        $result.captures += [pscustomobject]@{ name=$captureCase.name; label=$captureCase.label; fps=$fps; encoder='libx264'; useCaptureBlt=$captureCase.captureBlt; highPrecision=$captureCase.highPrecision; baseline=($compareGdi -and $captureCase.name -notin @('gdi-c-fixed','gdi-c-vfr')); targetStatus=$(if ($target.passed) { 'passed' } else { 'failed' }); target=$target; measurement=$capture.measurement; progress=$captureProgress; boardActivity=$boardActivity; segments=$media; frameCount=$frames; expectedFrames=$expectedFrames; frameCountAllowance=$fps; frameCountAllowanceReason='One second of live-source startup/stop timing; a source below target FPS remains a failed target-FPS test'; frameCountInRange=$frameCountInRange; invalidMedia=$invalidMedia; distinctDecodedFrameHashes=$distinctHashes; varyingDecodedContent=$varyingContent; contentAssessment=$(if ($advancedChecks) { 'Downscaled grayscale frame hashes must vary; not a semantic scene or cursor-flicker detector.' } else { 'Not checked in original GDI reproduction.' }) }
        if ($vfr) {
            $timing = Get-ReplayTimingSummary $media $DurationSeconds
            $clip = Test-SavedReplay $media $frames $DurationSeconds $logKey
            $badSegments = @($media | Where-Object { $_.exitCode -ne 0 -or $_.decodeExitCode -ne 0 -or $_.frameHashExitCode -ne 0 -or $_.frameTimes.count -lt 1 -or $_.frameTimes.nonIncreasingCount -gt 0 })
            $replayPassed = $capture.measurement.exitCode -eq 0 -and $captureProgress.state -eq 'end' -and $target.noArtificialDuplicates -and $captureProgress.droppedFrames -eq 0 -and $captureProgress.frameCount -eq $frames -and $varyingContent -and $invalidMedia -eq 0 -and $badSegments.Count -eq 0 -and $timing.passed -and $clip.passed
            $replay = [pscustomobject]@{ status=$(if ($replayPassed) { 'passed' } else { 'failed' }); fpsTargetStatus=$(if ($target.passed) { 'passed' } else { 'failed' }); segmentTiming=$timing; savedClip=$clip; noArtificialDuplicates=$target.noArtificialDuplicates }
            $result.captures[-1] | Add-Member -NotePropertyName vfrReplay -NotePropertyValue $replay
            if ($GdiVfrProbe) { $result.vfrReplay = $replay }
        }
        if ($capture.measurement.timedOut -or $capture.measurement.cancelled) { throw 'Capture was interrupted or exceeded its bounded timeout.' }
    }
} catch {
    $result.error = $_.Exception.ToString()
} finally {
    if ($script:board) {
        try { $script:board.Finish() }
        catch { if (-not $result.error) { $result.error = 'Cannot close the owned test board: ' + $_.Exception.Message } }
    }
    $result.finishedUtc = [DateTime]::UtcNow.ToString('o')
    $result.processes = @($script:runs.ToArray())
    $failedCaptures = @($result.captures | Where-Object { $_.measurement.exitCode -ne 0 -or $_.segments.Count -eq 0 -or $_.invalidMedia -gt 0 -or -not $_.frameCountInRange -or @($_.segments | Where-Object { $_.exitCode -ne 0 -or $null -eq $_.media -or $_.decodeExitCode -ne 0 -or $_.decodeTimedOut }).Count })
    $failedControls = @($result.controls | Where-Object { $_.measurement.exitCode -ne 0 -or $_.measurement.timedOut -or $_.measurement.cancelled -or $_.progress.state -ne 'end' -or -not $_.frameCountInRange })
    $failedDdaChecks = @($result.captures | Where-Object { $DdaProbe -and (-not $_.varyingDecodedContent -or $_.progress.duplicateFrames -ne 0 -or $_.progress.state -ne 'end' -or @($_.segments | Where-Object { $_.frameHashExitCode -ne 0 -or $null -eq $_.frameTimes -or $_.frameTimes.count -lt 1 -or $_.frameTimes.nonIncreasingCount -gt 0 }).Count) })
    $failedGdiFixChecks = @($result.captures | Where-Object { ($GdiFixProbe -or $productionCapture) -and (-not $_.varyingDecodedContent -or $_.progress.state -ne 'end' -or (-not $_.baseline -and -not $_.target.passed) -or @($_.segments | Where-Object { $_.frameHashExitCode -ne 0 -or $null -eq $_.frameTimes -or $_.frameTimes.count -lt 1 -or $_.frameTimes.nonIncreasingCount -gt 0 }).Count) })
    $complete = $(if ($GdiFixProbe) { $result.captures.Count -eq 3 } elseif ($DdaProbe) { $result.captures.Count -eq 1 } elseif ($ControlsOnly) { $result.controls.Count -eq 2 -and $result.captures.Count -eq 0 } else { $result.captures.Count -eq 2 })
    $vfrFailed = @($result.captures | Where-Object { ($productionCapture -or ($GdiVfrProbe -and -not $_.baseline)) -and ($_.vfrReplay.status -ne 'passed' -or $_.vfrReplay.fpsTargetStatus -ne 'passed') }).Count -gt 0
    $result.exitCode = $(if ($result.error -or $failedCaptures.Count -gt 0 -or $failedControls.Count -gt 0 -or $failedDdaChecks.Count -gt 0 -or $failedGdiFixChecks.Count -gt 0 -or $vfrFailed -or -not $complete) { 1 } else { 0 })
    $result.status = $(if ($result.exitCode -eq 0) { 'passed' } else { 'failed' })
    [IO.File]::WriteAllText((Join-Path $run 'results.json'), ($result | ConvertTo-Json -Depth 15), $utf8)
    if ($GdiVfrProbe -or $productionCapture) { & (Join-Path $tools 'recording-sandbox-report.ps1') -ResultPath (Join-Path $run 'results.json') }
    [IO.File]::WriteAllText((Join-Path $resultsRoot 'latest-sandbox-run.txt'), $run, $utf8)
    Write-Host ('Sandbox test results: ' + $run)
}
exit $result.exitCode
