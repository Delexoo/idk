#define MyAppName "idk"
#define MyAppVersion "0.1.12"
#define MyAppPublisher "idk"
#define MyAppExeName "idk.exe"

[Setup]
AppId={{B4E91C7A-2D55-4F18-9A60-7C3E8B21D4F6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppCopyright=Copyright (C) 2026 idk
DefaultDirName={autopf}\idk
DefaultGroupName=idk
DisableProgramGroupPage=yes
LicenseFile=license.rtf
SetupIconFile=..\Idk\Assets\idk.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
UsedUserAreasWarning=no
OutputDir=..\dist-setup
OutputBaseFilename=idk-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=idk
VersionInfoCompany=idk
VersionInfoProductName=idk
VersionInfoVersion=0.1.12.0
VersionInfoCopyright=Copyright (C) 2026 idk
SetupLogging=yes
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startonlogon"; Description: "Start idk when I sign in to Windows"; GroupDescription: "Shortcuts:"; Flags: unchecked

#ifndef Payload
#define Payload "stage"
#endif

[Files]
Source: "..\dist-setup\{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: ".env,billing.env"

[Icons]
Name: "{group}\idk"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "idk selection assistant"
Name: "{group}\Uninstall idk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\idk"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "idk"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startonlogon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch idk"; Flags: nowait postinstall skipifsilent
