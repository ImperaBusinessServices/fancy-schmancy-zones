; Inno Setup script for Fancy Schmancy Zones
; Builds a one-click, per-user installer (no admin needed) that creates Start Menu
; and Desktop shortcuts, with an optional "start at sign-in" choice.

#define AppName "Fancy Schmancy Zones"
#define AppVersion "0.12.0"
#define AppPublisher "Keith Blanco"
#define AppExeName "FancySchmancyZones.exe"
#define AppId "{{B6F1B3A2-7C4E-4E2A-9C1D-9F3E5A0C2D11}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Fancy Schmancy Zones
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
; Stable filename (no version) so the website's "latest" download link never has to change.
OutputBaseFilename=FancySchmancyZones-Setup
SetupIconFile=..\src\icon.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &Desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start &automatically when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\src\bin\Release\net8.0-windows\win-x64\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; Start at sign-in is a Run entry, the same one Settings -> "Start with Windows" writes, so the
; app's checkmark always matches what the installer did. Older builds used a Startup-folder
; shortcut; InstallDelete clears it so an upgrade can't end up launching us twice.
[InstallDelete]
Type: files; Name: "{userstartup}\{#AppName}.lnk"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup
; Belt and braces: drop the entry at uninstall even if it was switched on from inside the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent
