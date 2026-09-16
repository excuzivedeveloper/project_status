#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\artifacts\win-x64"
#endif

[Setup]
AppId={{A88A7B13-3C3E-45F5-A13C-85F3AC19D9C4}
AppName=Project Status
AppVersion={#MyAppVersion}
AppVerName=Project Status {#MyAppVersion}
AppPublisher=Project Status
AppPublisherURL=https://github.com/excuzivedeveloper/project_status
AppSupportURL=https://github.com/excuzivedeveloper/project_status/issues
AppUpdatesURL=https://github.com/excuzivedeveloper/project_status/releases/latest
DefaultDirName={localappdata}\Programs\Project Status
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=ProjectStatus-Setup-v{#MyAppVersion}
SetupIconFile=..\ProjectStatus.Client\Assets\project_status.ico
UninstallDisplayIcon={app}\ProjectStatus.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Project Status"; Filename: "{app}\ProjectStatus.exe"
Name: "{autodesktop}\Project Status"; Filename: "{app}\ProjectStatus.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ProjectStatus.exe"; Description: "Launch Project Status"; Flags: nowait postinstall skipifsilent
