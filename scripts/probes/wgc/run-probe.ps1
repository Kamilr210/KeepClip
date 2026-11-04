$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This probe can only run in Windows Sandbox as WDAGUtilityAccount.' }
$env:DOTNET_ROOT = Join-Path $PSScriptRoot 'runtime'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
$probe = Join-Path $PSScriptRoot 'probe\WgcProbe.dll'
$output = 'C:\KeepClipProbe\Output'
$cases = @()
foreach ($target in @('monitor','window')) {
    foreach ($interval in @('default','8','16')) {
        $case = "$target-$interval"
        $directory = Join-Path $output $case
        $process = $null
        try {
            New-Item -ItemType Directory -Path $directory -ErrorAction Stop | Out-Null
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $dotnet
            $start.Arguments = ('"{0}" --out "{1}" --target {2} --duration 10 --min-update-ms {3}' -f $probe, $directory, $target, $interval)
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $process = [Diagnostics.Process]::Start($start)
            if (-not $process.WaitForExit(25000)) { $process.Kill(); throw 'Probe exceeded 25 seconds.' }
            $metric = & (Join-Path $PSScriptRoot 'measure-result.ps1') -Directory $directory
            if ($process.ExitCode -ne 0 -and $metric.status -eq 'measured') { throw "Probe exited with $($process.ExitCode)." }
        } catch { $metric = [pscustomobject]@{ status='failed'; freshFrameTargetMet=$null; error=$_.Exception.Message } }
        finally { if ($null -ne $process) { $process.Dispose() } }
        $cases += [pscustomobject]@{ name=$case; measurement=$metric }
        [IO.File]::WriteAllText((Join-Path $output 'summary.json'), (ConvertTo-Json -InputObject $cases -Depth 6))
    }
}
