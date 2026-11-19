#ifndef SetupVersion
  #define SetupVersion GetFileProductVersion("dist\AnimeSSS.Local.next.exe")
#endif
#ifdef TestInstall
  #define ManagerId "AnimeSSS.DukhiDukhi.Manager.Test"
  #define SetupName "DukhiDukhi-Test-Setup"
  #define SetupLock "Local\AnimeSSS.DukhiDukhi.Setup.Test"
  #define ManagerLock "Local\AnimeSSS.DukhiDukhi.Manager.Test"
#else
  #define ManagerId "AnimeSSS.DukhiDukhi.Manager"
  #define SetupName "DukhiDukhi-Manager-Setup"
  #define SetupLock "Local\AnimeSSS.DukhiDukhi.Setup"
  #define ManagerLock "Local\AnimeSSS.DukhiDukhi.Manager"
#endif

[Setup]
AppId={#ManagerId}
AppName=Духи Духи менеджер
AppVersion={#SetupVersion}
VersionInfoVersion={#SetupVersion}
VersionInfoProductName=Духи Духи менеджер
SetupMutex={#SetupLock}
DefaultDirName={localappdata}\Programs\DukhiDukhiManager
DefaultGroupName=Духи Духи менеджер
DisableProgramGroupPage=yes
#ifdef TestInstall
Uninstallable=no
#endif
UninstallDisplayIcon={app}\dist\AnimeSSS.Local.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
#ifndef ManagerOnly
SetupIconFile=dist\app\app-icon.ico
#endif
OutputDir=.
OutputBaseFilename={#SetupName}
CloseApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "dist\AnimeSSS.Local.next.exe"; DestDir: "{app}\dist"; DestName: "AnimeSSS.Local.exe"; Flags: ignoreversion
Source: "inspector-extension\*"; DestDir: "{app}\dist\extensions\dukhi-inspector"; Flags: ignoreversion
#ifndef ManagerOnly
Source: "dist\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}\dist"; Flags: ignoreversion
Source: "dist\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}\dist"; Flags: ignoreversion
Source: "dist\WebView2Loader.dll"; DestDir: "{app}\dist"; Flags: ignoreversion
Source: "dist\app\app-icon.ico"; DestDir: "{app}\dist\app"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "dist\extensions\tampermonkey-legacy-quick-controls\*"; DestDir: "{app}\dist\extensions\tampermonkey-legacy-quick-controls-updates"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "dist\Dukhi.Reminders.exe"; DestDir: "{app}\dist"; Flags: ignoreversion
Source: "clicker-native-host.json"; DestDir: "{app}\dist"; Flags: ignoreversion
Source: "dist\blocker-filters\*"; DestDir: "{app}\dist\blocker-filters"; Flags: ignoreversion
Source: "Update-PatchedClient.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Update-PatchedClient.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "EXTENSIONS_RU.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "UPDATE_CLIENT_RU.md"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
#ifndef ManagerOnly
#ifndef TestInstall
Name: "{autoprograms}\Духи Духи менеджер"; Filename: "{app}\dist\AnimeSSS.Local.exe"; WorkingDir: "{app}\dist"
Name: "{autodesktop}\Духи Духи менеджер"; Filename: "{app}\dist\AnimeSSS.Local.exe"; WorkingDir: "{app}\dist"; Tasks: desktopicon
#endif
#endif

[Run]
Filename: "{app}\dist\AnimeSSS.Local.exe"; Description: "Открыть Духи Духи менеджер"; Flags: postinstall nowait skipifsilent
#ifndef TestInstall
Filename: "{app}\dist\AnimeSSS.Local.exe"; Parameters: "--after-manager-update"; Flags: nowait; Check: IsAutomaticManagerUpdate
#endif

[INI]
#ifndef ManagerOnly
Filename: "{app}\dist\manager-install.ini"; Section: "Manager"; Key: "Installed"; String: "1"; Flags: uninsdeletesection
#endif

#ifndef ManagerOnly
#ifndef TestInstall
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.dukhidukhi.clicker"; ValueType: string; ValueData: "{app}\dist\clicker-native-host.json"; Flags: uninsdeletekey
#endif
#endif

[UninstallDelete]
Type: files; Name: "{app}\dist\app\animesss-desktop.exe"
Type: files; Name: "{app}\dist\app\app-icon.ico"
Type: files; Name: "{app}\dist\app\VERSION_HISTORY.txt"
Type: files; Name: "{app}\dist\CLIENT_PATCH_REPORT.json"
Type: files; Name: "{app}\dist\CLIENT_PATCH.lock"
Type: files; Name: "{app}\dist\tampermonkey-attached.txt"
Type: files; Name: "{app}\dist\manager-install.ini"
Type: dirifempty; Name: "{app}\dist\app"
Type: dirifempty; Name: "{app}\dist"
Type: dirifempty; Name: "{app}"

[Code]
function IsAutomaticManagerUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:DUKHIAUTOUPDATE|0}') = '1';
end;

function InitializeSetup(): Boolean;
var
  I: Integer;
begin
  Result := True;
  if not IsAutomaticManagerUpdate() then Exit;
  for I := 1 to 150 do
  begin
    if not CheckForMutexes('{#ManagerLock}') then Exit;
    Sleep(100);
  end;
  Result := not CheckForMutexes('{#ManagerLock}');
end;

#ifdef ManagerOnly
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not FileExists(ExpandConstant('{app}\dist\manager-install.ini')) or
     not FileExists(ExpandConstant('{app}\dist\AnimeSSS.Local.exe')) then
    Result := 'Это обновление для уже установленного менеджера. Выберите его папку.';
end;
#endif
