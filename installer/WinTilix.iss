#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{8B9EE5B5-A28C-4D5C-BD93-73FE26EE178A}
AppName=WinTilix
AppVersion={#AppVersion}
AppPublisher=buffalo-analytics
AppPublisherURL=https://github.com/buffalo-analytics/WinTilix
AppSupportURL=https://github.com/buffalo-analytics/WinTilix/issues
AppUpdatesURL=https://github.com/buffalo-analytics/WinTilix/releases
DefaultDirName={localappdata}\Programs\WinTilix
DefaultGroupName=WinTilix
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=WinTilix-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\WinTilix.exe
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinTilix"; Filename: "{app}\WinTilix.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\WinTilix"; Filename: "{app}\WinTilix.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WinTilix.exe"; Description: "Launch WinTilix"; Flags: nowait postinstall skipifsilent
