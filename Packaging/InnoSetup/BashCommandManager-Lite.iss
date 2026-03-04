; BashCommandManager 精简安装包 - 不包含 .NET 8 运行时（需要用户已安装）
#define MyAppName "BashCommandManager"
#define MyAppVersion "1.0.0"
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
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "Lite\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Lite\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

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
    if MsgBox('检测到您尚未安装 .NET 8 运行时。' + #13#10 +
              '本精简版需要 .NET 8 运行时才可运行。' + #13#10 + #13#10 +
              '是否立即打开下载页面？' + #13#10 +
              '或点击"否"继续安装（程序可能无法运行）。',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
      Result := false;
    end;
  end;
end;
