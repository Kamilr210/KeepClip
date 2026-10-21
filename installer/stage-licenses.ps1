[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [string]$AssetsFile
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$Manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'licenses\manifest.json') -Raw | ConvertFrom-Json
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir 'KeepClip.deps.json'))) {
    throw 'Brak KeepClip.deps.json w katalogu publikacji.'
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'licenses') -File -Recurse) {
    $relative = $file.FullName.Substring($RepoRoot.Length+1)
    $destination = Join-Path $PublishDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md') -Destination $PublishDir -Force
$ffmpegDir = Join-Path $PublishDir 'licenses\ffmpeg-build'
New-Item -ItemType Directory -Force -Path $ffmpegDir | Out-Null
$provenancePath = Join-Path $PublishDir 'tools\ffmpeg-notices\provenance.json'
$binaries = @()
$complete = $false
if (Test-Path -LiteralPath $provenancePath) {
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    if ($provenance.schemaVersion -ne 2) { throw 'Stary FFmpeg bez kompletnego pakietu zrodel. Uruchom setup.ps1 -Force.' }
    & (Join-Path $RepoRoot 'build\ffmpeg\verify.ps1') -BinDir (Join-Path $PublishDir 'tools\bin') -NoticesDir (Split-Path $provenancePath)
    foreach ($file in @($provenance.files.file)+@('provenance.json')) {
        $destination=Join-Path $ffmpegDir $file
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path (Split-Path $provenancePath) $file) -Destination $destination -Force
    }
    $complete=$true
} else {
    foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
        $file=Join-Path $PublishDir ('tools\bin\'+$name)
        if (Test-Path -LiteralPath $file) {
            $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            $entry=@($Manifest.localFfmpegObservation|Where-Object {$_.file -eq $name -and $_.sha256 -eq $hash})
            if ($entry.Count -ne 1) { throw "Nieznany build $name ($hash). Uruchom setup.ps1 -Force." }
            $binaries+=$entry[0]
        }
    }
    if ($binaries.Count -eq 1) { throw 'Publikacja zawiera tylko jeden z plikow ffmpeg.exe / ffprobe.exe.' }
    @{schemaVersion=1;binaries=$binaries;correspondingSourceStatus='unverified';distributionAllowed=$false;details='licenses/FFMPEG-SOURCE.md'} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $ffmpegDir 'provenance.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'licenses\third-party\ffmpeg\COPYING.GPLv3') -Destination $ffmpegDir -Force
}
& (Join-Path $PSScriptRoot 'verify-licenses.ps1') -PublishDir $PublishDir -AssetsFile $AssetsFile
if ($complete) {
    $generated=Join-Path $PublishDir 'tools\ffmpeg-notices'
    if (-not ([IO.Path]::GetFullPath($generated)).StartsWith($PublishDir.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Nieprawidlowa sciezka notices w publikacji.' }
    Remove-Item -LiteralPath $generated -Recurse -Force
}
Write-Host 'KeepClip: dolaczono THIRD-PARTY-NOTICES.md, licenses/ i pasujace materialy FFmpeg.'
