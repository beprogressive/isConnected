; Built by installer/build-installer.ps1, which passes AppVersion, PublishDir and OutputDir.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<folder with the published app>
#endif
#ifndef OutputDir
  #error Pass /DOutputDir=<folder for the installer>
#endif

#define AppName "IsConnected"
#define AppExeName "IsConnected.exe"
#define AppUrl "https://github.com/beprogressive/isConnected"

[Setup]
; Identifies the app across versions. Never change it, or upgrades stop replacing the old install.
AppId={{7E3B5B0E-9C7A-4B52-8E0F-3D5A1C6F2A94}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=beprogressive
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Per-user install: no administrator rights, {autopf} is %LOCALAPPDATA%\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
ShowLanguageDialog=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; PrepareToInstall closes the app first; this covers the case where that did not work.
CloseApplications=yes
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "CloseRunningApp"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = '{#AppName}';

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExePath: String;
  ResultCode: Integer;
begin
  Result := '';
  ExePath := ExpandConstant('{app}\{#AppExeName}');
  if FileExists(ExePath) then
    Exec(ExePath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// The app's Autostart option writes this value. Remove it only when it points at this install,
// so that uninstalling leaves the autostart of a separately built copy alone.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current: String;
begin
  if (CurUninstallStep = usUninstall)
    and RegQueryStringValue(HKCU, RunKey, RunValue, Current)
    and (CompareText(Current, '"' + ExpandConstant('{app}\{#AppExeName}') + '"') = 0) then
    RegDeleteValue(HKCU, RunKey, RunValue);
end;
