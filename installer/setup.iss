; BrowserMux Inno Setup Script
; Registers BrowserMux as a browser in Windows and installs all required files.
;
; Prerequisites:
;   - .NET {#DotNetChannel} Desktop Runtime (x64) must be installed on the target machine.
;     The installer checks and offers to download if missing.
;   - Windows App SDK runtime is bundled (deployed from out/).
;
; Build: pwsh build.ps1 -Config Release, then compile this .iss with Inno Setup 6.

#define AppName      "BrowserMux"
#define AppVersion   "1.0.0"
#define AppPublisher "BrowserMux"
#define AppURL       "https://github.com/alxbd/browsermux"
#define AppExeName   "BrowserMux.exe"
#define HandlerExe   "BrowserMux.Handler.exe"
#define ProgId       "BrowserMuxURL"
#define RunKey       "Software\Microsoft\Windows\CurrentVersion\Run"
; First release that answers WM_QUERYENDSESSION / WM_ENDSESSION, i.e. that lets the
; Restart Manager close it. Older installs have to be terminated (see PrepareToInstall).
; 1.0.0, 1.0.1 and 1.0.2 all shipped without the handler.
; Not touched by scripts/set-version.ps1 — it is a fact about the past, not the version.
#define FirstSelfClosingVersion "1.0.3"
; .NET Desktop Runtime the app is built against. Keep in sync with TargetFramework.
; Channel drives the download URL, Major the installed-folder glob.
#define DotNetChannel           "10.0"
#define DotNetMajor             "10"
; Windows App SDK runtime the app is built against. Keep in sync with the
; Microsoft.WindowsAppSDK PackageReference in src/BrowserMux.App/BrowserMux.App.csproj.
#define WinAppRuntimeChannel    "2.3"
#define WinAppRuntimePackage    "Microsoft.WindowsAppRuntime.2"
#define WinAppRuntimeMinVersion "2.3.0.0"

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
FinishedLabel=Setup has finished installing [name] on your computer.%n%nTo finish, you need to set BrowserMux as your default browser in Windows Settings. Leave the boxes below checked and click Finish. Windows Default Apps will open: find "Web browser" (or search for BrowserMux) and select BrowserMux.

[Tasks]
Name: "startupicon"; Description: "Start {#AppName} with Windows"; GroupDescription: "Additional tasks:"
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

; --- Start with Windows ---
; Always HKCU, never HKA: the entry must stay per-user so the in-app toggle
; (Settings > General) can change it without admin rights.
; IsInteractiveInstall skips both lines on silent runs — the self-updater passes /SILENT,
; where Inno falls back to default task selection and would otherwise silently re-enable
; startup for a user who turned it off in the app.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon;     Check: IsInteractiveInstall
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none;   ValueName: "{#AppName}";                                      Flags: deletevalue;      Tasks: not startupicon; Check: IsInteractiveInstall

[Run]
; Start the app right after install (Finished page checkbox, checked by default).
; runasoriginaluser matters for an "all users" install: the app must run as the signed-in user.
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} now"; Flags: postinstall nowait skipifsilent runasoriginaluser
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

// False during silent runs (self-updater). Used to leave the "Start with Windows"
// registry value untouched when the user is not actually looking at the wizard.
function IsInteractiveInstall(): Boolean;
begin
  Result := not WizardSilent();
end;

// On an upgrade, the Tasks checkbox must reflect reality (the Run value) rather than
// Inno's remembered task state — the user may have toggled startup off inside the app.
// On a fresh install nothing is overridden, so the task stays checked by default.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpSelectTasks then Exit;

  if RegValueExists(HKEY_CURRENT_USER, '{#RunKey}', '{#AppName}') then
    WizardSelectTasks('*startupicon')
  else if FileExists(ExpandConstant('{app}\{#AppExeName}')) then
    WizardSelectTasks('*!startupicon');
end;

// Upgrades over a version older than {#FirstSelfClosingVersion} have to be forced.
// Those builds never answered the Restart Manager's shutdown request — the picker window
// cancels WM_CLOSE to hide into the tray and nothing handled WM_ENDSESSION — so
// CloseApplications waited 30 seconds and then failed with "Setup was unable to
// automatically close all applications", leaving the old exe locked.
//
// PrepareToInstall runs before Setup's Restart Manager step, so the process is already gone
// by the time Setup looks. Newer builds are left alone on purpose: they close themselves
// cleanly and remove their tray icon, where a kill would leave a ghost icon behind.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, FirstSelfClosing: Int64;
  ResultCode: Integer;
begin
  Result := '';

  // No readable exe at the target = fresh install, nothing to close.
  if not GetPackedVersion(ExpandConstant('{app}\{#AppExeName}'), InstalledVersion) then Exit;
  if not StrToVersion('{#FirstSelfClosingVersion}', FirstSelfClosing) then Exit;
  if ComparePackedVersion(InstalledVersion, FirstSelfClosing) >= 0 then Exit;

  Log('Installed build predates {#FirstSelfClosingVersion}: terminating it, it cannot close itself.');
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}',  '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#HandlerExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Detect the .NET Desktop Runtime by scanning the standard install folders.
// We don't shell out to `dotnet` because the CLI is not always on PATH.
function IsDotNetDesktopInstalled(): Boolean;
var
  BaseDir: String;
  FindRec: TFindRec;
begin
  Result := False;
  BaseDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BaseDir) then
    Exit;
  if FindFirst(BaseDir + '\{#DotNetMajor}.*', FindRec) then
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

// The Windows App SDK runtime is delivered as MSIX framework packages, detected through
// PowerShell. Two things changed with the 2.x line and both bite silently:
//   - the package is named Microsoft.WindowsAppRuntime.2, with no minor suffix. The 1.x
//     scheme was Microsoft.WindowsAppRuntime.1.7, so "...2.3" finds nothing and we would
//     reinstall the runtime on every single run.
//   - one package now covers the whole 2.x line, so presence alone is not enough: a machine
//     can carry 2.2.0.0 while we need {#WinAppRuntimeMinVersion}. The version is compared,
//     not just looked for.
// PowerShell does the comparison and prints OK, which keeps the Pascal side to a substring test.
function IsWindowsAppRuntimeInstalled(): Boolean;
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
          '/c powershell -NoProfile -Command "$v = (Get-AppxPackage -Name {#WinAppRuntimePackage} | ForEach-Object { [version]$_.Version } | Sort-Object -Descending | Select-Object -First 1); if ($v -and $v -ge [version]''{#WinAppRuntimeMinVersion}'') { ''OK'' }" > "' + TmpFile + '" 2>&1',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFile, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
        if Trim(Lines[I]) = 'OK' then
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

  NeedDotNet := not IsDotNetDesktopInstalled();
  NeedWAR    := not IsWindowsAppRuntimeInstalled();
  if not (NeedDotNet or NeedWAR) then Exit;

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add('https://aka.ms/dotnet/{#DotNetChannel}/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-x64.exe', '');
  if NeedWAR then
    DownloadPage.Add('https://aka.ms/windowsappsdk/{#WinAppRuntimeChannel}/latest/windowsappruntimeinstall-x64.exe', 'WindowsAppRuntimeInstall-x64.exe', '');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      if NeedDotNet then
        if not RunInstaller('windowsdesktop-runtime-x64.exe', '/install /quiet /norestart', '.NET {#DotNetChannel} Desktop Runtime') then
        begin
          Result := False;
          Exit;
        end;
      if NeedWAR then
        if not RunInstaller('WindowsAppRuntimeInstall-x64.exe', '--quiet', 'Windows App Runtime {#WinAppRuntimeChannel}') then
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
