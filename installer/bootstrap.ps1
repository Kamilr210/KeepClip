<#
  KeepClip - pierwsza konfiguracja (bootstrap).
  Wywolywane przez instalator po skopiowaniu plikow.
  Instaluje Pythona (jesli brak), tworzy srodowisko (venv), instaluje
  biblioteki i pobiera ffmpeg. Mozna uruchomic ponownie - jest idempotentny.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

function Find-Python {
  # 1) launcher 'py' (preferowany na Windows) - pytamy o realna sciezke exe
  $launcher = Get-Command py -ErrorAction SilentlyContinue
  if ($launcher) {
    try {
      $exe = (& $launcher.Source -3 -c "import sys; print(sys.executable)" 2>$null)
      if ($LASTEXITCODE -eq 0 -and $exe) { $exe = $exe.Trim(); if (Test-Path $exe) { return $exe } }
    } catch {}
  }
  # 2) python.exe w PATH (ale NIE zaslepka ze Sklepu Windows)
  foreach ($c in (Get-Command python -ErrorAction SilentlyContinue -All)) {
    if ($c.Source -and $c.Source -notlike '*\WindowsApps\*') { return $c.Source }
  }
  # 3) typowe lokalizacje instalacji
  foreach ($p in @(
      "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
      "$env:LOCALAPPDATA\Programs\Python\Python311\python.exe",
      "$env:LOCALAPPDATA\Programs\Python\Python310\python.exe")) {
    if (Test-Path $p) { return $p }
  }
  return $null
}

function Refresh-Path {
  $m = [Environment]::GetEnvironmentVariable('Path','Machine')
  $u = [Environment]::GetEnvironmentVariable('Path','User')
  $env:Path = (@($m,$u) | Where-Object { $_ }) -join ';'
}

try {
  Write-Step "Sprawdzam Pythona"
  $python = Find-Python
  if (-not $python) {
    Write-Step "Nie znaleziono Pythona - instaluje Python 3.11"
    $ok = $false
    if (Get-Command winget -ErrorAction SilentlyContinue) {
      try {
        winget install -e --id Python.Python.3.11 --scope user --silent `
          --accept-package-agreements --accept-source-agreements
        $ok = ($LASTEXITCODE -eq 0)
      } catch {}
    }
    if (-not $ok) {
      Write-Step "Pobieram instalator Pythona z python.org"
      $url = 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe'
      $exe = Join-Path $env:TEMP 'keepclip-python-setup.exe'
      Invoke-WebRequest -Uri $url -OutFile $exe
      Start-Process -FilePath $exe -Wait -ArgumentList `
        '/quiet','InstallAllUsers=0','PrependPath=1','Include_launcher=1','Include_test=0'
    }
    Refresh-Path
    $python = Find-Python
    if (-not $python) {
      throw "Nie udalo sie zainstalowac Pythona automatycznie. Zainstaluj go z https://www.python.org/ (zaznacz 'Add to PATH') i uruchom instalator KeepClip ponownie."
    }
  }
  Write-Host "Python: $python"

  Write-Step "Tworze srodowisko (venv)"
  $venv   = Join-Path $root 'backend\.venv'
  $venvPy = Join-Path $venv 'Scripts\python.exe'
  if (-not (Test-Path $venvPy)) {
    & $python -m venv $venv
    if (-not (Test-Path $venvPy)) { throw "Nie udalo sie utworzyc venv." }
  }

  Write-Step "Instaluje biblioteki (~2 GB, to potrwa kilka minut)"
  & $venvPy -m pip install --upgrade pip
  & $venvPy -m pip install -r (Join-Path $root 'backend\requirements.txt')
  if ($LASTEXITCODE -ne 0) { throw "Instalacja bibliotek (pip) nie powiodla sie. Sprawdz polaczenie z internetem." }

  Write-Step "Sprawdzam ffmpeg"
  $bin = Join-Path $root 'tools\bin'
  New-Item -ItemType Directory -Force -Path $bin | Out-Null
  if (-not (Test-Path (Join-Path $bin 'ffmpeg.exe'))) {
    Write-Step "Pobieram ffmpeg"
    $zip = Join-Path $env:TEMP 'keepclip-ffmpeg.zip'
    $ext = Join-Path $env:TEMP 'keepclip-ffmpeg'
    Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
    if (Test-Path $ext) { Remove-Item -Recurse -Force $ext }
    Expand-Archive -Path $zip -DestinationPath $ext -Force
    $ff = Get-ChildItem -Path $ext -Recurse -Filter ffmpeg.exe  | Select-Object -First 1
    $fp = Get-ChildItem -Path $ext -Recurse -Filter ffprobe.exe | Select-Object -First 1
    if (-not $ff -or -not $fp) { throw "Nie znaleziono ffmpeg w pobranym archiwum." }
    Copy-Item $ff.FullName (Join-Path $bin 'ffmpeg.exe')  -Force
    Copy-Item $fp.FullName (Join-Path $bin 'ffprobe.exe') -Force
    Remove-Item -Recurse -Force $ext -ErrorAction SilentlyContinue
    Remove-Item -Force $zip -ErrorAction SilentlyContinue
  }

  Write-Step "Gotowe! KeepClip jest zainstalowany."
  Write-Host "Uruchom go skrotem 'KeepClip' (pulpit lub menu Start)."
  Start-Sleep -Seconds 2
}
catch {
  Write-Host ""
  Write-Host "BLAD: $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "Nacisnij Enter, aby zamknac..."
  [void](Read-Host)
  exit 1
}
