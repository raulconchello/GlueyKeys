; Inno Setup Script for GlueyKeys
; This creates a Windows installer for the application

#define MyAppName "GlueyKeys"
#define MyAppVersion "0.0.2"
#define MyAppPublisher "GlueyKeys"
#define MyAppURL "https://github.com/raulconchello/GlueyKeys"
#define MyAppExeName "GlueyKeys.exe"
#define MyAppSourceDir "GlueyKeys\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; App identity
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation settings
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output settings
OutputDir=installer_output
OutputBaseFilename=GlueyKeys_Setup
SetupIconFile=GlueyKeys\Assets\icon.ico
Compression=lzma2
SolidCompression=yes

; UI settings
WizardStyle=modern
WizardSizePercent=100

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Shortcuts (unchecked by default)
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start menu shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
; App settings (checked by default - no flags means checked)
Name: "runstartup"; Description: "Run at Windows startup"; GroupDescription: "Application Settings:"
Name: "shownotifications"; Description: "Show notifications when keyboard layout changes"; GroupDescription: "Application Settings:"

[Files]
; Include all files from the publish directory
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Run at Windows startup via registry (same method the app uses)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: runstartup

[Run]
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall

[UninstallRun]
; Kill the app if running before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden

[UninstallDelete]
; Clean up app data folder
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"

[Code]
procedure CreateSettingsFile();
var
  SettingsDir: String;
  SettingsFile: String;
  JsonContent: String;
  ShowNotifications: String;
  RunAtStartup: String;
begin
  SettingsDir := ExpandConstant('{userappdata}\{#MyAppName}');
  SettingsFile := SettingsDir + '\settings.json';

  // Create directory if it doesn't exist
  if not DirExists(SettingsDir) then
    ForceDirectories(SettingsDir);

  // Get task selections
  if IsTaskSelected('shownotifications') then
    ShowNotifications := 'true'
  else
    ShowNotifications := 'false';

  if IsTaskSelected('runstartup') then
    RunAtStartup := 'true'
  else
    RunAtStartup := 'false';

  // Build JSON content (isEnabled is always true)
  JsonContent := '{' + #13#10 +
    '  "setupCompleted": true,' + #13#10 +
    '  "firstRunPromptShown": false,' + #13#10 +
    '  "isEnabled": true,' + #13#10 +
    '  "runAtStartup": ' + RunAtStartup + ',' + #13#10 +
    '  "showNotifications": ' + ShowNotifications + ',' + #13#10 +
    '  "keyboardMappings": []' + #13#10 +
    '}';

  // Write the settings file
  SaveStringToFile(SettingsFile, JsonContent, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CreateSettingsFile();
  end;
end;
