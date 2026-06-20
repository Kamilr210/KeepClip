<#
  KeepClip - jednokomendowy build instalatora (C# / .NET self-contained).

  Co robi:
    1. Pobiera bootstrapper WebView2 (maly, ~2 MB) do installer\redist\ (jesli brak).
    2. `dotnet publish` -> self-contained win-x64 do .\publish (runtime .NET w srodku,
       wiec uzytkownicy koncowi NIE musza nic instalowac).
    3. Przycina nieuzywane natywne runtimy (inne OS/architektury) zeby zmniejszyc rozmiar.
    4. Kompiluje installer\KeepClip.iss przez Inno Setup (ISCC). Gdy brak Inno Setup,
       probuje doinstalowac go przez winget.
    5. Wynik: installer\dist\KeepClip-Setup.exe (do wrzucenia na GitHub Releases).

  Uzycie:
      powershell -ExecutionPolicy Bypass -File installer\build.ps1
      ...\build.ps1 -SkipPublish      # gdy publish juz jest aktualny (szybka iteracja)
      ...\build.ps1 -NoInstaller      # sam publish, bez budowania .exe instalatora
#>
[CmdletBinding()]
param(
  [switch]$SkipPublish,
  [switch]$NoInstaller,
  # Public release: strips developer-only features (the "Logi aplikacji" panel) from
  # the build. Auto-on under GitHub Actions, so the automated release workflow never
  # ships dev tools even if the switch is forgotten.
  [switch]$PublicRelease
)

$ErrorActionPreference = 'Stop'

$Public = $PublicRelease.IsPresent -or ($env:GITHUB_ACTIONS -eq 'true')

function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

# Remove <!-- DEV:START -->...<!-- DEV:END --> (HTML) and /* DEV:START */.../* DEV:END */
# (JS) blocks from a published frontend file. Written UTF-8 without BOM (the app reads
# index.html as UTF-8; a BOM there is asking for trouble).
function Remove-DevBlocks([string]$file) {
  if (-not (Test-Path $file)) { return }
  $txt = [IO.File]::ReadAllText($file)
  $txt = [regex]::Replace($txt, '(?s)<!-- DEV:START.*?DEV:END -->\s*', '')
  $txt = [regex]::Replace($txt, '(?s)/\* DEV:START.*?DEV:END \*/\s*', '')
  [IO.File]::WriteAllText($file, $txt, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "  oczyszczono $file"
}

$InstallerDir = $PSScriptRoot
$RepoRoot     = Split-Path -Parent $InstallerDir
$Project      = Join-Path $RepoRoot 'KeepClip\KeepClip.csproj'
$PublishDir   = Join-Path $RepoRoot 'publish'
$RedistDir    = Join-Path $InstallerDir 'redist'
$IssFile      = Join-Path $InstallerDir 'KeepClip.iss'
$DistDir      = Join-Path $InstallerDir 'dist'

try {
  # --- 1. Bootstrapper WebView2 (best-effort; bez niego instalator i tak sie zbuduje) ---
  Step "Sprawdzam bootstrapper WebView2"
  New-Item -ItemType Directory -Force -Path $RedistDir | Out-Null
  $wv2 = Join-Path $RedistDir 'MicrosoftEdgeWebview2Setup.exe'
  if (Test-Path $wv2) {
    Write-Host "Jest: $wv2"
  } else {
    Write-Host "Pobieram bootstrapper WebView2..."
    try {
      Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $wv2
      Write-Host "Pobrano: $wv2"
    } catch {
      Write-Warning "Nie udalo sie pobrac bootstrappera WebView2 ($($_.Exception.Message))."
      Write-Warning "Instalator powstanie bez niego - na maszynach bez WebView2 trzeba bedzie go doinstalowac recznie."
    }
  }

  # --- 2. dotnet publish (self-contained win-x64) ---
  if ($SkipPublish) {
    Step "Pomijam publish (-SkipPublish)"
    if (-not (Test-Path (Join-Path $PublishDir 'KeepClip.exe'))) {
      throw "Brak $PublishDir\KeepClip.exe - uruchom bez -SkipPublish."
    }
  } else {
    Step "Publikuje aplikacje (dotnet publish, self-contained win-x64)"
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    $pubArgs = @(
      'publish', $Project,
      '-c', 'Release',
      '-r', 'win-x64',
      '--self-contained', 'true',
      '-p:PublishSingleFile=false',
      '-p:PublishTrimmed=false',
      '-o', $PublishDir
    )
    if ($Public) { $pubArgs += '-p:PublicRelease=true' }  # drops KEEPCLIP_DEV (no log endpoint/capture)
    Write-Host ("Tryb: {0}" -f $(if ($Public) { 'PUBLICZNY (bez funkcji deweloperskich)' } else { 'deweloperski' }))
    & dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish zwrocil kod $LASTEXITCODE." }
  }

  # Public release: also physically strip the dev-only UI from the staged frontend, so
  # the installer doesn't even contain the "Logi aplikacji" markup/script.
  if ($Public) {
    Step "Public release: usuwam funkcje deweloperskie z frontendu"
    Remove-DevBlocks (Join-Path $PublishDir 'frontend\index.html')
    Remove-DevBlocks (Join-Path $PublishDir 'frontend\app.js')
  }

  # --- 3. Przytnij nieuzywane natywne runtimy (proces win-x64 ich nie laduje) ---
  Step "Przycinam nieuzywane runtimy (inne OS/architektury)"
  $prune = @(
    (Join-Path $PublishDir 'runtimes\win-x86'),
    (Join-Path $PublishDir 'runtimes\win-arm64'),
    (Join-Path $PublishDir 'runtimes\vulkan\linux-x64'),
    (Join-Path $PublishDir 'runtimes\linux-x64'),
    (Join-Path $PublishDir 'runtimes\linux-arm64'),
    (Join-Path $PublishDir 'runtimes\osx'),
    (Join-Path $PublishDir 'runtimes\osx-x64'),
    (Join-Path $PublishDir 'runtimes\osx-arm64')
  )
  foreach ($p in $prune) {
    if (Test-Path $p) { try { Remove-Item -Recurse -Force $p; Write-Host "  usunieto $p" } catch {} }
  }
  $pubSize = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
  Write-Host "Rozmiar publish: $pubSize MB"

  if ($NoInstaller) {
    Step "Gotowe (-NoInstaller): publish w $PublishDir"
    return
  }

  # --- 4. Znajdz / doinstaluj Inno Setup (ISCC) ---
  Step "Szukam kompilatora Inno Setup (ISCC.exe)"
  function Find-ISCC {
    # Inno Setup instaluje sie czesto PER-USER (do %LOCALAPPDATA%\Programs), nie tylko
    # do Program Files - sprawdzamy oba, plus sciezke z rejestru (InstallLocation) i PATH.
    $cands = @(
      (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
      (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
      (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $cands) { if ($c -and (Test-Path $c)) { return $c } }
    $regKeys = @(
      'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
      'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($k in $regKeys) {
      try {
        $loc = (Get-ItemProperty -Path $k -ErrorAction Stop).InstallLocation
        if ($loc) {
          $exe = Join-Path $loc 'ISCC.exe'
          if (Test-Path $exe) { return $exe }
        }
      } catch {}
    }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
  }
  $iscc = Find-ISCC
  if (-not $iscc) {
    Write-Host "Nie znaleziono Inno Setup - probuje przez winget..."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
      try {
        winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
      } catch {}
      $iscc = Find-ISCC
    }
  }
  if (-not $iscc) {
    throw "Brak Inno Setup. Zainstaluj z https://jrsoftware.org/isdl.php (Inno Setup 6) i uruchom ponownie, albo `winget install JRSoftware.InnoSetup`."
  }
  Write-Host "ISCC: $iscc"

  # --- 5. Kompiluj instalator ---
  Step "Buduje instalator (ISCC)"
  New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
  & $iscc $IssFile
  if ($LASTEXITCODE -ne 0) { throw "ISCC zwrocil kod $LASTEXITCODE." }

  $setup = Join-Path $DistDir 'KeepClip-Setup.exe'
  if (Test-Path $setup) {
    $mb = [math]::Round(((Get-Item $setup).Length / 1MB), 1)
    Step "GOTOWE"
    Write-Host "Instalator: $setup ($mb MB)" -ForegroundColor Green
    Write-Host "Wrzuc go na GitHub Releases - uzytkownicy koncowi pobieraja i klikaja Dalej." -ForegroundColor Green
  } else {
    throw "ISCC zakonczyl sie sukcesem, ale nie ma $setup."
  }
}
catch {
  Write-Host ""
  Write-Host "BLAD: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
