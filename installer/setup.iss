; BrowserMux Inno Setup Script
; Registers BrowserMux as a browser in Windows and installs all required files.
;
; Prerequisites:
;   - .NET 9 Desktop Runtime (x64) must be installed on the target machine.
;     The installer checks and offers to download if missing.
;   - Windows App SDK runtime is bundled (deployed from out/).
;
; Build: pwsh build.ps1 -Config Release, then compile this .iss with Inno Setup 6.

#define AppName      "BrowserMux"
#define AppVersion   "1.0.0"
#define AppPublisher "BrowserMux"
#define AppURL       "https://browsermux.com"
#define AppExeName   "BrowserMux.exe"
#define HandlerExe   "BrowserMux.Handler.exe"
#define ProgId       "BrowserMuxURL"

[Setup]
AppId={{B8A2F3E1-7C4D-4A5B-9E6F-1D2C3B4A5E6F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=BrowserMux-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\src\BrowserMux.App\Assets\AppIcon.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#HandlerExe}
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
FinishedLabel=Setup has finished installing [name] on your computer.%n%nTo finish, you need to set BrowserMux as your default browser in Windows Settings. Leave the box below checked and click Finish — Windows Default Apps will open. Find "Web browser" (or search for BrowserMux) and select BrowserMux.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Main application files (everything from out/)
Source: "..\out\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; --- ProgId: URL handler pointing to the Handler exe ---
Root: HKA; Subkey: "Software\Classes\{#ProgId}";                            ValueType: string; ValueName: "";             ValueData: "{#AppName} URL Handler"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}";                            ValueType: string; ValueName: "URL Protocol";  ValueData: ""
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon";                ValueType: string; ValueName: "";             ValueData: "{app}\{#HandlerExe},0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command";         ValueType: string; ValueName: "";             ValueData: """{app}\{#HandlerExe}"" ""%1"""; Flags: uninsdeletekey

; --- StartMenuInternet: declares BrowserMux as a browser ---
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}";                          ValueType: string; ValueName: "";                  ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\DefaultIcon";              ValueType: string; ValueName: "";                  ValueData: "{app}\{#HandlerExe},0"
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\shell\open\command";       ValueType: string; ValueName: "";                  ValueData: """{app}\{#AppExeName}"""
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities";             ValueType: string; ValueName: "ApplicationName";        ValueData: "{#AppName}"
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities";             ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Browser selector for Windows"
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities";             ValueType: string; ValueName: "ApplicationIcon";        ValueData: "{app}\{#HandlerExe},0"
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities\URLAssociations"; ValueType: string; ValueName: "http";  ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "{#ProgId}"

; --- RegisteredApplications: makes it visible in Windows Settings > Default Apps ---
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\Clients\StartMenuInternet\{#AppName}\Capabilities"; Flags: uninsdeletevalue

[Run]
; Open default apps settings after install so user can set BrowserMux as default
Filename: "ms-settings:defaultapps"; Description: "Open Default Apps settings to set {#AppName} as default browser"; Flags: postinstall shellexec nowait skipifsilent
; Relaunch BrowserMux automatically when invoked by the in-app self-updater
; (the updater passes /RELAUNCH on top of /SILENT). Skipped on interactive installs.
Filename: "{app}\{#AppExeName}"; Flags: nowait runasoriginaluser; Check: WantsSilentRelaunch

[Code]
var
  DownloadPage: TDownloadWizardPage;

// True when the installer was launched by the in-app self-updater. The C# updater
// passes /RELAUNCH on top of /SILENT so we know to restart BrowserMux post-install.
function WantsSilentRelaunch(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

// Detect .NET 9 Desktop Runtime by scanning the standard install folders.
// We don't shell out to `dotnet` because the CLI is not always on PATH.
function IsDotNet9DesktopInstalled(): Boolean;
var
  BaseDir: String;
  FindRec: TFindRec;
begin
  Result := False;
  BaseDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BaseDir) then
    Exit;
  if FindFirst(BaseDir + '\9.*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

// Windows App SDK 1.7 runtime is delivered as MSIX framework packages.
// We detect it by checking for the registered package family via PowerShell.
function IsWindowsAppRuntime17Installed(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\war_check.txt');
  // Get-AppxPackage is per-user; the framework package is provisioned globally so this still finds it.
  if Exec(ExpandConstant('{cmd}'),
          '/c powershell -NoProfile -Command "Get-AppxPackage -Name Microsoft.WindowsAppRuntime.1.7 | Select-Object -ExpandProperty Version" > "' + TmpFile + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFile, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
        if Trim(Lines[I]) <> '' then
        begin
          Result := True;
          Break;
        end;
    end;
  end;
  DeleteFile(TmpFile);
end;

function RunInstaller(const FileName, Args, FriendlyName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not Exec(ExpandConstant('{tmp}\') + FileName, Args, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Failed to launch the ' + FriendlyName + ' installer.', mbError, MB_OK);
    Result := False;
  end
  else if (ResultCode <> 0) and (ResultCode <> 1641) and (ResultCode <> 3010) then
  begin
    MsgBox('The ' + FriendlyName + ' installer returned error code ' + IntToStr(ResultCode) + '.', mbError, MB_OK);
    Result := False;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  NeedDotNet, NeedWAR: Boolean;
begin
  Result := True;
  if CurPageID <> wpReady then Exit;

  NeedDotNet := not IsDotNet9DesktopInstalled();
  NeedWAR    := not IsWindowsAppRuntime17Installed();
  if not (NeedDotNet or NeedWAR) then Exit;

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add('https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-9-x64.exe', '');
  if NeedWAR then
    DownloadPage.Add('https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe', 'WindowsAppRuntimeInstall-x64.exe', '');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if NeedDotNet then
        if not RunInstaller('windowsdesktop-runtime-9-x64.exe', '/install /quiet /norestart', '.NET 9 Desktop Runtime') then
        begin
          Result := False;
          Exit;
        end;
      if NeedWAR then
        if not RunInstaller('WindowsAppRuntimeInstall-x64.exe', '--quiet', 'Windows App Runtime 1.7') then
        begin
          Result := False;
          Exit;
        end;
    except
      MsgBox('Could not download a required prerequisite:' + #13#10 + GetExceptionMessage, mbError, MB_OK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

// Ask the user during uninstall whether to wipe their settings/rules/logs.
// Defaults to keeping them, so accidental Yes-clicking won't lose data.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    UserDataDir := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(UserDataDir) then
    begin
      if MsgBox('Also remove your {#AppName} settings, rules and logs?' + #13#10 + #13#10 +
                'Location: ' + UserDataDir + #13#10 + #13#10 +
                'Choose No to keep them for a future reinstall.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(UserDataDir, True, True, True);
      end;
    end;
  end;
end;
