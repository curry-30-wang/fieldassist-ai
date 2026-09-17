[Setup]
AppId={{B8FF91DF-394C-48A8-9CF0-50B51E32B827}
AppName=沥青拌合站经营管理系统
AppVersion=1.0.2
AppPublisher=本机单机版
DefaultDirName={localappdata}\AsphaltPlantManager
DefaultGroupName=沥青拌合站经营管理系统
DisableProgramGroupPage=yes
OutputDir=C:\Users\DELL\Documents\Codex\2026-08-07\new-chat-2\outputs
OutputBaseFilename=AsphaltPlantManager-Setup-1.0.2
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayName=沥青拌合站经营管理系统

[Files]
Source: "C:\Users\DELL\Documents\Codex\2026-08-07\new-chat-2\outputs\Release-v3\AsphaltPlantManager.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\DELL\Documents\Codex\2026-08-07\new-chat-2\outputs\Release-v3\UserGuide.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\DELL\Documents\Codex\2026-08-07\new-chat-2\outputs\Release-v3\templates\*"; DestDir: "{app}\templates"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\沥青拌合站经营管理系统"; Filename: "{app}\AsphaltPlantManager.App.exe"
Name: "{group}\沥青拌合站经营管理系统"; Filename: "{app}\AsphaltPlantManager.App.exe"

[Run]
Filename: "{app}\AsphaltPlantManager.App.exe"; Description: "启动沥青拌合站经营管理系统"; Flags: nowait postinstall skipifsilent
