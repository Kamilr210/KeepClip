[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $PSScriptRoot 'probes\wgc'
$run = Join-Path $repo ('TestResults\wgc-sandbox\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$inputPath = Join-Path $run 'input'
$outputPath = Join-Path $run 'output'
$runtime = Join-Path $inputPath 'runtime'
$dotnet = (Get-Command dotnet.exe -CommandType Application | Select-Object -First 1).Source
$dotnetRoot = Split-Path -Parent $dotnet
$frameworks = @('host\fxr','shared\Microsoft.NETCore.App','shared\Microsoft.WindowsDesktop.App')
$version = Get-ChildItem -Directory -LiteralPath (Join-Path $dotnetRoot $frameworks[0]) | Where-Object {
    $_.Name -match '^10\.0\.\d+$' -and
        (Test-Path -LiteralPath (Join-Path $dotnetRoot ($frameworks[1] + '\' + $_.Name))) -and
        (Test-Path -LiteralPath (Join-Path $dotnetRoot ($frameworks[2] + '\' + $_.Name)))
} | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $version) { throw 'A matching local .NET 10 host, Core and Windows Desktop runtime are required.' }
New-Item -ItemType Directory -Path $runtime, (Join-Path $inputPath 'probe'), $outputPath -ErrorAction Stop | Out-Null
$build = Join-Path $run 'build'
& $dotnet build (Join-Path $source 'WgcProbe.csproj') -c Release --artifacts-path (Join-Path $run 'artifacts') -o $build --nologo
if ($LASTEXITCODE -ne 0) { throw "WGC probe build exited with $LASTEXITCODE." }
foreach ($name in @('WgcProbe.dll','WgcProbe.deps.json','WgcProbe.runtimeconfig.json','Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll')) {
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $inputPath 'probe')
}
Copy-Item -LiteralPath $dotnet -Destination $runtime
foreach ($item in $frameworks) {
    $parent = New-Item -ItemType Directory -Path (Join-Path $runtime $item)
    Copy-Item -LiteralPath (Join-Path $dotnetRoot ($item + '\' + $version)) -Destination $parent.FullName -Recurse
}
foreach ($name in @('run-probe.ps1','measure-result.ps1')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $inputPath }
$configuration = Join-Path $run 'KeepClip-WGC-Probe.wsb'
$xml = @'
<Configuration>
  <VGpu>Default</VGpu>
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
  <LogonCommand><Command>powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\KeepClipProbe\Input\run-probe.ps1</Command></LogonCommand>
</Configuration>
'@
[IO.File]::WriteAllText($configuration, $xml.Replace('__INPUT__', [Security.SecurityElement]::Escape($inputPath)).Replace('__OUTPUT__', [Security.SecurityElement]::Escape($outputPath)))
Write-Host ('Prepared (open manually): ' + $configuration)
Write-Host ('Results: ' + $outputPath)
