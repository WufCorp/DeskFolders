; Установщик DeskFolders — папки на рабочем столе.
; Ставится для текущего пользователя (без прав администратора) в
; %LocalAppData%\Programs\DeskFolders, поэтому путь стабилен и не «уезжает».

#define MyAppName "DeskFolders"
#define MyAppVersion "0.1.0"
#define MyAppExe "DeskFolders.exe"

[Setup]
AppId={{B7A3F2E1-9C4D-4A6E-8F12-3D5A7C9E1B04}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=DeskFolders
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Установка для текущего пользователя — права администратора не нужны.
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=DeskFolders-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\DeskFolders\icon.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
; Если приложение запущено — мастер предложит его закрыть (по имени мьютекса).
AppMutex=DeskFolders_SingleInstance_v1
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Запускать {#MyAppName} при старте Windows"; GroupDescription: "Автозапуск:"

[Files]
Source: "..\DeskFolders\publish-sc\*"; DestDir: "{app}"; \
    Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Автозапуск через HKCU\...\Run (если выбрана задача). То же самое приложение
; умеет включать/выключать из окна настроек и из трея.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "DeskFolders"; \
    ValueData: """{app}\{#MyAppExe}"""; \
    Tasks: autostart; Flags: uninsdeletevalue
; При удалении всегда чистим запись автозапуска, даже если её создало само приложение.
; ValueType: none — ничего не пишем при установке, но удаляем при деинсталляции.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "DeskFolders"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; \
    ValueType: none; ValueName: "DeskFolders"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent
