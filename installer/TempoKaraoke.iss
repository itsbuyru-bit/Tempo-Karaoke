; Установщик Tempo Karaoke (Inno Setup 6+).
; Схема как у UniGetUI: установка в профиль пользователя без UAC, а обновление
; запускается из приложения тем же установщиком в тихом режиме.
;
; Сборка:
;   dotnet publish src\TempoKaraoke\TempoKaraoke.csproj -c Release -p:PublishProfile=portable-x64
;   iscc installer\TempoKaraoke.iss /DMyAppVersion=1.9.0.0
;
; Сборка self-contained: Windows App SDK и .NET лежат рядом с exe, поэтому
; установщик ничего не доустанавливает и не требует прав администратора.

#define MyAppName "Tempo Karaoke"
#define MyAppPublisher "Cappuccino Dev"
#define MyAppExeName "TempoKaraoke.exe"
#define MyAppId "{{8ECFDE07-71F6-4AC1-9968-53F119FF033C}"

#ifndef MyAppVersion
  #define MyAppVersion "1.9.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\src\TempoKaraoke\bin\Release\net10.0-windows10.0.26100.0\win-x64\portable"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Страница выбора папки показывается всегда: пользователь может поставить
; приложение в свой каталог (например, на другой диск).
DisableDirPage=no
DirExistsWarning=no
UsePreviousAppDir=yes
; Установка в профиль пользователя по умолчанию; диалог позволяет выбрать
; «для всех пользователей» (Program Files + UAC).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
; Тихая установка (обновление из приложения) повторяет область предыдущей
; установки: per-user остаётся per-user, all-users остаётся all-users.
UsePreviousPrivileges=yes
; Приложение работает без identity, поэтому архитектура — только x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=TempoKaraoke-Setup-{#MyAppVersion}
SetupIconFile=..\src\TempoKaraoke\Assets\AppIcon.ico
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Установщик сам закрывает приложение и helper-процессы перед заменой файлов.
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
SetupLogging=yes
; Для релиза: подписать установщик сертификатом (Azure Trusted Signing или PFX).
; SignTool=azsign
; SignedUninstaller=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; AppUserModelID связывает ярлык с уведомлениями приложения.
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "CappuccinoDev.TempoKaraoke"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "CappuccinoDev.TempoKaraoke"

[Run]
; В тихом режиме (обновление из приложения) тоже перезапускаем новую версию.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillTempoKaraoke"

[Code]
procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  // Полный путь: Exec не ищет в PATH.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(700);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Наша оболочка запускает дочерние процессы из того же exe.
  KillRunningApp();
  Result := '';
end;

// --- Маркер области установки ----------------------------------------------
// Обновление из приложения читает его и ставит новую версию в ту же область.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if IsAdminInstallMode then
      SaveStringToFile(ExpandConstant('{app}\install-scope.txt'), 'all', False)
    else
      SaveStringToFile(ExpandConstant('{app}\install-scope.txt'), 'user', False);
  end;
end;

// --- Удаление пользовательских данных --------------------------------------
// Галочка в деинсталляторе: по умолчанию данные остаются, чтобы переустановка
// не теряла настройки, очередь и кэш библиотеки.
var
  DeleteUserData: Boolean;
  UserDataDir: String;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteUserData := False;
  UserDataDir := ExpandConstant('{localappdata}\TempoKaraoke');

  // При тихой деинсталляции ничего не спрашиваем и данные не трогаем.
  if UninstallSilent or (not DirExists(UserDataDir)) then
    Exit;

  DeleteUserData := MsgBox(
    'Удалить также настройки, очередь и кэш библиотеки?' + #13#10 +
    UserDataDir + #13#10 + #13#10 +
    'Если оставить (Нет), данные сохранятся для следующей установки.',
    mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    KillRunningApp();
    DelTree(UserDataDir, True, True, True);
  end;
end;
