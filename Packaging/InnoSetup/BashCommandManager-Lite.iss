; BashCommandManager 精简安装包 - 不包含 .NET 8 运行时（需要用户已安装）
#define MyAppName "BashCommandManager"
#define MyAppVersion "1.3.0"
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
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "addtocli"; Description: "Add bcm CLI to PATH"; GroupDescription: "Command line:"; Flags: unchecked

[Files]
Source: "Lite\bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "Lite\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; PATH 只在 {app}\cli\bcm.exe 存在、且按 ';' 拆分后还没有 {app}\cli 时才追加（幂等，重复安装不会重复追加）。
; 空 Path 与非空 Path 分两条写入，避免 {olddata} 前面多出分号，也避免覆盖已有内容。
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{app}\cli"; Tasks: addtocli; Flags: preservestringtype; Check: ShouldAddCliToPathAndPathIsEmpty
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; Tasks: addtocli; Flags: preservestringtype; Check: ShouldAddCliToPathAndPathHasValue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function ShouldAddCliToPath(): Boolean;
var
  Existing: String;
  CliDir: String;
  Remainder: String;
  Part: String;
  P: Integer;
begin
  Result := False;

  // CLI 没有随包发布时不要写 PATH，避免留下失效目录。
  if not FileExists(ExpandConstant('{app}\cli\bcm.exe')) then
  begin
    Exit;
  end;

  CliDir := ExpandConstant('{app}\cli');

  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) then
  begin
    Result := True;
    Exit;
  end;

  Result := True;
  Remainder := Existing;
  while Remainder <> '' do
  begin
    P := Pos(';', Remainder);
    if P = 0 then
    begin
      Part := Remainder;
      Remainder := '';
    end
    else
    begin
      Part := Copy(Remainder, 1, P - 1);
      Remainder := Copy(Remainder, P + 1, Length(Remainder) - P);
    end;

    Part := Trim(Part);
    // 兼容带引号的 PATH 条目。
    if (Length(Part) >= 2) and (Copy(Part, 1, 1) = '"') and (Copy(Part, Length(Part), 1) = '"') then
    begin
      Part := Copy(Part, 2, Length(Part) - 2);
    end;

    if SameText(Part, CliDir) then
    begin
      // 已在 PATH 中：不重复追加。
      Result := False;
      Exit;
    end;
  end;
end;

function UserPathHasValue(): Boolean;
var
  Existing: String;
begin
  Result := RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) and (Trim(Existing) <> '');
end;

function UserPathIsEmpty(): Boolean;
var
  Existing: String;
begin
  Result := (not RegQueryStringValue(HKCU, 'Environment', 'Path', Existing)) or (Trim(Existing) = '');
end;

function ShouldAddCliToPathAndPathIsEmpty(): Boolean;
begin
  Result := ShouldAddCliToPath() and UserPathIsEmpty();
end;

function ShouldAddCliToPathAndPathHasValue(): Boolean;
begin
  Result := ShouldAddCliToPath() and UserPathHasValue();
end;

// 卸载时只移除本安装的 {app}\cli 段，绝不删除整个 Path 值。
procedure RemoveCliFromUserPath();
var
  Existing: String;
  CliDir: String;
  Remainder: String;
  Part: String;
  Rebuilt: String;
  Removed: Boolean;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) then
  begin
    Exit;
  end;

  CliDir := ExpandConstant('{app}\cli');
  Rebuilt := '';
  Removed := False;
  Remainder := Existing;

  while Remainder <> '' do
  begin
    P := Pos(';', Remainder);
    if P = 0 then
    begin
      Part := Remainder;
      Remainder := '';
    end
    else
    begin
      Part := Copy(Remainder, 1, P - 1);
      Remainder := Copy(Remainder, P + 1, Length(Remainder) - P);
    end;

    if SameText(Trim(Part), CliDir) then
    begin
      Removed := True;
    end
    else
    begin
      if Rebuilt = '' then
      begin
        Rebuilt := Part;
      end
      else
      begin
        Rebuilt := Rebuilt + ';' + Part;
      end;
    end;
  end;

  if not Removed then
  begin
    Exit;
  end;

  if Rebuilt = '' then
  begin
    RegDeleteValue(HKCU, 'Environment', 'Path');
  end
  else
  begin
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Rebuilt);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveCliFromUserPath();
  end;
end;

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
