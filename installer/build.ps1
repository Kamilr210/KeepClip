<#
  Buduje samowystarczalną publikację win-x64 i instalator KeepClip przez Inno Setup.

  Użycie:
      powershell -ExecutionPolicy Bypass -File installer\build.ps1
      ...\build.ps1 -SkipPublish
      ...\build.ps1 -NoInstaller
#>
[CmdletBinding()]
param(
  [switch]$SkipPublish,
  [switch]$NoInstaller,
  # Wydanie publiczne usuwa panel logów; GitHub Actions włącza ten tryb automatycznie.
  [switch]$PublicRelease,
  [switch]$VerifyPackaging,
  [string]$OutputRoot,
  [string]$GoogleClientPath
)

$ErrorActionPreference = 'Stop'

$Public = $PublicRelease.IsPresent -or ($env:GITHUB_ACTIONS -eq 'true')
if ($VerifyPackaging -and $Public) { throw '-VerifyPackaging jest tylko lokalnym testem; nie lacz go z -PublicRelease ani GitHub Actions.' }

function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

# Usuwa bloki DEV z publikowanego interfejsu i zapisuje UTF-8 bez BOM.
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
$PublishDir   = Join-Path $InstallerDir 'staging\publish'
$RedistDir    = Join-Path $InstallerDir 'redist'
$IssFile      = Join-Path $InstallerDir 'KeepClip.iss'
$DistDir      = Join-Path $InstallerDir 'dist'
$GoogleClient = Join-Path $InstallerDir 'embed\google_client.json'
if ($GoogleClientPath) { $GoogleClient = [IO.Path]::GetFullPath($GoogleClientPath) }
if ($OutputRoot) {
  $OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
  if ($OutputRoot -eq $RepoRoot -or $OutputRoot -eq $InstallerDir) { throw 'OutputRoot musi wskazywac osobny katalog wynikowy.' }
  $PublishDir = Join-Path $OutputRoot 'publish'
  $DistDir = Join-Path $OutputRoot 'dist'
}
if ($VerifyPackaging) { $DistDir = Join-Path $DistDir 'verification' }

try {
  # Kazdy instalator przeznaczony dla uzytkownikow musi miec wspolnego klienta
  # OAuth autora. Bez niego aplikacja zamiast zwyklego logowania Google pokazalaby
  # uzytkownikowi instrukcje tworzenia projektu w Google Cloud.
  if (-not $NoInstaller -or $GoogleClientPath) {
    Step "Sprawdzam klienta OAuth Google dla instalatora"
    if (-not (Test-Path -LiteralPath $GoogleClient)) {
      throw "Brak $GoogleClient. Skopiuj pobrany klient OAuth typu Desktop app do installer\embed\google_client.json."
    }
    try {
      $oauthJson = Get-Content -LiteralPath $GoogleClient -Raw | ConvertFrom-Json
    } catch {
      throw 'Niepoprawny JSON konfiguracji klienta OAuth.'
    }
    if ($oauthJson.installed -isnot [PSCustomObject] -or $oauthJson.PSObject.Properties.Name -contains 'web' `
        -or $oauthJson.installed.client_id -isnot [string] -or [string]::IsNullOrWhiteSpace($oauthJson.installed.client_id)) {
      throw 'Wymagana konfiguracja Google OAuth typu Desktop app z installed.client_id.'
    }
    Write-Host "Klient OAuth jest gotowy; instalator pokaze zwykle logowanie Google."
  }

  # Brak małego instalatora WebView2 nie blokuje budowy głównego instalatora.
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

  if ($SkipPublish) {
    Step "Pomijam publish (-SkipPublish)"
    if (-not (Test-Path (Join-Path $PublishDir 'KeepClip.exe'))) {
      throw "Brak $PublishDir\KeepClip.exe - uruchom bez -SkipPublish."
    }
  } else {
    Step "Publikuje aplikacje (dotnet publish, self-contained win-x64)"
    $allowedRoot = if ($OutputRoot) { $OutputRoot } else { Join-Path $InstallerDir 'staging' }
    $resolvedPublish = [IO.Path]::GetFullPath($PublishDir)
    if (-not $resolvedPublish.StartsWith([IO.Path]::GetFullPath($allowedRoot).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {
      throw 'Katalog usuwanej publikacji wykracza poza katalog wynikowy.'
    }
    if (Test-Path -LiteralPath $resolvedPublish) { Remove-Item -LiteralPath $resolvedPublish -Recurse -Force }
    $pubArgs = @(
      'publish', $Project,
      '-c', 'Release',
      '-r', 'win-x64',
      '--self-contained', 'true',
      '-p:PublishSingleFile=false',
      '-p:PublishTrimmed=false',
      '-o', $PublishDir
    )
    if ($Public) { $pubArgs += '-p:PublicRelease=true' }
    if ($OutputRoot) { $pubArgs += ('-p:BaseOutputPath='+(Join-Path $OutputRoot 'build\')) }
    Write-Host ("Tryb: {0}" -f $(if ($Public) { 'PUBLICZNY (bez funkcji deweloperskich)' } else { 'deweloperski' }))
    & dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish zwrocil kod $LASTEXITCODE." }
  }

  if (-not $NoInstaller -or $GoogleClientPath) {
    Step 'Przygotowuje publiczna konfiguracje OAuth bez sekretu'
    $publicClient = Join-Path $PublishDir 'data\google_client.json'
    & (Join-Path $InstallerDir 'prepare-public-oauth.ps1') -InputPath $GoogleClient -OutputPath $publicClient
    & (Join-Path $InstallerDir 'prepare-public-oauth.ps1') -InputPath $publicClient -VerifyOnly
  }

  # Samo wyłączenie backendu nie wystarcza: usuwa również kod panelu z plików instalatora.
  if ($Public) {
    Step "Public release: usuwam funkcje deweloperskie z frontendu"
    Remove-DevBlocks (Join-Path $PublishDir 'frontend\index.html')
    Remove-DevBlocks (Join-Path $PublishDir 'frontend\app.js')
  }

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

  Step "Sprawdzam licencje i notices publikacji"
  & (Join-Path $InstallerDir 'verify-licenses.ps1') -PublishDir $PublishDir -RequireFfmpeg

  if ($NoInstaller) {
    Step "Gotowe (-NoInstaller): publish w $PublishDir"
    return
  }

  if (-not $VerifyPackaging) {
    & (Join-Path $InstallerDir 'verify-licenses.ps1') -PublishDir $PublishDir -RequireFfmpeg -ForDistribution
  } else {
    Write-Warning 'Test pakowania: wynik nie jest przeznaczony do dystrybucji.'
  }

  Step "Szukam kompilatora Inno Setup (ISCC.exe)"
  function Find-ISCC {
    # Inno Setup często instaluje się dla bieżącego użytkownika, dlatego sprawdzamy
    # katalogi użytkownika i systemu, rejestr oraz ścieżkę wyszukiwania programów.
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
    throw "Brak Inno Setup. Zainstaluj z https://jrsoftware.org/isdl.php (Inno Setup 6) i uruchom ponownie, albo `winget install JRSoftware.InnoSetup`."
  }
  Write-Host "ISCC: $iscc"

  $engineLicense = Join-Path (Split-Path $iscc) 'license.txt'
  if (-not (Test-Path -LiteralPath $engineLicense)) { throw 'Brak oryginalnej licencji kompilatora Inno Setup.' }
  $engineNoticeDir = Join-Path $PublishDir 'licenses\installer-engine'
  New-Item -ItemType Directory -Force -Path $engineNoticeDir | Out-Null
  Copy-Item -LiteralPath $engineLicense -Destination (Join-Path $engineNoticeDir 'LICENSE.txt') -Force
  @{compiler='Inno Setup';compilerSha256=(Get-FileHash -LiteralPath $iscc -Algorithm SHA256).Hash.ToLowerInvariant();licenseSha256=(Get-FileHash -LiteralPath $engineLicense -Algorithm SHA256).Hash.ToLowerInvariant();source='license.txt alongside the compiler used for this installer'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $engineNoticeDir 'provenance.json') -Encoding UTF8
  if (Test-Path -LiteralPath $wv2) {
    $wv2Info = (Get-Item -LiteralPath $wv2).VersionInfo
    @{component='Microsoft Edge WebView2 Evergreen bootstrapper';sha256=(Get-FileHash -LiteralPath $wv2 -Algorithm SHA256).Hash.ToLowerInvariant();fileVersion=$wv2Info.FileVersion;productVersion=$wv2Info.ProductVersion;source='https://go.microsoft.com/fwlink/p/?LinkId=2124703';runtimeTerms='https://developer.microsoft.com/en-us/microsoft-edge/webview2/';runtimeVersion='downloaded later; Evergreen, not the SDK version';license='Microsoft software license terms; distinct from SDK BSD-3-Clause'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $engineNoticeDir 'webview2-bootstrapper.json') -Encoding UTF8
  }

  New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
  $sourceArchive=Join-Path $DistDir 'KeepClip-FFmpeg-Source.zip'
  Compress-Archive -Path (Join-Path $PublishDir 'licenses\ffmpeg-build\*') -DestinationPath $sourceArchive -Force

  Step "Buduje instalator (ISCC)"
  New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
  $isccArgs = @("/DPublishDir=$PublishDir", "/O$DistDir")
  if ($VerifyPackaging) { $isccArgs += '/DVerificationOnly=1' }
  & $iscc @isccArgs $IssFile
  if ($LASTEXITCODE -ne 0) { throw "ISCC zwrocil kod $LASTEXITCODE." }

  $setupName = if ($VerifyPackaging) { 'KeepClip-Packaging-Verification.exe' } else { 'KeepClip-Setup.exe' }
  $setup = Join-Path $DistDir $setupName
  if (Test-Path $setup) {
    $mb = [math]::Round(((Get-Item $setup).Length / 1MB), 1)
    $ffmpegBuild=Get-Content -LiteralPath (Join-Path $PublishDir 'licenses\ffmpeg-build\provenance.json') -Raw | ConvertFrom-Json
    @{installerSha256=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant();ffmpegSourceArchiveSha256=(Get-FileHash -LiteralPath $sourceArchive -Algorithm SHA256).Hash.ToLowerInvariant();ffmpeg=$ffmpegBuild.binaries;buildId=$ffmpegBuild.buildId} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $DistDir 'KeepClip-Setup.provenance.json') -Encoding UTF8
    Step "GOTOWE"
    Write-Host "Instalator: $setup ($mb MB)" -ForegroundColor Green
    if ($VerifyPackaging) { Write-Host 'Wynik wylacznie do lokalnej kontroli zawartosci; nie publikuj.' -ForegroundColor Yellow }
  } else {
    throw "ISCC zakonczyl sie sukcesem, ale nie ma $setup."
  }
}
catch {
  Write-Host ""
  Write-Host "BLAD: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
