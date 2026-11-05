using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TempoKaraoke;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeToWorkArea();

        RootFrame.Navigated += (_, args) =>
        {
            if (args.Content is MainPage page)
            {
                SetTitleBar(page.HeaderDragArea);
            }
        };

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void ResizeToWorkArea()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var width = Math.Clamp(1810, 900, Math.Max(900, work.Width - 40));
        var height = Math.Clamp(1123, 640, Math.Max(640, work.Height - 40));
        AppWindow.Resize(new SizeInt32(width, height));
    }
}
