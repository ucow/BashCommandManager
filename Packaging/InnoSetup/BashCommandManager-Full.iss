; BashCommandManager 完整安装包 - 包含 .NET 8 运行时
#define MyAppName "BashCommandManager"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "BashCommandManager"
#define MyAppURL "https://github.com/ucow/BashCommandManager"
#define MyAppExeName "BashCommandManager.exe"

[Setup]
AppId={{B8A5F4D2-E1C3-4A5B-9D8E-7F6G5H4I3J2K}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\bin\Publish\Installer
OutputBaseFilename=BashCommandManager-v{#MyAppVersion}-Full-Setup
SetupIconFile=..\..\Assets\app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "Full\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Full\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
