; Установщик Tempo Karaoke (Inno Setup 7+).
; Схема: установка в профиль пользователя без UAC, а обновление запускается из
; приложения тем же установщиком в тихом режиме.
;
; Сборка:
;   dotnet publish src\TempoKaraoke\TempoKaraoke.csproj -c Release -p:PublishProfile=portable-x64
;   iscc installer\TempoKaraoke.iss
;
; Версия по умолчанию читается из TempoKaraoke.exe; /DMyAppVersion=… переопределяет.
; Сборка self-contained: Windows App SDK и .NET лежат рядом с exe, поэтому
; установщик ничего не доустанавливает и не требует прав администратора.

#define MyAppName "Tempo Karaoke"
#define MyAppPublisher "Cappuccino Dev"
#define MyAppExeName "TempoKaraoke.exe"
#define MyAppUrl "https://github.com/itsbuyru-bit/Tempo-Karaoke"
#define MyAppId "{{8ECFDE07-71F6-4AC1-9968-53F119FF033C}"

#ifndef PublishDir
  #define PublishDir "..\src\TempoKaraoke\bin\Release\net10.0-windows10.0.26100.0\win-x64\portable"
#endif

; Версия из PE-ресурсов собранного exe: всегда 4 компонента (X.Y.Z.W).
#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(AddBackslash(PublishDir) + MyAppExeName)
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
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

[CustomMessages]
russian.DeleteUserDataPrompt=Удалить также настройки, очередь и кэш библиотеки?%n%1%n%nЕсли оставить (Нет), данные сохранятся для следующей установки.
english.DeleteUserDataPrompt=Also delete settings, playback queue, and library cache?%n%1%n%nIf you choose No, your data will be kept for future installations.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Исключаем отладочные файлы и случайные локальные конфиги/логи из папки сборки.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib,settings.json,logs\*,crash-reports\*,Reports\*,media.db,queue.json,playlists.json,remote-clients.json"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; AppUserModelID связывает ярлык с уведомлениями приложения.
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "CappuccinoDev.TempoKaraoke"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "CappuccinoDev.TempoKaraoke"

[Run]
; Обычная установка: галочка «Запустить» на финальной странице.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall runasoriginaluser; Check: not WizardSilent
; Тихая установка (обновление из приложения): запускаем новую версию без галочки.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /t /im {#MyAppExeName}"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillTempoKaraoke"

[UninstallDelete]
; Маркер области установки создаётся кодом, поэтому Inno сам его не удалит.
Type: files; Name: "{app}\install-scope.txt"

[Code]
procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  // Полный путь: Exec не ищет в PATH. /t завершает и дочерние процессы.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /t /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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

  // Язык сообщения — выбранный в установщике (russian/english).
  // MB_DEFBUTTON2 делает безопасной кнопку «Нет».
  DeleteUserData := MsgBox(
    FmtMessage(CustomMessage('DeleteUserDataPrompt'), [UserDataDir]),
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    KillRunningApp();
    DelTree(UserDataDir, True, True, True);
  end;
end;
