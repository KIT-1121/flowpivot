#define PreviewVersion "0.1.0-preview.1"
#define Title "FlowPivot Preview"
[Setup]
AppId={{961F670D-54C8-4069-B83C-E737A6BEC887}
AppName={#Title}
AppVersion={#PreviewVersion}
AppVerName=流枢 FlowPivot · 离线预览
VersionInfoVersion=0.1.0.1
DefaultDirName={localappdata}\Programs\FlowPivot Preview
DisableProgramGroupPage=yes
DefaultGroupName=FlowPivot Preview
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64os
MinVersion=10.0.17763
UninstallDisplayIcon={app}\FlowPivot.exe
SetupIconFile={#Payload}\FlowPivot.ico
OutputDir={#Output}
OutputBaseFilename=FlowPivot-{#PreviewVersion}-windows-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AllowNoIcons=yes

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.OptionalDesktop=创建桌面快捷方式
zhcn.StartPreview=打开 FlowPivot 离线预览
en.OptionalDesktop=Create a desktop shortcut
en.StartPreview=Launch FlowPivot offline preview

[Tasks]
Name: "desktopicon"; Description: "{cm:OptionalDesktop}"; Flags: unchecked

[Files]
Source: "{#Payload}\FlowPivot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\WebView2-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\WebView2-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Payload}\web\index.html"; DestDir: "{app}\web"; Flags: ignoreversion
Source: "{#Payload}\web\visuals.js"; DestDir: "{app}\web"; Flags: ignoreversion
Source: "{#Payload}\web\app.js"; DestDir: "{app}\web"; Flags: ignoreversion
Source: "{#Payload}\web\app.css"; DestDir: "{app}\web"; Flags: ignoreversion

[Icons]
Name: "{group}\流枢 FlowPivot 预览"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\流枢 FlowPivot 预览"; Filename: "{app}\FlowPivot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FlowPivot.exe"; Description: "{cm:StartPreview}"; Flags: nowait postinstall skipifsilent unchecked
