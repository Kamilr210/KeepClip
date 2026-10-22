[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BinDir,[Parameter(Mandatory=$true)][string]$NoticesDir)
$ErrorActionPreference='Stop'
$Lock=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'sources.lock.json') -Raw | ConvertFrom-Json
$Provenance=Get-Content -LiteralPath (Join-Path $NoticesDir 'provenance.json') -Raw | ConvertFrom-Json
if ($Provenance.schemaVersion -ne 2 -or $Provenance.buildId -ne $Lock.buildId -or $Provenance.correspondingSourceStatus -ne 'built-from-bundled-sources') { throw 'Niezweryfikowany FFmpeg: wymagany build z przypietych, dolaczonych zrodel.' }
function Assert-Hash([string]$Base,[string]$Relative,[string]$Hash) {
    $root=[IO.Path]::GetFullPath($Base).TrimEnd('\')+'\'
    $file=[IO.Path]::GetFullPath((Join-Path $Base $Relative))
    if (-not $file.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw 'Nieprawidlowa sciezka w identyfikacji FFmpeg.' }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Hash) { throw "Brak lub zmiana FFmpeg: $Relative" }
}
if (@($Provenance.binaries).Count -ne 2) { throw 'Wymagane dokladnie ffmpeg.exe i ffprobe.exe.' }
foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
    $entry=@($Provenance.binaries|Where-Object {$_.file -eq $name})
    if ($entry.Count -ne 1) { throw "Brak identyfikacji $name." }
    Assert-Hash $BinDir $name $entry[0].sha256
}
foreach ($entry in $Provenance.files) { Assert-Hash $NoticesDir $entry.file $entry.sha256 }
foreach ($source in $Lock.sources) { Assert-Hash $NoticesDir $source.bundlePath $source.sha256 }
foreach ($recipe in @('Dockerfile','build.sh','build.mjs','docker-cli.mjs','sources.lock.json','README.md')) {
    Assert-Hash $NoticesDir ('build/'+$recipe) (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $recipe) -Algorithm SHA256).Hash
}
foreach ($required in @('COPYING.GPLv3','COPYING.GPLv2','LICENSE.md','X264-COPYING','AMF-LICENSE.txt','LIBVPL-LICENSE','LIBVPL-THIRD-PARTY-PROGRAMS.txt','MINGW-COPYRIGHT','GCC-COPYRIGHT','NV-CODEC-LICENSES.txt','build/config.log','build/config.h','build/config_components.h','build/toolchain-packages.tsv','build/ffmpeg-imports.txt','build/ffprobe-imports.txt')) {
    if (@($Provenance.files|Where-Object {$_.file -eq $required}).Count -ne 1) { throw "Brak wymaganego notice / konfiguracji FFmpeg: $required" }
}
$toolchain=Get-Content -LiteralPath (Join-Path $NoticesDir 'build/toolchain-packages.tsv') -Raw
foreach($package in @('mingw-w64-common','mingw-w64-x86-64-dev')) {
    if($toolchain -notmatch ('(?m)^'+[regex]::Escape($package)+'\t10\.0\.0-3\r?$')) { throw 'Wersja CRT/winpthreads nie odpowiada dolaczonym zrodlom MinGW 10.0.0-3.' }
}
$settings=Get-Content -LiteralPath (Join-Path $NoticesDir 'build/config.h') -Raw
foreach($setting in @('GPL','VERSION3','LIBX264','LIBVPL','AMF','FFNVCODEC','CUDA_LLVM')) {
    if($settings -notmatch ('#define CONFIG_'+$setting+' 1\b')) { throw "Nieprawidlowa konfiguracja FFmpeg: $setting" }
}
if($settings -notmatch '#define CONFIG_NONFREE 0\b'){throw 'Build FFmpeg nie moze zawierac nonfree.'}
$config=Get-Content -LiteralPath (Join-Path $NoticesDir 'build/config_components.h') -Raw
foreach ($component in @('LIBX264_ENCODER','H264_NVENC_ENCODER','H264_AMF_ENCODER','H264_QSV_ENCODER','AAC_ENCODER','GDIGRAB_INDEV','DDAGRAB_FILTER','DYNAUDNORM_FILTER','SCALE_CUDA_FILTER')) {
    if ($config -notmatch ('#define CONFIG_'+$component+' 1\b')) { throw "Build FFmpeg nie zawiera wymaganego $component." }
}
$systemDlls=@('ADVAPI32.dll','AVICAP32.dll','bcrypt.dll','CRYPT32.dll','d3d11.dll','dxgi.dll','GDI32.dll','KERNEL32.dll','msvcrt.dll','ncrypt.dll','ole32.dll','OLEAUT32.dll','PSAPI.DLL','Secur32.dll','SHELL32.dll','USER32.dll','VERSION.dll','WS2_32.dll','WINMM.dll','cfgmgr32.dll','SETUPAPI.dll','SHLWAPI.dll','IMM32.dll')
foreach ($file in @('ffmpeg-imports.txt','ffprobe-imports.txt')) {
    foreach ($line in Get-Content -LiteralPath (Join-Path $NoticesDir ('build/'+$file))) {
        if ($line -match 'DLL Name:\s*(\S+)' -and $Matches[1] -notin $systemDlls) { throw "Nieznana biblioteka FFmpeg: $($Matches[1]). Wymaga zrodel/licencji i przegladu." }
    }
}
Write-Host 'FFmpeg: zgodne binaria, komplet przypietych zrodel, skrypty, notices i wymagane funkcje.'
