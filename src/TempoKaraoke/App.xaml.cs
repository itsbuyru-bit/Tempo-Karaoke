using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TempoKaraoke;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

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
        // Вместо этого активируем окно уже запущенного экземпляра.
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
            }
        }
        catch (Exception ex)
        {
            // Ограниченные окружения могут запретить именованный mutex.
            // Лучше запуститься, чем упасть до создания окна.
            Services.LogService.Warn($"Single-instance mutex unavailable: {ex.Message}");
        }

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
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
        Services.StartupTrace.Mark("Window activated");
    }

    private static void ActivateExistingWindow()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var handle = FindWindow(null, "Tempo Karaoke");
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 9 /* SW_RESTORE */);
                SetForegroundWindow(handle);
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
}
