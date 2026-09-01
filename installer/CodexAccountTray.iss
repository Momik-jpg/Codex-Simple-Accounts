#define AppName "Codex-Konten"
#define AppVersion "1.5.5"
#define AppExeName "Codex-Konten.exe"

[Setup]
AppId={{52A5EEC2-7834-46B8-9E4B-8AA7648B4D72}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Andrin
DefaultDirName={localappdata}\Programs\CodexAccountTray
DefaultGroupName=Codex-Konten
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\artifacts\installer
OutputBaseFilename=Codex-Konten-Installer
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Uninstallable=yes
CreateUninstallRegKey=yes
SetupIconFile=..\assets\Codex-Konten.ico
VersionInfoVersion=1.5.5.0
VersionInfoCompany=Andrin
VersionInfoDescription=Codex-Konten Installer
VersionInfoProductName=Codex-Konten
VersionInfoProductVersion=1.5.5
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{userstartup}\Codex-Konten.lnk"
Type: files; Name: "{userstartup}\Start-Codex-Konten.cmd"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexAccountTray"; ValueData: """{app}\{#AppExeName}"" --background"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\Codex-Konten"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\Codex-Konten"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"; Flags: unchecked

[Run]
Filename: "{sys}\explorer.exe"; Parameters: """{app}\{#AppExeName}"""; Description: "Codex-Konten unabhängig starten"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Value: String;
begin
  if (CurStep = ssInstall) and
     RegQueryStringValue(HKCU, 'Environment', 'CODEX_APP_SERVER_WS_URL', Value) and
     (CompareText(Value, 'ws://127.0.0.1:47831') = 0) then
    RegDeleteValue(HKCU, 'Environment', 'CODEX_APP_SERVER_WS_URL');
end;
