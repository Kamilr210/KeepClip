; ============================================================================
;  KeepClip - skrypt instalatora (Inno Setup) dla buildu C# (.NET self-contained)
;  --------------------------------------------------------------------------
;  W ODROZNIENIU od starego buildu Pythona: BRAK bootstrap.ps1, BRAK pobierania
;  Pythona/bibliotek/ffmpeg w trakcie instalacji. Cala aplikacja (runtime .NET +
;  frontend + ffmpeg) jest juz w folderze publish\, ktory powstaje komenda:
;      installer\build.ps1            (albo recznie: dotnet publish ... -o publish)
;  Instalator tylko kopiuje gotowe pliki do %LOCALAPPDATA%\KeepClip (bez admina),
;  tworzy skroty i deinstalator, oraz - jesli trzeba - doinstalowuje srodowisko
;  uruchomieniowe WebView2 (Edge). Model Whisper (~1.6 GB) pobiera sie sam przy
;  pierwszej transkrypcji, wiec NIE jest w instalatorze.
;
;  Kompilacja recznie:
;      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\KeepClip.iss
;  (albo po prostu uruchom installer\build.ps1 - zrobi publish I instalator).
; ============================================================================

#define MyAppName "KeepClip"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "Kamil"
#define MyAppURL "https://github.com/Kamilr210/KeepClip"
#define MyAppExe "KeepClip.exe"
; Folder z wynikiem `dotnet publish` (sciezki ponizej sa wzgledem tego pliku .iss).
#define PublishDir "..\publish"

[Setup]
AppId={{A1F2C3D4-5E6F-4A8B-9C0D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
; Instalacja per-uzytkownik do %LOCALAPPDATA% => NIE wymaga uprawnien administratora,
; co jest kluczowe dla "latwej instalacji dla innych uzytkownikow".
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=KeepClip-Setup
SetupIconFile=..\frontend\icons\icon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Build jest win-x64 (self-contained), wiec wymagamy 64-bitowego Windowsa.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Cala opublikowana aplikacja: self-contained runtime .NET + KeepClip.exe + frontend\
; + tools\bin\ffmpeg/ffprobe + natywne runtimy Whisper (Vulkan/CPU) i WebView2Loader.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Bootstrapper srodowiska WebView2 (Edge) - maly (~2 MB). Wypakowywany do {tmp} i
; uruchamiany TYLKO gdy WebView2 nie ma na maszynie (patrz [Run] + [Code]). Plik
; dostarcza build.ps1; gdy go brak, instalator i tak sie zbuduje (skipifsource...).
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; OPCJONALNIE: wbudowany klient OAuth Google (model "Aternos") - jesli autor wrzuci
; swoj google_client.json do installer\embed\, uzytkownicy koncowi od razu klikaja
; "Polacz" bez zakladania wlasnego projektu OAuth. Gdy pliku brak, instalator go
; pomija, a aplikacja pokazuje panel jednorazowej konfiguracji chmury. Plik NIGDY
; nie trafia do repozytorium (installer\embed\ jest w .gitignore).
Source: "embed\google_client.json"; DestDir: "{app}\data"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Doinstaluj WebView2 tylko gdy faktycznie go brakuje (i mamy bootstrapper w {tmp}).
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Instaluje skladnik WebView2 (Edge) - chwila..."; \
  Check: NeedWebView2; Flags: waituntilterminated

; Opcjonalne uruchomienie aplikacji po instalacji.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: postinstall nowait skipifsilent

[UninstallRun]
; Czysta deinstalacja: usun refresh token Google Drive z Menedzera poswiadczen
; Windows (zapisany pod targetem "KeepClip:GoogleDriveRefreshToken"). Best-effort -
; jesli go nie ma, cmdkey po prostu nic nie zrobi (runhidden, nie blokuje usuwania).
Filename: "{cmd}"; Parameters: "/c cmdkey /delete:KeepClip:GoogleDriveRefreshToken"; \
  Flags: runhidden runascurrentuser; RunOnceId: "DelDriveToken"

[UninstallDelete]
; "Latwa destrukcja": usun dane aplikacji i pobrany model (~1.6 GB), zeby nie zostawic
; smieci. UWAGA: prawdziwe klipy uzytkownika leza w jego folderze Wideo (POZA {app})
; i NIE sa ruszane przez deinstalator.
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\models"
Type: dirifempty; Name: "{app}"

[Code]
// Wykrycie srodowiska uruchomieniowego WebView2 (Evergreen). Microsoft publikuje
// numer wersji ("pv") pod kluczem EdgeUpdate\Clients (GUID WebView2). Sprawdzamy
// instalacje systemowa (HKLM, takze widok 32-bit przez WOW6432Node) oraz per-user
// (HKCU). Pusty lub "0.0.0.0" oznacza brak.
const
  WV2_KEY = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WV2_KEY_WOW = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasPv(Root: Integer; const Key: String): Boolean;
var
  v: String;
begin
  Result := False;
  if RegQueryStringValue(Root, Key, 'pv', v) then
    if (v <> '') and (v <> '0.0.0.0') then
      Result := True;
end;

function WebView2Installed(): Boolean;
begin
  Result := HasPv(HKLM, WV2_KEY_WOW)
         or HasPv(HKLM, WV2_KEY)
         or HasPv(HKCU, WV2_KEY);
end;

// Uruchom bootstrapper tylko gdy runtime brakuje I plik faktycznie zostal wypakowany.
function NeedWebView2(): Boolean;
begin
  Result := (not WebView2Installed())
         and FileExists(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'));
end;
