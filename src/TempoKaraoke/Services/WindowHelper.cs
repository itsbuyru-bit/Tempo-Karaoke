using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace TempoKaraoke.Services;

public static class WindowHelper
{
    public static void ToggleFullScreen(Window window)
    {
        var appWindow = window.AppWindow;
        if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }

    public static void ExitFullScreen(Window window)
    {
        if (window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            window.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
    }

    public static bool IsFullScreen(Window window) =>
        window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
}
