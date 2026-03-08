; BashCommandManager 精简安装包 - 不包含 .NET 8 运行时（需要用户已安装）
#define MyAppName "BashCommandManager"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "BashCommandManager"
#define MyAppURL "https://github.com/ucow/BashCommandManager"
#define MyAppExeName "BashCommandManager.exe"

[Setup]
AppId={{C9D6G5E3-F2D4-5B6C-0E9F-8G7H6I5J4K3L}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} (Lite)
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\bin\Publish\Installer
OutputBaseFilename=BashCommandManager-v{#MyAppVersion}-Lite-Setup
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
Source: "Lite\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Lite\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  NetRuntimeInstalled: Boolean;
begin
  Result := true;

  // 检查 .NET 8 运行时是否已安装
  NetRuntimeInstalled := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.0') or
                         RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.0');

  if not NetRuntimeInstalled then
  begin
    if MsgBox('.NET 8 Runtime not detected.' + #13#10 +
              'The Lite version requires .NET 8 Runtime to run.' + #13#10 + #13#10 +
              'Open the download page now?' + #13#10 +
              'Click "No" to continue anyway (program may not work).',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
      Result := false;
    end;
  end;
end;
