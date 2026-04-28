[Setup]
AppName=Clauddy
AppVersion=0.1.0
AppPublisher=Ron
AppPublisherURL=https://github.com/yourname/clauddy
DefaultDirName={localappdata}\Clauddy
DefaultGroupName=Clauddy
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Clauddy-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\Clauddy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\hooks\*"; DestDir: "{app}\hooks"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Clauddy"; Filename: "{app}\Clauddy.exe"
Name: "{commondesktop}\Clauddy"; Filename: "{app}\Clauddy.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\Clauddy.exe"; Description: "Launch Clauddy now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\Clauddy.exe"; Parameters: "--uninstall-hooks"; Flags: runhidden
