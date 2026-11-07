using System;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Windows.Graphics;
using Windows.UI;

namespace TempoKaraoke;

/// <summary>
/// Главное окно: применяет сохранённое положение, следит за его изменением
/// и создаёт MainPage. Тяжёлая инициализация страницы отложена до первого кадра.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _placementTimer;
    private DisplayAreaWatcher? _displayWatcher;
    private bool _applyingPlacement;
    private bool _placementReady;
    private double _lastScale = 1.0;
    private PlacementRect _lastNormalRect;

    public MainWindow()
    {
        StartupTrace.Mark("MainWindow ctor");
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        // Кнопки «свернуть/развернуть/закрыть» рисует система поверх нашего
        // заголовка; без явных цветов в светлой теме они остаются белыми.
        ApplyTitleBarTheme(ResolveTheme(SettingsService.Theme));

        ApplyPlacement(SettingsService.GetWindowPlacement(), StartupTrace.ResetWindowRequested);
        if (StartupTrace.ResetWindowRequested)
        {
            StartupTrace.ResetWindowRequested = false;
            SettingsService.ResetWindowPlacement();
        }

        _placementTimer = DispatcherQueue.CreateTimer();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(800);
        _placementTimer.IsRepeating = false;
        _placementTimer.Tick += (_, _) =>
        {
            _placementTimer.Stop();
            SavePlacement();
        };

        RootFrame.Navigated += (_, args) =>
        {
            if (args.Content is MainPage page)
            {
                SetTitleBar(page.HeaderDragArea);
            }
        };

        RootFrame.Loaded += OnRootFrameLoaded;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnWindowClosed;
        StartDisplayWatcher();

        // MainPage создаётся до активации окна: WinUI не может загрузить страницу
        // с x:Uid после Activate (MRT не разрешает ресурсы). Тяжёлые операции
        // страницы отложены в MainPage.InitializeAfterFirstRender.
        StartupTrace.Mark("Navigate -> MainPage");
        RootFrame.Navigate(typeof(MainPage));
    }

    /// <summary>Перекрашивает системные кнопки окна под выбранную тему.</summary>
    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var foreground = isDark ? Colors.White : Colors.Black;
        var inactiveForeground = isDark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 96, 96, 96);
        var hoverBackground = isDark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        var pressedBackground = isDark
            ? Color.FromArgb(40, 255, 255, 255)
            : Color.FromArgb(40, 0, 0, 0);

        var bar = AppWindow.TitleBar;
        bar.BackgroundColor = Colors.Transparent;
        bar.InactiveBackgroundColor = Colors.Transparent;
        bar.ForegroundColor = foreground;
        bar.InactiveForegroundColor = inactiveForeground;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = inactiveForeground;
        bar.ButtonHoverBackgroundColor = hoverBackground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = pressedBackground;
        bar.ButtonPressedForegroundColor = foreground;

        LogService.Info($"[window] title bar theme={(isDark ? "dark" : "light")}");
    }

    private static ElementTheme ResolveTheme(string theme) => theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Сбрасывает сохранённое положение и центрирует окно с размером по умолчанию.</summary>
    public void ResetPlacement()
    {
        SettingsService.ResetWindowPlacement();
        _placementTimer.Stop();
        ApplyPlacement(new WindowPlacementData(), forceReset: true);
    }

    private void ApplyPlacement(WindowPlacementData saved, bool forceReset)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _applyingPlacement = true;
        try
        {
            var result = WindowPlacementService.Apply(AppWindow, handle, saved, forceReset);
            _lastNormalRect = result.FinalRect;
            _lastScale = result.Scale;
            StartupTrace.Mark(
                $"Window placed ({result.Reason}) {result.FinalRect} display={result.DisplayId} " +
                $"scale={result.Scale:0.##} clamped={result.Clamped} reset={result.Reset} maximized={result.Maximized}");
        }
        catch (Exception ex)
        {
            LogService.Warn($"Window placement failed: {ex.Message}");
            FallbackPlacement(handle);
        }
        finally
        {
            _applyingPlacement = false;
            _placementReady = true;
        }
    }

    /// <summary>Последний рубеж: окно 75% рабочей области по центру.</summary>
    private void FallbackPlacement(IntPtr handle)
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = new PlacementRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height);
            var rect = WindowPlacementMath.ComputeDefault(work, WindowPlacementService.GetScale(handle));
            AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
            _lastNormalRect = rect;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Window fallback placement failed: {ex.Message}");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_applyingPlacement || !_placementReady)
        {
            return;
        }

        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
        {
            // Сохраняем только по факту изменения, с задержкой — чтобы не писать
            // settings.json на каждый пиксель перетаскивания.
            _placementTimer.Stop();
            _placementTimer.Start();
        }
    }

    private void SavePlacement()
    {
        if (_applyingPlacement || !_placementReady)
        {
            return;
        }

        try
        {
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter?.State == OverlappedPresenterState.Minimized)
            {
                return;
            }

            var maximized = presenter?.State == OverlappedPresenterState.Maximized;
            if (!maximized)
            {
                var position = AppWindow.Position;
                var size = AppWindow.Size;
                _lastNormalRect = new PlacementRect(position.X, position.Y, size.Width, size.Height);
            }

            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var data = WindowPlacementService.Capture(AppWindow, handle, _lastNormalRect, maximized);
            SettingsService.SaveWindowPlacement(data);
            StartupTrace.Mark(
                $"Window placement saved dip={data.X},{data.Y} {data.Width}x{data.Height} " +
                $"display={data.DisplayId} scale={data.DpiScale:0.##} maximized={data.Maximized}");
        }
        catch (Exception ex)
        {
            LogService.Warn($"Window placement save failed: {ex.Message}");
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _placementTimer.Stop();
        try
        {
            _displayWatcher?.Stop();
        }
        catch
        {
            // Остановка watcher не критична при выходе.
        }
    }

    private void OnRootFrameLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _lastScale = WindowPlacementService.GetScale(handle);

            if (RootFrame.XamlRoot is { } xamlRoot)
            {
                xamlRoot.Changed += OnXamlRootChanged;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"XamlRoot hook failed: {ex.Message}");
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_applyingPlacement)
        {
            return;
        }

        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var scale = WindowPlacementService.GetScale(handle);
            if (Math.Abs(scale - _lastScale) < 0.01)
            {
                return;
            }

            _lastScale = scale;
            LogService.Info($"[window] DPI scale changed to {scale:0.##}");
            ScheduleVisibilityCheck("dpi-changed");
        }
        catch (Exception ex)
        {
            LogService.Warn($"DPI change handling failed: {ex.Message}");
        }
    }

    private void StartDisplayWatcher()
    {
        try
        {
            _displayWatcher = DisplayArea.CreateWatcher();
            _displayWatcher.Added += (_, _) => ScheduleVisibilityCheck("display-added");
            _displayWatcher.Removed += (_, _) => ScheduleVisibilityCheck("display-removed");
            _displayWatcher.Updated += (_, _) => ScheduleVisibilityCheck("display-updated");
            _displayWatcher.Start();
        }
        catch (Exception ex)
        {
            // В некоторых окружениях watcher недоступен — проверяем при следующем запуске.
            LogService.Warn($"DisplayArea watcher unavailable: {ex.Message}");
        }
    }

    private void ScheduleVisibilityCheck(string reason)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_applyingPlacement)
            {
                return;
            }

            _applyingPlacement = true;
            try
            {
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var result = WindowPlacementService.EnsureVisible(AppWindow, handle, _lastNormalRect);
                if (result.Clamped)
                {
                    _lastNormalRect = result.FinalRect;
                }

                LogService.Info($"[window] displays changed ({reason}): {result.Describe()}");
            }
            catch (Exception ex)
            {
                LogService.Warn($"Window visibility check failed: {ex.Message}");
            }
            finally
            {
                _applyingPlacement = false;
            }
        });
    }
}
