[CmdletBinding()]
param([ValidateSet('Default', 'Disable')][string]$Vgpu = 'Default', [ValidateRange(3,12)][int]$DurationSeconds = 10, [switch]$ControlsOnly, [switch]$DdaProbe, [switch]$GdiFixProbe, [switch]$GdiVfrProbe, [string]$ToolsDirectory)

$ErrorActionPreference = 'Stop'
$modes = @('ControlsOnly','DdaProbe','GdiFixProbe','GdiVfrProbe' | Where-Object { $PSBoundParameters[$_] })
if ($modes.Count -gt 1) { throw 'Choose only one probe mode.' }
$mode = if ($modes.Count) { $modes[0] } else { 'Capture' }
if ($mode -in @('DdaProbe','GdiFixProbe','GdiVfrProbe') -and $DurationSeconds -gt 10) { throw 'Candidate capture probes are limited to 10 seconds per phase.' }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $repo 'tools\bin' }
$ToolsDirectory = [IO.Path]::GetFullPath($ToolsDirectory)
$root = Join-Path $repo ('TestResults\recording-sandbox\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$inputPath = Join-Path $root 'input'
$outputPath = Join-Path $root 'output'
$files = @(
    @{ name = 'ffmpeg.exe'; source = (Join-Path $ToolsDirectory 'ffmpeg.exe') },
    @{ name = 'ffprobe.exe'; source = (Join-Path $ToolsDirectory 'ffprobe.exe') },
    @{ name = 'recording-sandbox-probe.ps1'; source = (Join-Path $PSScriptRoot 'recording-sandbox-probe.ps1') },
    @{ name = 'recording-sandbox-checks.ps1'; source = (Join-Path $PSScriptRoot 'recording-sandbox-checks.ps1') },
    @{ name = 'recording-sandbox-report.ps1'; source = (Join-Path $PSScriptRoot 'recording-sandbox-report.ps1') },
    @{ name = 'ReplayCaptureCommand.cs'; source = (Join-Path $repo 'KeepClip\Infrastructure\ReplayCaptureCommand.cs') }
)
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file.source -PathType Leaf)) { throw ('Missing probe input: ' + $file.source) } }
New-Item -ItemType Directory -Path $inputPath, $outputPath -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath $file.source -Destination (Join-Path $inputPath $file.name) }
$manifest = [pscustomobject]@{
    schemaVersion = 1
    purpose = 'Fresh Windows Sandbox, own synthetic test board and an explicitly selected capture experiment; no host desktop or microphone.'
    requestedVgpu = $Vgpu
    mode = $mode
    source = 'Capture tests production GDI options. ControlsOnly isolates encoding, DdaProbe tests DDA, GdiFixProbe compares GDI input flags, and GdiVfrProbe compares legacy CFR with a timestamp-preserving GDI replay. These tests do not launch the complete KeepClip host.'
    files = @($files | ForEach-Object { [pscustomobject]@{ name = $_.name; sha256 = (Get-FileHash -LiteralPath (Join-Path $inputPath $_.name) -Algorithm SHA256).Hash } })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $inputPath 'manifest.json') -Encoding UTF8
$bootstrap = @'
$ErrorActionPreference = 'Stop'
try {
    & 'C:\KeepClipProbe\Input\recording-sandbox-probe.ps1' -InputDirectory 'C:\KeepClipProbe\Input' -OutputDirectory 'C:\KeepClipProbe\Output' -ManifestPath 'C:\KeepClipProbe\Input\manifest.json' -DurationSeconds __SECONDS__ __MODE__ *> 'C:\KeepClipProbe\Output\bootstrap.log'
} catch {
    $_ | Out-File -LiteralPath 'C:\KeepClipProbe\Output\bootstrap.log' -Append -Encoding UTF8
    exit 1
} finally {
    [IO.File]::WriteAllText('C:\KeepClipProbe\Output\finished.txt', [DateTime]::UtcNow.ToString('o'))
}
'@
[IO.File]::WriteAllText((Join-Path $inputPath 'start-probe.ps1'), $bootstrap.Replace('__SECONDS__', [string]$DurationSeconds).Replace('__MODE__', $(if ($modes.Count) { '-' + $mode } else { '' })), [Text.UTF8Encoding]::new($false))
$xml = @'
<Configuration>
  <VGpu>__VGPU__</VGpu>
  <Networking>Disable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>__INPUT__</HostFolder><SandboxFolder>C:\KeepClipProbe\Input</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>__OUTPUT__</HostFolder><SandboxFolder>C:\KeepClipProbe\Output</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\KeepClipProbe\Input\start-probe.ps1</Command></LogonCommand>
</Configuration>
'@
$xml = $xml.Replace('__VGPU__', $Vgpu).Replace('__INPUT__', [Security.SecurityElement]::Escape($inputPath)).Replace('__OUTPUT__', [Security.SecurityElement]::Escape($outputPath))
$configuration = Join-Path $root 'KeepClip-Recording-Probe.wsb'
[IO.File]::WriteAllText($configuration, $xml, [Text.UTF8Encoding]::new($false))
Write-Host ('Prepared: ' + $configuration)
Write-Host ('Results: ' + $outputPath)
Write-Host 'Open the WSB file to run the synthetic probe in a new visible Sandbox. This script does not launch it or enable Windows features.'
