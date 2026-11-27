[Setup]
AppId={{1F156DD0-1ABC-4D48-BB6C-A73862FA9515}
AppName=FlowPivot
AppVersion={#Version}
AppVerName=流枢 FlowPivot {#Version}
VersionInfoVersion={#FileVersion}
DefaultDirName={localappdata}\Programs\FlowPivot
DefaultGroupName=FlowPivot
SetupMutex=Local\FlowPivot.Windows.Setup
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousTasks=no
SetupIconFile={#AppIcon}
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\FlowPivot.exe
OutputDir={#Output}
OutputBaseFilename=FlowPivot-{#Version}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.Desktop=创建桌面快捷方式
en.Desktop=Create a desktop shortcut
zhcn.StartClient=打开流枢 FlowPivot
en.StartClient=Launch FlowPivot
zhcn.BadUpgrade=无法确认旧版流枢的卸载程序。现有安装未被移除，请在 Windows「已安装的应用」中核对后重试。
en.BadUpgrade=The previous FlowPivot uninstaller could not be verified. Check it in Windows Installed apps and try again.
zhcn.StopFailed=未能退出流枢。请确认旧版已从托盘退出，必要时用相同 Windows 用户关闭以管理员身份运行的旧版，再重试。
en.StopFailed=FlowPivot could not be closed. Exit it from the tray, including any elevated copy, and try again.
zhcn.UninstallFailed=旧版卸载未完成。没有继续安装新版，用户数据未清理。请检查 Windows「已安装的应用」，必要时重启后重试。
en.UninstallFailed=The previous version did not finish uninstalling. Setup did not install the new version or clear user data. Check Windows Installed apps and retry after restarting if needed.
zhcn.ProxyRecoverFailed=未能自动确认或恢复之前的系统代理。若无法联网，请检查 Windows「网络和 Internet → 代理」。
en.ProxyRecoverFailed=The previous system proxy could not be verified or restored automatically. If you cannot connect, check Windows Network & internet > Proxy.

[Tasks]
Name: "desktopicon"; Description: "{cm:Desktop}"

[Files]
Source: "{#UpgradeHelper}"; Flags: dontcopy
Source: "{#Payload}\FlowPivot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\FlowPivot-{#Version}.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\*-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\WebView2-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\THIRD-PARTY-CORES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\cores\*"; DestDir: "{app}\cores"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion

[Icons]
Name: "{group}\流枢 FlowPivot"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FlowPivot-{#Version}.ico"
Name: "{userdesktop}\流枢 FlowPivot"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FlowPivot-{#Version}.ico"; Check: ShouldCreateDesktopIcon

[Run]
Filename: "{app}\FlowPivot.exe"; Description: "{cm:StartClient}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  UpgradeKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{1F156DD0-1ABC-4D48-BB6C-A73862FA9515}_is1';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ClientMutex = 'Local\FlowPivot.Windows.Client';
var
  KeepDesktopIcon, KeepAutoStart, ProxyRecoveryWarning: Boolean;

function ExistingDesktopIconTargets(const AppPath: String): Boolean;
var LinkPath: String;
    Shell, Shortcut: Variant;
begin
  Result := False;
  LinkPath := ExpandConstant('{userdesktop}\流枢 FlowPivot.lnk');
  if not FileExists(LinkPath) then Exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(LinkPath);
    Result := CompareText(Shortcut.TargetPath, AppPath) = 0;
  except
    Log('Could not verify an existing desktop shortcut.');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var OldDir, OldUninstaller, OldExe, SavedRun, Name: String;
    ExitCode, I: Integer;
begin
  Result := '';
  OldDir := '';
  if RegKeyExists(HKCU, UpgradeKey) then begin
    if not RegQueryStringValue(HKCU, UpgradeKey, 'InstallLocation', OldDir) or
       not RegQueryStringValue(HKCU, UpgradeKey, 'UninstallString', OldUninstaller) then begin
      Result := CustomMessage('BadUpgrade');
      Exit;
    end;
    OldDir := RemoveBackslashUnlessRoot(OldDir);
    OldUninstaller := RemoveQuotes(OldUninstaller);
    OldExe := AddBackslash(OldDir) + 'FlowPivot.exe';
    Name := Lowercase(ExtractFileName(OldUninstaller));
    if not DirExists(OldDir) or (Copy(OldDir, 2, 2) <> ':\') or
       (CompareText(RemoveBackslashUnlessRoot(ExtractFilePath(OldUninstaller)), OldDir) <> 0) or
       (Length(Name) <> 12) or (Copy(Name, 1, 5) <> 'unins') or (Copy(Name, 9, 4) <> '.exe') or
       (Pos(Copy(Name, 6, 1), '0123456789') = 0) or
       (Pos(Copy(Name, 7, 1), '0123456789') = 0) or
       (Pos(Copy(Name, 8, 1), '0123456789') = 0) or
       not FileExists(OldUninstaller) or not FileExists(ChangeFileExt(OldUninstaller, '.dat')) then begin
      Result := CustomMessage('BadUpgrade');
      Exit;
    end;
    KeepDesktopIcon := KeepDesktopIcon or ExistingDesktopIconTargets(OldExe);
    KeepAutoStart := KeepAutoStart or (RegQueryStringValue(HKCU, RunKey, 'FlowPivot', SavedRun) and
      (CompareText(SavedRun, '"' + OldExe + '"') = 0));
  end;

  try
    ExtractTemporaryFile('FlowPivotUpgrade.exe');
  except
    Result := CustomMessage('StopFailed');
    Exit;
  end;
  if not Exec(ExpandConstant('{tmp}\FlowPivotUpgrade.exe'), '"' + OldDir + '"',
       ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := CustomMessage('StopFailed');
    Exit;
  end;
  if ((ExitCode <> 0) and (ExitCode <> 5)) or CheckForMutexes(ClientMutex) then begin
    Result := CustomMessage('StopFailed');
    Exit;
  end;
  ProxyRecoveryWarning := ExitCode = 5;
  if OldDir = '' then Exit;

  Log('Uninstalling the previous FlowPivot application (user data retained).');
  if not Exec(OldUninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', OldDir, SW_HIDE,
       ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
    Result := CustomMessage('UninstallFailed');
    Exit;
  end;
  for I := 1 to 40 do begin
    if not FileExists(OldUninstaller) and not FileExists(OldExe) and not RegKeyExists(HKCU, UpgradeKey) then Break;
    Sleep(200);
  end;
  if FileExists(OldUninstaller) or FileExists(OldExe) or RegKeyExists(HKCU, UpgradeKey) then
    Result := CustomMessage('UninstallFailed')
  else if CheckForMutexes(ClientMutex) then
    Result := CustomMessage('StopFailed');
end;

function ShouldCreateDesktopIcon: Boolean;
begin
  Result := WizardIsTaskSelected('desktopicon') or KeepDesktopIcon or
    ExistingDesktopIconTargets(ExpandConstant('{app}\FlowPivot.exe'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Value: String;
begin
  if CurStep <> ssPostInstall then Exit;
  if KeepAutoStart and not RegQueryStringValue(HKCU, RunKey, 'FlowPivot', Value) then
    if not RegWriteStringValue(HKCU, RunKey, 'FlowPivot', '"' + ExpandConstant('{app}\FlowPivot.exe') + '"') then
      Log('Unable to restore the startup preference.');
  if ProxyRecoveryWarning then begin
    Log('The existing Windows proxy settings were not recovered.');
    if not WizardSilent then MsgBox(CustomMessage('ProxyRecoverFailed'), mbInformation, MB_OK);
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := not CheckForMutexes(ClientMutex);
  if not Result and not UninstallSilent then MsgBox(CustomMessage('StopFailed'), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Existing: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, RunKey, 'FlowPivot', Existing) and
       (CompareText(Existing, '"' + ExpandConstant('{app}\FlowPivot.exe') + '"') = 0) then
      RegDeleteValue(HKCU, RunKey, 'FlowPivot');
  end;
end;
