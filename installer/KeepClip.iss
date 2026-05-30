; ============================================================================
;  KeepClip - skrypt instalatora (Inno Setup)
;  Buduje KeepClip-Setup.exe. Instalator kopiuje aplikacje do %LOCALAPPDATA%,
;  a nastepnie uruchamia bootstrap.ps1, ktory dociaga Pythona, biblioteki i
;  ffmpeg. Tworzy skroty (pulpit + menu Start) i deinstalator.
;  Kompilacja:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" KeepClip.iss
; ============================================================================

#define MyAppName "KeepClip"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Kamil"
#define MyAppURL "https://github.com/Kamilr210/KeepClip"
#define Src ".."

[Setup]
AppId={{A1F2C3D4-5E6F-4A8B-9C0D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=KeepClip-Setup
SetupIconFile=keepclip.ico
UninstallDisplayIcon={app}\keepclip.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Kod aplikacji (bez venv, __pycache__, .pyc)
Source: "{#Src}\backend\*";  DestDir: "{app}\backend";  Excludes: "*.pyc,__pycache__,.venv"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#Src}\frontend\*"; DestDir: "{app}\frontend"; Flags: recursesubdirs createallsubdirs ignoreversion
; Skrypty startowe i launcher
Source: "{#Src}\start.bat";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\start_server.bat";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\start_hidden.vbs";    DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\klipy.cmd";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\keepclip.vbs";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\install_shortcut.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\README.md";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#Src}\LICENSE";             DestDir: "{app}"; Flags: ignoreversion
; Pliki instalatora kopiowane do aplikacji
Source: "bootstrap.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "keepclip.ico";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";        Filename: "{app}\keepclip.vbs"; WorkingDir: "{app}"; IconFilename: "{app}\keepclip.ico"
Name: "{group}\Odinstaluj {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\keepclip.vbs"; WorkingDir: "{app}"; IconFilename: "{app}\keepclip.ico"; Tasks: desktopicon

[Run]
; Konfiguracja po skopiowaniu plikow (Python + biblioteki + ffmpeg).
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\bootstrap.ps1"""; \
  WorkingDir: "{app}"; \
  StatusMsg: "Instaluje Pythona, biblioteki i ffmpeg - to moze potrwac kilka minut..."; \
  Flags: waituntilterminated
; Opcjonalne uruchomienie po zakonczeniu instalacji.
Filename: "{app}\keepclip.vbs"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: postinstall nowait skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{app}\backend\.venv"
Type: filesandordirs; Name: "{app}\backend\__pycache__"
Type: filesandordirs; Name: "{app}\tools"
Type: filesandordirs; Name: "{app}\data"
