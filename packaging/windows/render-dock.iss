#define ProductName "Render Dock"
#define ProductVersion "1.0.0"
#define Publisher "BKE Digital Solutions"
#define PublishDir "..\..\artifacts\publish\win-x64"

[Setup]
AppId={{D90963BB-38D2-41AF-BB40-94D763C7D6DE}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher={#Publisher}
DefaultDirName={autopf}\BKE Digital Solutions\Render Dock
DefaultGroupName=BKE Digital Solutions
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\..\artifacts\installer
OutputBaseFilename=Render-Dock-1.0.0-Windows-x64
SetupIconFile=..\..\BKE_RENDER_DOCK\Assets\Render Dock.ico
UninstallDisplayIcon={app}\RENDER DOCK.exe
PrivilegesRequired=admin
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BKE Digital Solutions\Render Dock"; Filename: "{app}\RENDER DOCK.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Render Dock"; Filename: "{app}\RENDER DOCK.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\RENDER DOCK.exe"; Description: "Launch Render Dock"; Flags: nowait postinstall skipifsilent
