#define ProductVersion "1.0.0"

[Setup]
AppId={{BKE-Render-Dock-1.0.0}}
AppName=Render Dock
AppVersion={#ProductVersion}
AppPublisher=BKE Digital Solutions
DefaultDirName={autopf}\BKE Digital Solutions\Render Dock
DefaultGroupName=BKE Digital Solutions\Render Dock
UninstallDisplayName=Render Dock
OutputDir=artifacts
OutputBaseFilename=Render-Dock-1.0.0-Windows-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Render Dock"; Filename: "{app}\RENDER DOCK.exe"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
