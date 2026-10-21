[CmdletBinding()]
param([switch]$Force,[string]$BuildArtifactDir)
$ErrorActionPreference='Stop'
$ToolsBin=Join-Path $PSScriptRoot 'tools\bin'
$NoticesDir=Join-Path $PSScriptRoot 'tools\ffmpeg-notices'
$Verify=Join-Path $PSScriptRoot 'build\ffmpeg\verify.ps1'
$TempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$WorkDir=Join-Path $TempRoot ('keepclip-ffmpeg-'+[Guid]::NewGuid().ToString('N'))
try {
    if ((Test-Path -LiteralPath (Join-Path $ToolsBin 'ffmpeg.exe')) -and (Test-Path -LiteralPath (Join-Path $ToolsBin 'ffprobe.exe')) -and -not $Force) {
        & $Verify -BinDir $ToolsBin -NoticesDir $NoticesDir
        Write-Host 'Istniejacy FFmpeg ma pasujace zrodla i notices. -Force odbudowuje/wymienia narzedzia.'
        return
    }
    if (-not $BuildArtifactDir) {
        if (-not (Get-Command node -ErrorAction SilentlyContinue) -or -not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Budowa FFmpeg wymaga Node 20+ oraz Docker z kontenerami Linux. W CI artefakt tworzy job ffmpeg.' }
        $BuildArtifactDir=Join-Path $WorkDir 'artifact'
        & node (Join-Path $PSScriptRoot 'build\ffmpeg\build.mjs') --output $BuildArtifactDir
        if ($LASTEXITCODE -ne 0) { throw "Budowa FFmpeg zwrocila kod $LASTEXITCODE. Dotychczasowe narzedzia pozostaja bez zmian." }
    }
    $BuildArtifactDir=[IO.Path]::GetFullPath($BuildArtifactDir)
    $Bin=Join-Path $BuildArtifactDir 'bin'
    $Notices=Join-Path $BuildArtifactDir 'notices'
    & $Verify -BinDir $Bin -NoticesDir $Notices
    New-Item -ItemType Directory -Force -Path $ToolsBin,$NoticesDir | Out-Null
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) { Copy-Item -LiteralPath (Join-Path $Bin $name) -Destination (Join-Path $ToolsBin $name) -Force }
    foreach ($file in Get-ChildItem -LiteralPath $Notices -File -Recurse) {
        $relative=$file.FullName.Substring($Notices.Length+1)
        $destination=Join-Path $NoticesDir $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    & $Verify -BinDir $ToolsBin -NoticesDir $NoticesDir
    Write-Host 'Gotowe: FFmpeg 9.0.2 z odpowiadajacymi zrodlami, zaleznosciami, skryptami i licencjami.' -ForegroundColor Green
} catch {
    Write-Host "BLAD: $($_.Exception.Message)" -ForegroundColor Red
    if (-not $Force) { Write-Host 'Stary lub nieznany FFmpeg: uruchom setup.ps1 -Force, aby jawnie go wymienic.' }
    exit 1
} finally {
    $resolved=[IO.Path]::GetFullPath($WorkDir)
    if ($resolved.StartsWith($TempRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
