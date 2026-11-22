[Setup]
AppId={{1F156DD0-1ABC-4D48-BB6C-A73862FA9515}
AppName=FlowPivot
AppVersion={#Version}
AppVerName=流枢 FlowPivot {#Version}
VersionInfoVersion={#FileVersion}
DefaultDirName={localappdata}\Programs\FlowPivot
DefaultGroupName=FlowPivot
AppMutex=Local\FlowPivot.Windows.Client
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

[Tasks]
Name: "desktopicon"; Description: "{cm:Desktop}"

[Files]
Source: "{#Payload}\FlowPivot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\*-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\WebView2-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\THIRD-PARTY-CORES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\cores\*"; DestDir: "{app}\cores"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\web\*"; DestDir: "{app}\web"; Flags: ignoreversion

[Icons]
Name: "{group}\流枢 FlowPivot"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FlowPivot.exe"
Name: "{userdesktop}\流枢 FlowPivot"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FlowPivot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FlowPivot.exe"; Description: "{cm:StartClient}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Existing: String;
    Key: String;
begin
  if CurUninstallStep = usUninstall then begin
    Key := 'Software\Microsoft\Windows\CurrentVersion\Run';
    if RegQueryStringValue(HKCU, Key, 'FlowPivot', Existing) and
       (CompareText(Existing, '"' + ExpandConstant('{app}\FlowPivot.exe') + '"') = 0) then
      RegDeleteValue(HKCU, Key, 'FlowPivot');
  end;
end;
