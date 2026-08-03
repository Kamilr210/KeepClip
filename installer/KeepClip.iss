; Instaluje gotową publikację samowystarczalną bez uprawnień administratora.
; Model Whisper pobiera się dopiero przy pierwszej transkrypcji.

#define MyAppName "KeepClip"
#define MyAppVersion "2.2.5"
#define MyAppPublisher "Kamil"
#define MyAppURL "https://github.com/Kamilr210/KeepClip"
#define MyAppExe "KeepClip.exe"
; build.ps1 nadpisuje ten katalog ścieżką do publikacji przejściowej.
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{A1F2C3D4-5E6F-4A8B-9C0D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
; Instalacja dla bieżącego użytkownika nie wymaga uprawnień administratora.
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
; Aktualizacja w miejscu: działający KeepClip (także schowany w zasobniku) jest
; zamykany przez Menedżera ponownego uruchamiania, a nie blokuje kopiowania plików.
CloseApplications=yes
CloseApplicationsFilter=KeepClip.exe
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Bootstrapper uruchamia się tylko wtedy, gdy WebView2 nie ma na komputerze.
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; Wbudowany klient OAuth jest wymagany, aby użytkownik dostał zwykłe logowanie Google.
; Plik nie trafia do repozytorium; CI odtwarza go z sekretu.
Source: "embed\google_client.json"; DestDir: "{app}\data"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Instaluje skladnik WebView2 (Edge) - chwila..."; \
  Check: NeedWebView2; Flags: waituntilterminated

Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: postinstall nowait skipifsilent

[UninstallRun]
; Deinstalacja usuwa token odświeżania z Menedżera poświadczeń Windows.
Filename: "{cmd}"; Parameters: "/c cmdkey /delete:KeepClip:GoogleDriveRefreshToken"; \
  Flags: runhidden runascurrentuser; RunOnceId: "DelDriveToken"

[UninstallDelete]
; Usuwa dane aplikacji i model, ale nigdy nie dotyka klipów poza katalogiem aplikacji.
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\models"
Type: dirifempty; Name: "{app}"

[Code]
// Sprawdza systemową i przypisaną do użytkownika instalację WebView2 w kluczach EdgeUpdate.
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

function NeedWebView2(): Boolean;
begin
  Result := (not WebView2Installed())
         and FileExists(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'));
end;
