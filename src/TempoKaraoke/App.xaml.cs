using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using TempoKaraoke.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TempoKaraoke;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceKey = "TempoKaraoke.Main";

    private static Mutex? _singleInstanceMutex;
    private static AppInstance? _mainInstance;
    private static string? _pendingUpdateAction;

    /// <summary>Тост/редирект просит выполнить действие с обновлением.</summary>
    public static event Action<string>? UpdateActionRequested;

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        // Второй процесс не должен параллельно писать media.db/playlists.json/очередь.
        // AppLifecycle передаёт аргументы активации (например, клик по тосту)
        // уже запущенному экземпляру; при недоступности — прежний Mutex.
        SetupSingleInstance();

        InitializeComponent();

        // Заголовок сессии попадает в лог даже при выключенном обычном логировании:
        // по нему видно версию, ОС, локаль и состояние сети на момент запуска.
        Services.LogService.BeginSession(Services.DiagnosticsService.BuildSessionHeader());

        // Падения пишутся в лог всегда, даже если ведение логов выключено.
        UnhandledException += (_, e) =>
        {
            Services.LogService.Crash(e.Exception, "UI UnhandledException");
            e.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.LogService.Crash(e.ExceptionObject as Exception, "AppDomain");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Services.LogService.Crash(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };

        Services.LogService.Info("Приложение запущено.");
        Services.StartupTrace.Mark("App ctor done");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Services.StartupTrace.Mark("OnLaunched");
        Services.LogService.Info($"[update] launch arguments='{args.Arguments}'");

        // Регистрируем уведомления до создания окна: холодный старт из тоста
        // должен попасть в очередь, даже если страница ещё не загружена.
        NotificationService.UpdateActionInvoked += QueueUpdateAction;
        NotificationService.TryInitialize();

        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
        Services.StartupTrace.Mark("Window activated");

        // Холодный старт из тоста: MainPage ещё не загружена, поэтому действие
        // сохраняется в PendingUpdateAction и применяется после первого кадра.
        try
        {
            RouteActivation(AppInstance.GetCurrent().GetActivatedEventArgs());
        }
        catch (Exception ex)
        {
            Services.LogService.Warn($"Cannot read activation arguments: {ex.Message}");
        }

        TryQueueCommandLineActivation(args.Arguments);
    }

    /// <summary>
    /// Некоторые схемы запуска (portable/unpackaged) передают аргумент тоста
    /// в командной строке. Берём только явные action=/tempokaraoke: аргументы.
    /// </summary>
    private static void TryQueueCommandLineActivation(string? launchArguments)
    {
        if (_pendingUpdateAction is not null)
        {
            return;
        }

        var candidate = IsActivationArgument(launchArguments)
            ? launchArguments
            : Environment.GetCommandLineArgs()
                .Skip(1)
                .FirstOrDefault(IsActivationArgument);

        var action = UpdateActivationArguments.ParseAction(candidate);
        if (action is not null)
        {
            Services.LogService.Info($"[update] command-line activation argument='{candidate}' action='{action}'");
            QueueUpdateAction(action);
        }
    }

    private static bool IsActivationArgument(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("action=", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("tempokaraoke:", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Закладывает действие обновления в очередь и будит MainPage. Если страница
    /// ещё не подписана, действие останется в <see cref="TakePendingUpdateAction"/>.
    /// </summary>
    public static void QueueUpdateAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        _pendingUpdateAction = action;
        DispatcherQueue?.TryEnqueue(() => UpdateActionRequested?.Invoke(action));
    }

    /// <summary>Забирает действие ровно один раз (защита от двойной обработки).</summary>
    public static string? TakePendingUpdateAction()
    {
        var action = _pendingUpdateAction;
        _pendingUpdateAction = null;
        return action;
    }

    private static void SetupSingleInstance()
    {
        // Mutex first: this path is stable in unpackaged Inno builds. AppLifecycle's
        // RedirectActivationToAsync can raise a native stowed exception
        // (CoreMessagingXP / 0xc000027b) when a second process starts.
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                @"Local\TempoKaraoke.SingleInstance",
                out var createdNew);
            if (!createdNew)
            {
                ActivateExistingWindow();
                Environment.Exit(0);
                return;
            }
        }
        catch (Exception ex)
        {
            Services.LogService.Warn($"Single-instance mutex unavailable: {ex.Message}");
        }

        // Keep AppLifecycle activation routing for the first instance only.
        try
        {
            _mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            if (_mainInstance.IsCurrent)
            {
                _mainInstance.Activated += OnRedirectedActivation;
            }
        }
        catch (Exception ex)
        {
            Services.LogService.Warn($"AppLifecycle activation unavailable: {ex.Message}");
        }
    }

    private static void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        RouteActivation(args);
    }

    /// <summary>Разбирает toast/app-notification активацию и ставит действие в очередь.</summary>
    private static void RouteActivation(AppActivationArguments args)
    {
        try
        {
            var argument = GetActivationArgument(args);
            LogService.Info(
                $"[update] activation probe kind={args.Kind} data={args.Data?.GetType().Name ?? "null"} argument='{argument}'");
            var action = UpdateActivationArguments.ParseAction(argument);
            if (action is null)
            {
                // Обычный повторный запуск: поднимаем уже открытое окно.
                if (string.Equals(args.Kind.ToString(), "Launch", StringComparison.OrdinalIgnoreCase))
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        try
                        {
                            Window?.Activate();
                            SetForegroundWindow(WindowHandle);
                        }
                        catch (Exception ex)
                        {
                            Services.LogService.Warn($"Cannot activate the existing window: {ex.Message}");
                        }
                    });
                }

                return;
            }

            Services.LogService.Info(
                $"[update] activation kind={args.Kind} argument='{argument}' action='{action}'");
            QueueUpdateAction(action);
        }
        catch (Exception ex)
        {
            Services.LogService.Warn($"Activation routing failed: {ex.Message}");
        }
    }

    private static string? GetActivationArgument(AppActivationArguments args)
    {
        var kind = args.Kind.ToString();
        if (!string.Equals(kind, "AppNotification", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(kind, "Toast", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (args.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs notification)
        {
            return notification.Argument;
        }

        // Классический ToastNotificationActivatedEventArgs живёт в другой сборке;
        // читаем Argument рефлексией, чтобы не тянуть UWP-типы в hot path.
        return args.Data?.GetType().GetProperty("Argument")?.GetValue(args.Data) as string;
    }

    private static void ActivateExistingWindow()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var handle = FindWindow(null, "Tempo Karaoke");
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 9 /* SW_RESTORE */);
                if (!SetForegroundWindow(handle))
                {
                    // Windows may refuse SetForegroundWindow while the new
                    // process is still becoming foreground. Raise the window
                    // explicitly and try once more.
                    BringWindowToTop(handle);
                    SetWindowPos(
                        handle,
                        IntPtr.Zero,
                        0,
                        0,
                        0,
                        0,
                        0x0001 | 0x0002 | 0x0040 /* NOSIZE | NOMOVE | SHOWWINDOW */);
                    SetForegroundWindow(handle);
                }

                return;
            }

            Thread.Sleep(100);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
