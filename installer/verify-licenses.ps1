[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [string]$AssetsFile,
    [switch]$RequireFfmpeg,
    [switch]$ForDistribution
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'licenses\manifest.json') -Raw | ConvertFrom-Json
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
function Assert-File($relative, $expectedHash) {
    $path = Join-Path $PublishDir $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Brak wymaganego materialu: $relative" }
    if ($expectedHash -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expectedHash) { throw "Material rozni sie od zweryfikowanego oryginalu: $relative" }
}
foreach ($material in $Manifest.materials) { Assert-File $material.path $material.sha256 }
foreach ($relative in @('THIRD-PARTY-NOTICES.md','licenses\manifest.json','licenses\README.md','licenses\FFMPEG-SOURCE.md')) {
    Assert-File $relative (Get-FileHash -LiteralPath (Join-Path $RepoRoot $relative) -Algorithm SHA256).Hash
}
if ($AssetsFile) {
    $assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
    $expected = @($Manifest.packages | ForEach-Object {$_.id+'/'+$_.version} | Sort-Object)
    $actual = @($assets.libraries.PSObject.Properties | Where-Object {$_.Value.type -eq 'package'} | ForEach-Object {$_.Name} | Sort-Object)
    if (@(Compare-Object $expected $actual).Count -gt 0) { throw 'Zmiana zaleznosci NuGet: odswiez licenses/manifest.json i oficjalne licencje/notices.' }
    foreach ($package in $Manifest.packages) {
        if ($assets.libraries.($package.id+'/'+$package.version).sha512 -ne $package.nugetContentHash) { throw "Zmienila sie zawartosc pakietu $($package.id)." }
    }
}
$deps = Get-Content -LiteralPath (Join-Path $PublishDir 'KeepClip.deps.json') -Raw | ConvertFrom-Json
foreach ($lib in $deps.libraries.PSObject.Properties) {
    if ($lib.Value.type -eq 'project') { continue }
    $parts = $lib.Name -split '/'
    if ($parts[0].StartsWith('runtimepack.')) {
        $known = @($Manifest.runtimePacks | Where-Object {$_.id -eq $parts[0].Substring(12) -and $_.version -eq $parts[1]})
    } elseif ($lib.Value.type -eq 'reference' -and $parts[0] -in @('Microsoft.Web.WebView2.Core','Microsoft.Web.WebView2.WinForms')) {
        $known = @($Manifest.packages | Where-Object {$_.id -eq 'Microsoft.Web.WebView2' -and $_.version -eq $parts[1]})
    } else {
        $known = @($Manifest.packages | Where-Object {$_.id -eq $parts[0] -and $_.version -eq $parts[1]})
    }
    if ($known.Count -ne 1) { throw "Brak zweryfikowanych licencji dla $($lib.Name). Odswiez licenses/." }
    foreach ($relative in $known[0].licenseFiles) { Assert-File $relative $null }
}
foreach ($native in $Manifest.nativeAssets) { Assert-File $native.path $native.sha256 }
$ffmpegPath = Join-Path $PublishDir 'licenses\ffmpeg-build\provenance.json'
Assert-File 'licenses\ffmpeg-build\COPYING.GPLv3' (Get-FileHash -LiteralPath (Join-Path $RepoRoot 'licenses\third-party\ffmpeg\COPYING.GPLv3') -Algorithm SHA256).Hash
Assert-File 'licenses\ffmpeg-build\provenance.json' $null
$ffmpeg = Get-Content -LiteralPath $ffmpegPath -Raw | ConvertFrom-Json
if ($RequireFfmpeg -and @($ffmpeg.binaries).Count -ne 2) { throw 'Brak ffmpeg.exe / ffprobe.exe oraz ich identyfikacji w publikacji.' }
foreach ($binary in $ffmpeg.binaries) { Assert-File ('tools\bin\'+$binary.file) $binary.sha256 }
if ($ffmpeg.schemaVersion -eq 2) {
    & (Join-Path $RepoRoot 'build\ffmpeg\verify.ps1') -BinDir (Join-Path $PublishDir 'tools\bin') -NoticesDir (Split-Path $ffmpegPath)
} elseif ($ForDistribution) {
    throw 'DYSTRYBUCJA ZABLOKOWANA: FFmpeg nie pochodzi z przypietych, dolaczonych zrodel. Uruchom setup.ps1 -Force. Zobacz licenses/FFMPEG-SOURCE.md.'
}
Write-Host "Zweryfikowano licencje i notices: $($Manifest.packages.Count) pakietow NuGet, $($Manifest.runtimePacks.Count) runtime packs oraz hashe natywnych bibliotek."
