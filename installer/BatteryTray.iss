#ifndef MyVersion
  #define MyVersion "0.1.0"
#endif

[Setup]
AppId={{B7C0A1E2-5D3F-4B7A-9E41-6C2D8F0A1B35}
AppName=Battery Tray
AppVersion={#MyVersion}
AppPublisher=Battery Tray
DefaultDirName={autopf}\Battery Tray
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=BatteryTray-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Battery Tray

[Files]
Source: "..\publish\BatteryTray.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Battery Tray"; Filename: "{app}\BatteryTray.exe"

[Registry]
; Start with Windows (the app's Settings can turn this off later). Removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BatteryTray"; ValueData: """{app}\BatteryTray.exe"" --tray"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\BatteryTray.exe"; Description: "Start Battery Tray now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM BatteryTray.exe"; Flags: runhidden; RunOnceId: "StopBatteryTray"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\BatteryTray"

[Code]
function InitializeSetup(): Boolean;
var rc: Integer;
begin
  { close a running copy so upgrades can replace the file }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM BatteryTray.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Result := True;
end;
