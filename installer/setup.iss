#define MyAppName "CliManager"
#ifndef MyAppVersion
#define MyAppVersion "0.0.3"
#endif
#define MyAppPublisher "anyaoqi"
#define MyAppURL "https://github.com/anyaoqi/Cli-Manager"
#define MyAppExeName "CliManager.App.exe"

[Setup]
; App Identity
AppId={{6BC5C0D1-546E-46B2-B91D-BBB3ABF48748}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default Installation Directory:
; Lowest privilege allows non-admin install to %LOCALAPPDATA%\Programs\CliManager
; Admin install defaults to C:\Program Files\CliManager
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible

; License and Output
LicenseFile=..\LICENSE
; 安装包 EXE 图标（向导、资源管理器、卸载列表均展示应用 Logo）
SetupIconFile=..\src\CliManager.App\Assets\appicon.ico
OutputDir=..\artifacts\release
OutputBaseFilename=CliManager-v{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Uninstallation metadata
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Distribute all publish files
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
