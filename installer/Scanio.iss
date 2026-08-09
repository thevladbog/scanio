#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{B786AC90-6A74-4E80-AE30-8D3C15A8C9C2}
AppName=Scanio
AppVersion={#AppVersion}
AppPublisher=Scanio
DefaultDirName={localappdata}\Programs\Scanio
DefaultGroupName=Scanio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=Scanio-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\Scanio.Presentation\Assets\scanio.ico
UninstallDisplayIcon={app}\Scanio.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,Data\*"

[Icons]
Name: "{userprograms}\Scanio"; Filename: "{app}\Scanio.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Scanio"; Filename: "{app}\Scanio.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Scanio.exe"; Description: "{cm:LaunchProgram,Scanio}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
