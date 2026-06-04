<#
  KeepClip - setup.ps1
  Pobiera zaleznosci, ktorych NIE ma (i nie moze byc) w repozytorium git:
    - ffmpeg.exe + ffprobe.exe  ->  tools\bin\
      (oficjalny build Windows z gyan.dev; pliki >100 MB, wiec GitHub ich nie przyjmuje)

  Czego ten skrypt NIE robi (bo dzieje sie samo):
    - Model Whisper (~1.6 GB) pobiera sie sam przy PIERWSZEJ transkrypcji.
    - Folder data\ (baza, miniatury, ustawienia) tworzy sie sam przy starcie aplikacji.

  Uzycie (w folderze repo):
      powershell -ExecutionPolicy Bypass -File setup.ps1
      ...\setup.ps1 -Force     # wymus ponowne pobranie ffmpeg, nawet gdy juz jest
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

$RepoRoot = $PSScriptRoot
$ToolsBin = Join-Path $RepoRoot 'tools\bin'
$Ffmpeg   = Join-Path $ToolsBin 'ffmpeg.exe'
$Ffprobe  = Join-Path $ToolsBin 'ffprobe.exe'
$Url      = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

try {
  # --- 1. Sprawdz .NET 10 SDK (wymagane do `dotnet run` / `dotnet build`) ---
  Step "Sprawdzam .NET SDK"
  $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
  if (-not $dotnet) {
    Write-Warning "Nie znaleziono 'dotnet'. Zainstaluj .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
  } else {
    $sdks = & dotnet --list-sdks
    if ($sdks -match '^10\.') {
      Write-Host "OK - wykryto .NET 10 SDK."
    } else {
      Write-Warning "Jest 'dotnet', ale bez SDK 10.x. Zainstaluj .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0"
      Write-Host ("Wykryte SDK:`n" + ($sdks -join "`n"))
    }
  }

  # --- 2. ffmpeg / ffprobe -> tools\bin ---
  Step "Sprawdzam ffmpeg / ffprobe"
  if ((Test-Path $Ffmpeg) -and (Test-Path $Ffprobe) -and -not $Force) {
    Write-Host "Juz sa w tools\bin - pomijam (uzyj -Force, by pobrac ponownie)."
  } else {
    New-Item -ItemType Directory -Force -Path $ToolsBin | Out-Null
    $zip = Join-Path $env:TEMP 'keepclip-ffmpeg.zip'
    $ext = Join-Path $env:TEMP 'keepclip-ffmpeg'
    Write-Host "Pobieram ffmpeg (oficjalny build Windows z gyan.dev)..."
    try {
      Invoke-WebRequest -Uri $Url -OutFile $zip
    } catch {
      throw "Nie udalo sie pobrac ffmpeg z $Url ($($_.Exception.Message)). Sprawdz internet albo pobierz recznie i wrzuc ffmpeg.exe + ffprobe.exe do tools\bin\."
    }
    Write-Host "Rozpakowuje..."
    if (Test-Path $ext) { Remove-Item -Recurse -Force $ext }
    Expand-Archive -Path $zip -DestinationPath $ext -Force
    $srcDir = (Get-ChildItem $ext -Recurse -Filter ffmpeg.exe | Select-Object -First 1).DirectoryName
    if (-not $srcDir) { throw "W pobranym archiwum nie ma ffmpeg.exe - cos poszlo nie tak." }
    Copy-Item (Join-Path $srcDir 'ffmpeg.exe')  $Ffmpeg  -Force
    Copy-Item (Join-Path $srcDir 'ffprobe.exe') $Ffprobe -Force
    Remove-Item $zip -Force
    Remove-Item $ext -Recurse -Force
    Write-Host "Wrzucono ffmpeg.exe + ffprobe.exe do tools\bin." -ForegroundColor Green
  }

  # --- 3. Weryfikacja ---
  Step "Weryfikacja"
  if (-not ((Test-Path $Ffmpeg) -and (Test-Path $Ffprobe))) {
    throw "Brakuje ffmpeg.exe lub ffprobe.exe w tools\bin - setup nieukonczony."
  }
  $ver = (& $Ffmpeg -version | Select-Object -First 1)
  Write-Host "ffmpeg dziala: $ver" -ForegroundColor Green

  Step "GOTOWE"
  Write-Host "Uruchom aplikacje komenda:" -ForegroundColor Green
  Write-Host "    dotnet run --project KeepClip" -ForegroundColor Green
}
catch {
  Write-Host ""
  Write-Host "BLAD: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
