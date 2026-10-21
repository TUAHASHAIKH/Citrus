#ifndef Payload
  #error Payload must point to a prepared release directory
#endif
#ifndef ReleaseOutput
  #error ReleaseOutput must be specified
#endif
#define Version "0.3.2"

[Setup]
AppId={{8BA05164-740A-4B45-A8B2-93D589747B51}
AppName=Citrus
AppVersion={#Version}
AppPublisher=Citrus Contributors
AppPublisherURL=https://github.com/TUAHASHAIKH/Citrus
AppSupportURL=https://github.com/TUAHASHAIKH/Citrus/issues
DefaultDirName={localappdata}\Programs\Citrus
DefaultGroupName=Citrus
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#ReleaseOutput}
OutputBaseFilename=Citrus-Setup-{#Version}-x64
SetupIconFile=..\assets\citrus.ico
UninstallDisplayIcon={app}\Citrus.exe
LicenseFile={#Payload}\INSTALLER_LICENSE.txt
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#Version}.0

[Tasks]
Name: startup; Description: "Start Citrus when I sign in to Windows"; Flags: checkedonce

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\prerequisites\vc_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{group}\Citrus"; Filename: "{app}\Citrus.exe"
Name: "{group}\Uninstall Citrus"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CitrusJpegConverter"; ValueData: """{app}\Citrus.exe"" --background"; Tasks: startup

[Run]
Filename: "{app}\Citrus.exe"; Parameters: "--background"; Description: "Start Citrus in the system tray"; Flags: nowait postinstall skipifsilent; Check: ShouldLaunch

[Code]
function ShouldLaunch: Boolean;
begin
  Result := ExpandConstant('{param:NOLAUNCH|0}') <> '1';
end;

function StopCitrus: Boolean;
var ResultCode: Integer; Exe: String;
begin
  Exe := ExpandConstant('{app}\Citrus.exe');
  Result := True;
  if FileExists(Exe) then
    Result := Exec(Exe, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Major, Minor: Cardinal; Code: Integer;
begin
  Result := '';
  if not StopCitrus then Result := 'Citrus could not close. Exit Citrus from its tray menu and retry.';
  if Result <> '' then Exit;
  Major := 0; Minor := 0;
  RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Major', Major);
  RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Minor', Minor);
  if (Major < 14) or ((Major = 14) and (Minor < {#RuntimeMinor})) then begin
    ExtractTemporaryFile('vc_redist.x64.exe');
    if not ShellExec('runas', ExpandConstant('{tmp}\vc_redist.x64.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := 'The Microsoft Visual C++ runtime is required. Please allow its installation and retry.'
    else if (Code <> 0) and (Code <> 1638) and (Code <> 3010) then
      Result := 'Microsoft Visual C++ runtime installation failed. Error: ' + IntToStr(Code)
    else NeedsRestart := Code = 3010;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := StopCitrus;
  if not Result then MsgBox('Exit Citrus from its tray menu, then retry uninstalling.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Current: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CitrusJpegConverter', Current) then
      if CompareText(Current, '"' + ExpandConstant('{app}\Citrus.exe') + '" --background') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CitrusJpegConverter');
  end;
end;
