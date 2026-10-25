; Inno Setup script: builds dist\ScreengrabberSetup-<version>.exe from the self-contained publish in dist\app.
; Run build-installer.ps1 rather than compiling this directly.

#define AppName "Screengrabber"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Keep this AppId forever: it's how upgrades and the uninstaller find the existing install.
AppId={{9C5D7F0E-6E2B-4C8A-9E1F-3B7A2D4C5E61}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
; Per-user install: no admin prompt, and the app's own startup entry is per-user too.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=..\dist
OutputBaseFilename=ScreengrabberSetup-{#AppVersion}
SetupIconFile=..\src\Screengrabber\Screengrabber.ico
UninstallDisplayIcon={app}\Screengrabber.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 or later.
MinVersion=10.0.17763
WizardStyle=modern
; We close the app ourselves (see [Code]); don't also prompt about files in use.
CloseApplications=no

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\Screengrabber.exe"; Comment: "Screenshot to anywhere. Opens Settings if already running."

[Registry]
; The app adds its own "start with Windows" entry; remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppName}"; ValueType: none; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
; Also remove the folder when an earlier copy (not this installer) created it.
Type: dirifempty; Name: "{app}"

[Run]
Filename: "{app}\Screengrabber.exe"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent
; In-app updates run the installer silently: start the new version again afterwards.
Filename: "{app}\Screengrabber.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[Code]
const
  AppMutex = 'Local\Screengrabber.SingleInstance';

// Asks a running copy to quit (Screengrabber.exe --quit), then waits for it; force-closes as a last resort
// (e.g. an older version that doesn't know --quit). The app holds no unsaved work.
procedure CloseRunningApp(Exe: String);
var
  ResultCode, Waited: Integer;
begin
  if not CheckForMutexes(AppMutex) then
    exit;
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Waited := 0;
  while CheckForMutexes(AppMutex) and (Waited < 50) do
  begin
    Sleep(100);
    Waited := Waited + 1;
  end;
  if CheckForMutexes(AppMutex) then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Screengrabber.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp(ExpandConstant('{app}\Screengrabber.exe'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningApp(ExpandConstant('{app}\Screengrabber.exe'));
  Result := True;
end;
