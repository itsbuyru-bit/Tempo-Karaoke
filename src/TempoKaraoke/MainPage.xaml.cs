using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TempoKaraoke.Models;
using TempoKaraoke.Services;
using Windows.Storage.Pickers;
using Windows.System;

namespace TempoKaraoke;

public sealed partial class MainPage : Page
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".wmv",
    };

    private readonly List<MediaItem> _all = new();
    private readonly PlayerService _player;
    private readonly DispatcherTimer _timer;
    private MediaItem? _current;
    private string[]? _mainSwapOptions;
    private PlayerWindow? _playerWindow;
    private bool _detached;
    private bool _isSeeking;
    private bool _shuffle;
    private bool _repeat;
    private bool _suppressVolume;
    private bool _isPopulatingFolders;
    private bool _isInitializingSettings;
    private bool _isRestoringQueue;
    private int _queueIndex = -1;
    private readonly Stack<string> _navHistory = new();
    private string? _currentTag;
    private bool _isNavigatingBack;

    public ObservableCollection<MediaItem> Filtered { get; } = new();

    public ObservableCollection<MediaItem> Queue { get; } = new();

    public ObservableCollection<MediaItem> Recent { get; } = new();

    public FrameworkElement HeaderDragArea => AppTitleBarDragRegion;

    public ObservableCollection<string> VideoRoots { get; } = new();

    public ObservableCollection<FolderInfo> FolderGroups { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        SortComboBox.SelectedIndex = 0;
        _player = new PlayerService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _player.StateChanged += (_, _) => UpdateTransport();
        _player.EndReached += (_, _) => PlayNext();
        _player.Failed += (_, message) => BarSubtitle.Text = message;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateTransport();

        VideoView.Initialized += OnVideoViewInitialized;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        InitializeSettings();
        RestoreQueue();
        Queue.CollectionChanged += (_, _) => SaveQueue();
        RestoreRecent();
        Recent.CollectionChanged += (_, _) => UpdateRecentEmpty();
        NavView.PaneOpened += (_, _) => UpdateHeader();
        NavView.PaneClosed += (_, _) => UpdateHeader();
        ShowLibrary();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag as string ?? "library";

        if (!_isNavigatingBack &&
            _currentTag is not null &&
            !string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            _navHistory.Push(_currentTag);
        }

        _currentTag = tag;
        UpdateHeader();

        switch (tag)
        {
            case "library":
                ShowLibrary();
                break;
            case "queue":
                ShowQueue();
                break;
            case "home":
                ShowHome();
                break;
            case "playlists":
                ShowPlaceholder("Плейлисты", "Именованные плейлисты и экспорт M3U — следующий этап.");
                break;
            case "settings":
                ShowSettings();
                break;
        }
    }

    private void HeaderMenu_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        UpdateHeader();
    }

    private void HeaderBack_Click(object sender, RoutedEventArgs e)
    {
        GoBack();
    }

    private void UpdateHeader()
    {
        if (AppHeader is null || HeaderIcon is null || HeaderTitle is null || HeaderBackButton is null)
        {
            return;
        }

        var open = NavView.IsPaneOpen;
        AppHeader.Width = open ? 320 : 48;
        HeaderIcon.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HeaderTitle.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HeaderBackButton.Visibility = open && _navHistory.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem firstItem)
        {
            firstItem.Margin = new Thickness(0, open ? 0 : 56, 0, 0);
        }
    }

    private void GoBack()
    {
        if (_navHistory.Count == 0)
        {
            return;
        }

        var tag = _navHistory.Pop();
        var item = ItemForTag(tag);
        if (item is not null)
        {
            _isNavigatingBack = true;
            NavView.SelectedItem = item;
            _isNavigatingBack = false;
        }

        _currentTag = tag;
        UpdateHeader();
    }

    private NavigationViewItem? ItemForTag(string tag)
    {
        return tag switch
        {
            "home" => HomeNavItem,
            "library" => LibraryNavItem,
            "queue" => QueueNavItem,
            "playlists" => PlaylistsNavItem,
            "settings" => SettingsNavItem,
            _ => null,
        };
    }

    private void ShowLibrary()
    {
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void ShowHome()
    {
        HomeView.Visibility = Visibility.Visible;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        UpdateRecentEmpty();
    }

    private void ShowQueue()
    {
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        QueueList.Focus(FocusState.Programmatic);
    }

    private void ShowPlaceholder(string title, string text)
    {
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = text;
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void ShowSettings()
    {
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
    }

    private void InitializeSettings()
    {
        _isInitializingSettings = true;
        RefreshVideoRoots();
        ThemeComboBox.SelectedIndex = SettingsService.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };
        AccentComboBox.SelectedIndex = 0;
        _isInitializingSettings = false;
        ApplyTheme(SettingsService.Theme);
    }

    private void ApplyTheme(string theme)
    {
        var elementTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        RootLayout.RequestedTheme = elementTheme;
        if (App.Window is not null && App.Window.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }
    }

    private void RefreshVideoRoots()
    {
        VideoRoots.Clear();
        foreach (var root in SettingsService.VideoRoots)
        {
            VideoRoots.Add(root);
        }
    }

    private async void AddVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var roots = SettingsService.VideoRoots;
        if (roots.Any(root => string.Equals(root, folder.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        roots.Add(folder.Path);
        SettingsService.SetVideoRoots(roots);
        RefreshVideoRoots();
        await LoadPreviewAsync();
    }

    private async void RemoveVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string root)
        {
            return;
        }

        var roots = SettingsService.VideoRoots;
        roots.RemoveAll(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase));
        if (roots.Count == 0)
        {
            roots.Add(SettingsService.DefaultVideoRoot);
        }

        SettingsService.SetVideoRoots(roots);
        RefreshVideoRoots();
        await LoadPreviewAsync();
    }

    private async void RefreshLibraries_Click(object sender, RoutedEventArgs e)
    {
        await LoadPreviewAsync();
    }

    private async void OpenIndexingSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:search-indexing"));
        }
        catch
        {
            // The Settings app may be unavailable in a restricted environment.
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var theme = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        SettingsService.Theme = theme;
        ApplyTheme(theme);
    }

    private void AccentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var accent = (AccentComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        SettingsService.Accent = accent;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        App.Window.SetTitleBar(AppTitleBarDragRegion);
        UpdateHeader();
        ApplyTheme(SettingsService.Theme);
        if (VolumeSlider is not null)
        {
            VolumeSlider.Value = _player.Volume;
        }
        if (VolumeValueText is not null)
        {
            VolumeValueText.Text = _player.Volume.ToString();
        }
        UpdateTransport();
        _ = LoadPreviewAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        VideoView.Initialized -= OnVideoViewInitialized;
        _playerWindow?.Close();
        _player.Detach();
    }

    private void OnVideoViewInitialized(object? sender, InitializedEventArgs e)
    {
        _mainSwapOptions = e.SwapChainOptions;
        _player.Attach(VideoView, e.SwapChainOptions);
        UpdateTransport();
    }

    private async Task LoadPreviewAsync()
    {
        var roots = SettingsService.VideoRoots;
        ScanButton.IsEnabled = false;
        StatusText.Text = "Читаю медиатеку…";

        try
        {
            var items = await Task.Run(() =>
            {
                var all = new List<MediaItem>();
                foreach (var root in roots)
                {
                    try
                    {
                        all.AddRange(EnumerateVideos(root));
                    }
                    catch
                    {
                        // A disconnected drive must not abort the whole scan.
                    }
                }

                return all
                    .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            });

            _all.Clear();
            _all.AddRange(items);
            PopulateFolders();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось прочитать библиотеку: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private static IEnumerable<MediaItem> EnumerateVideos(string root)
    {
        if (!Directory.Exists(root))
        {
            return Enumerable.Empty<MediaItem>();
        }

        return Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Select(MediaItem.FromPath);
    }

    private void PopulateFolders()
    {
        _isPopulatingFolders = true;

        FolderGroups.Clear();
        foreach (var group in _all
                     .GroupBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            FolderGroups.Add(new FolderInfo(group.Key, group.Count()));
        }

        FolderFilter.Items.Clear();
        FolderFilter.Items.Add("Все папки");
        foreach (var group in FolderGroups)
        {
            FolderFilter.Items.Add(group.Name);
        }

        FolderFilter.SelectedIndex = 0;
        _isPopulatingFolders = false;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var folder = FolderFilter.SelectedItem as string;

        IEnumerable<MediaItem> result = _all;
        if (!string.IsNullOrWhiteSpace(folder) && folder != "Все папки")
        {
            result = result.Where(item =>
                string.Equals(item.Folder, folder, StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = result.Where(item => terms.All(term =>
                item.DisplayTitle.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                item.Folder.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        }

        var sort = (SortComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "artist";
        result = sort switch
        {
            "az" => result
                .OrderBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
            "title" => result
                .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase),
            "folder" => result
                .OrderBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
            _ => result
                .OrderBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase),
        };

        Filtered.Clear();
        foreach (var item in result)
        {
            Filtered.Add(item);
        }

        CountText.Text = $"{Filtered.Count} песен";
        StatusText.Text = $"Показано {Filtered.Count} из {_all.Count}";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void FolderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingFolders)
        {
            return;
        }

        ApplyFilter();
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadPreviewAsync();
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (Filtered.Count == 0)
        {
            return;
        }

        Queue.Clear();
        _isRestoringQueue = true;
        foreach (var item in Filtered.ToList())
        {
            Queue.Add(item);
        }
        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(0);
    }

    private void FolderList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not FolderInfo folder)
        {
            return;
        }

        var match = FolderFilter.Items
            .OfType<string>()
            .FirstOrDefault(name => string.Equals(name, folder.Name, StringComparison.CurrentCultureIgnoreCase));
        if (match is not null)
        {
            FolderFilter.SelectedItem = match;
        }

        LibraryTabs.SelectedIndex = 0;
    }

    private void LibraryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is MediaItem item)
        {
            PlayNow(item);
        }
    }

    private void LibraryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && LibraryList.SelectedItem is MediaItem item)
        {
            PlayNow(item);
            e.Handled = true;
        }
    }

    private void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = QueueList.SelectedItem as MediaItem ?? SelectedQueueItems().FirstOrDefault();
        if (item is not null)
        {
            PlayQueueItem(item);
        }
    }

    private void QueueList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var item = QueueList.SelectedItem as MediaItem ?? SelectedQueueItems().FirstOrDefault();
        if (e.Key == VirtualKey.Enter && item is not null)
        {
            PlayQueueItem(item);
            e.Handled = true;
        }
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionBar();
    }

    private List<MediaItem> SelectedQueueItems()
    {
        return QueueList.SelectedItems.OfType<MediaItem>().ToList();
    }

    private void UpdateSelectionBar()
    {
        if (SelectionBar is null || SelectionText is null || QueueActionsBar is null)
        {
            return;
        }

        var count = QueueList?.SelectedItems.Count ?? 0;
        SelectionText.Text = count == 1 ? "Выбран 1 элемент" : $"Выбрано: {count}";
        SelectionBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueActionsBar.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        QueueList.SelectedItems.Clear();
        UpdateSelectionBar();
    }

    private void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItems().FirstOrDefault();
        if (item is not null)
        {
            PlayQueueItem(item);
        }
    }

    private void PlaySelectedNext_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQueueItems();
        if (selected.Count == 0)
        {
            return;
        }

        var insert = Math.Clamp(_queueIndex + 1, 0, Queue.Count);
        foreach (var item in selected)
        {
            Queue.Remove(item);
        }

        insert = Math.Min(insert, Queue.Count);
        foreach (var item in selected)
        {
            Queue.Insert(insert++, item);
        }

        _queueIndex = _current is null ? -1 : Queue.IndexOf(_current);
        UpdateQueueCount();
        UpdateSelectionBar();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedQueueItems();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            Queue.Remove(item);
        }

        _queueIndex = _current is null ? -1 : Queue.IndexOf(_current);
        UpdateQueueCount();
        UpdateSelectionBar();
    }

    private void MoveSelectedUp_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItems().FirstOrDefault();
        if (item is null)
        {
            return;
        }

        var index = Queue.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        Queue.Move(index, index - 1);
        _queueIndex = _current is null ? -1 : Queue.IndexOf(_current);
        UpdateSelectionBar();
    }

    private void MoveSelectedDown_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItems().FirstOrDefault();
        if (item is null)
        {
            return;
        }

        var index = Queue.IndexOf(item);
        if (index < 0 || index >= Queue.Count - 1)
        {
            return;
        }

        Queue.Move(index, index + 1);
        _queueIndex = _current is null ? -1 : Queue.IndexOf(_current);
        UpdateSelectionBar();
    }

    private async void SelectedProperties_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedQueueItems().FirstOrDefault();
        if (item is null)
        {
            return;
        }

        await ShowPropertiesAsync(item);
    }

    private async Task ShowPropertiesAsync(MediaItem item)
    {
        var panel = new StackPanel
        {
            MinWidth = 760,
            Spacing = 16,
        };

        var grid = new Grid
        {
            ColumnSpacing = 32,
            RowSpacing = 12,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        void AddCell(string label, string value, int column)
        {
            var cell = new StackPanel { Spacing = 2 };
            cell.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)),
            });
            cell.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        void AddPair(string leftLabel, string leftValue, string rightLabel, string rightValue)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(leftLabel, leftValue, 0);
            AddCell(rightLabel, rightValue, 1);
            row++;
        }

        AddPair("Название", item.DisplayTitle, "Подзаголовок", string.Empty);
        AddPair("Дополнительные исполнители", item.Artist, "Длительность", item.DurationText);
        AddPair("Жанр", string.Empty, "Год", string.Empty);
        AddPair("Разрешение", string.Empty, "Частота кадров", string.Empty);
        AddPair("Звуковые каналы", string.Empty, "Тип элемента", "." + item.Format.ToLowerInvariant());
        panel.Children.Add(grid);

        var pathPanel = new StackPanel { Spacing = 4 };
        pathPanel.Children.Add(new TextBlock
        {
            Text = "Расположение файла",
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)),
        });
        pathPanel.Children.Add(new TextBlock
        {
            Text = item.Path,
            FontSize = 14,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(pathPanel);

        var openFolder = new HyperlinkButton { Content = "Открыть папку с файлом" };
        openFolder.Click += (_, _) => OpenContainingFolder(item.Path);
        panel.Children.Add(openFolder);

        var dialog = new ContentDialog
        {
            Title = "Свойства",
            Content = panel,
            CloseButtonText = "Закрыть",
            MinWidth = 820,
            MaxWidth = 900,
            XamlRoot = XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private static void OpenContainingFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "explorer.exe",
                $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Explorer may be unavailable in a restricted environment.
        }
    }

    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            AddToQueue(item);
        }
    }

    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MediaItem item)
        {
            return;
        }

        var index = Queue.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Queue.RemoveAt(index);
        if (index < _queueIndex)
        {
            _queueIndex--;
        }
        else if (index == _queueIndex)
        {
            _queueIndex = Math.Min(_queueIndex, Queue.Count - 1);
        }

        UpdateQueueCount();
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        Queue.Clear();
        _queueIndex = -1;
        UpdateQueueCount();
    }

    private async void AddFilesSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await PickFilesToQueueAsync();
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        await PickFilesToQueueAsync();
    }

    private async Task PickFilesToQueueAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".webm");
        picker.FileTypeFilter.Add(".m4v");
        picker.FileTypeFilter.Add(".wmv");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            AddToQueue(MediaItem.FromPath(file.Path));
        }
    }

    private async void AddFolderToQueue_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        var items = await Task.Run(() => EnumerateVideos(folder.Path)
            .OrderBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList());

        if (items.Count == 0)
        {
            BarSubtitle.Text = "В папке нет видеофайлов";
            return;
        }

        foreach (var item in items)
        {
            AddToQueue(item);
        }
    }

    private async void AddUrlToQueue_Click(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "https://example.com/video.mkv",
            Width = 420,
        };

        var dialog = new ContentDialog
        {
            Title = "Добавить по URL",
            Content = input,
            PrimaryButtonText = "Добавить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var url = input.Text.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == "rtsp"))
        {
            AddToQueue(MediaItem.FromUrl(url));
        }
        else
        {
            BarSubtitle.Text = "Некорректный URL";
        }
    }

    private void AddToQueue(MediaItem item)
    {
        Queue.Add(item);
        UpdateQueueCount();
        _ = LoadDurationAsync(item);
        if (QueueView.Visibility == Visibility.Visible)
        {
            QueueList.ScrollIntoView(item);
        }
    }

    private static async Task LoadDurationAsync(MediaItem item)
    {
        if (IsRemote(item.Path))
        {
            return;
        }

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            item.SetDuration((long)properties.Duration.TotalMilliseconds);
        }
        catch
        {
            // A missing file or unsupported metadata must not break the queue.
        }
    }

    private void RestoreQueue()
    {
        _isRestoringQueue = true;
        foreach (var path in SettingsService.QueuePaths)
        {
            var item = IsRemote(path) ? MediaItem.FromUrl(path) : MediaItem.FromPath(path);
            Queue.Add(item);
            _ = LoadDurationAsync(item);
        }
        _isRestoringQueue = false;
        UpdateQueueCount();
    }

    private void SaveQueue()
    {
        if (_isRestoringQueue)
        {
            return;
        }

        SettingsService.QueuePaths = Queue.Select(item => item.Path).ToList();
    }

    private void RestoreRecent()
    {
        foreach (var path in SettingsService.RecentPaths)
        {
            Recent.Add(IsRemote(path) ? MediaItem.FromUrl(path) : MediaItem.FromPath(path));
        }

        UpdateRecentEmpty();
    }

    private void RecordRecent(MediaItem item)
    {
        var existing = Recent.FirstOrDefault(
            recent => string.Equals(recent.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        Recent.Insert(0, item);
        while (Recent.Count > 12)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        SettingsService.RecentPaths = Recent.Select(recent => recent.Path).ToList();
        UpdateRecentEmpty();
    }

    private void UpdateRecentEmpty()
    {
        if (RecentEmptyText is null)
        {
            return;
        }

        RecentEmptyText.Visibility = Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecentList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RecentList.SelectedItem is MediaItem item)
        {
            PlayNow(item);
        }
    }

    private void RecentList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && RecentList.SelectedItem is MediaItem item)
        {
            PlayNow(item);
            e.Handled = true;
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = LibraryNavItem;
    }

    private void OpenQueue_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = QueueNavItem;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = SettingsNavItem;
    }

    private static bool IsRemote(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == "rtsp");
    }

    private void PlayNow(MediaItem item)
    {
        _current = item;
        _queueIndex = Queue.IndexOf(item);
        RecordRecent(item);

        if (_detached && _playerWindow is not null)
        {
            _playerWindow.Play(item.Path);
        }
        else
        {
            _player.Play(item.Path);
        }

        UpdateNowPlaying();
    }

    private void PlayQueueItem(MediaItem item)
    {
        var index = Queue.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _queueIndex = index;
        _current = item;
        RecordRecent(item);

        if (_detached && _playerWindow is not null)
        {
            _playerWindow.Play(item.Path);
        }
        else
        {
            _player.Play(item.Path);
        }

        UpdateNowPlaying();
    }

    private void PlayQueueIndex(int index)
    {
        if (index < 0 || index >= Queue.Count)
        {
            return;
        }

        PlayQueueItem(Queue[index]);
    }

    private void PlayNext()
    {
        if (Queue.Count > 0)
        {
            if (_shuffle)
            {
                var next = Queue.Count == 1 ? 0 : Random.Shared.Next(Queue.Count - 1);
                if (_queueIndex >= 0 && next >= _queueIndex)
                {
                    next++;
                }

                PlayQueueIndex(next);
                return;
            }

            var nextIndex = _queueIndex < 0 ? 0 : _queueIndex + 1;
            if (nextIndex < Queue.Count)
            {
                PlayQueueIndex(nextIndex);
            }
            else if (_repeat)
            {
                PlayQueueIndex(0);
            }
            else
            {
                _player.Stop();
            }

            return;
        }

        if (Filtered.Count == 0)
        {
            return;
        }

        var index = _current is null ? -1 : Filtered.IndexOf(_current);
        var nextInList = index < 0 ? 0 : index + 1;
        if (nextInList < Filtered.Count)
        {
            PlayNow(Filtered[nextInList]);
        }
    }

    private void PlayPrevious()
    {
        if (_player.PositionMs > 3000)
        {
            _player.Seek(0);
            return;
        }

        if (Queue.Count > 0 && _queueIndex > 0)
        {
            PlayQueueIndex(_queueIndex - 1);
            return;
        }

        if (Queue.Count > 0 && _queueIndex < 0)
        {
            PlayQueueIndex(0);
            return;
        }

        if (Filtered.Count == 0)
        {
            return;
        }

        var index = _current is null ? -1 : Filtered.IndexOf(_current);
        if (index > 0)
        {
            PlayNow(Filtered[index - 1]);
        }
        else
        {
            _player.Seek(0);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _player.PlayPause();
        UpdateTransport();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        UpdateTransport();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        PlayPrevious();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        PlayNext();
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        _shuffle = ShuffleButton.IsChecked == true;
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeat = RepeatButton.IsChecked == true;
    }

    private async void MediaInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            BarSubtitle.Text = "Нет активного медиа";
            return;
        }

        var text =
            $"Название: {_current.DisplayTitle}\n" +
            $"Исполнитель: {_current.Artist}\n" +
            $"Папка: {_current.Folder}\n" +
            $"Формат: {_current.Format}\n" +
            $"Позиция: {FormatTime(_player.PositionMs)} / {FormatTime(_player.LengthMs)}\n\n" +
            _current.Path;

        var content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            MaxWidth = 560,
        };

        var dialog = new ContentDialog
        {
            Title = "Информация о медиа",
            Content = content,
            CloseButtonText = "Закрыть",
            XamlRoot = XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private void SkipBack_Click(object sender, RoutedEventArgs e)
    {
        _player.Seek(_player.PositionMs - 10000);
    }

    private void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        _player.Seek(_player.PositionMs + 30000);
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
        _player.Seek((long)SeekSlider.Value);
        UpdateTransport();
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
        _player.Seek((long)SeekSlider.Value);
        UpdateTransport();
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isSeeking)
        {
            CurrentTimeText.Text = FormatTime((long)e.NewValue);
            TotalTimeText.Text = FormatTime(_player.LengthMs);
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var volume = (int)e.NewValue;
        if (VolumeValueText is not null)
        {
            VolumeValueText.Text = volume.ToString();
        }

        if (_suppressVolume)
        {
            return;
        }

        _player.SetVolume(volume);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _player.ToggleMute();
        _suppressVolume = true;
        if (VolumeSlider is not null)
        {
            VolumeSlider.Value = _player.Volume;
        }
        _suppressVolume = false;
        if (VolumeValueText is not null)
        {
            VolumeValueText.Text = _player.Volume.ToString();
        }
        UpdateTransport();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.ToggleFullScreen(App.Window);
    }

    private void FullScreenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WindowHelper.ToggleFullScreen(App.Window);
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (SearchBox.FocusState != FocusState.Unfocused && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchBox.Text = string.Empty;
            ApplyFilter();
            return;
        }

        WindowHelper.ExitFullScreen(App.Window);
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (focused is TextBox or AutoSuggestBox or RichEditBox)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Space:
                _player.PlayPause();
                e.Handled = true;
                break;
            case VirtualKey.Left when focused is not ListView and not ListViewItem:
                _player.Seek(_player.PositionMs - 5000);
                e.Handled = true;
                break;
            case VirtualKey.Right when focused is not ListView and not ListViewItem:
                _player.Seek(_player.PositionMs + 5000);
                e.Handled = true;
                break;
            case VirtualKey.Up when focused is not ListView and not ListViewItem:
                _player.SetVolume(_player.Volume + 5);
                e.Handled = true;
                break;
            case VirtualKey.Down when focused is not ListView and not ListViewItem:
                _player.SetVolume(_player.Volume - 5);
                e.Handled = true;
                break;
        }
    }

    private void Detach_Click(object sender, RoutedEventArgs e)
    {
        DetachVideo(false);
    }

    private void MiniPlayer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        DetachVideo(true);
    }

    private void MiniPlayer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        MiniPlayerCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(34, 255, 255, 255));
    }

    private void MiniPlayer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        MiniPlayerCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    private void DetachVideo(bool maximize)
    {
        if (_detached || _current is null)
        {
            return;
        }

        var position = _player.PositionMs;
        var wasPlaying = _player.IsPlaying;
        var volume = _player.Volume;

        _player.Pause();
        _detached = true;

        TransportBar.Visibility = Visibility.Collapsed;
        DetachedBar.Visibility = Visibility.Visible;
        DetachedTitle.Text = _current.DisplayTitle;

        _playerWindow = new PlayerWindow(
            _current.Path,
            position,
            wasPlaying,
            volume,
            PlayNext,
            PlayPrevious,
            ReturnFromDetached);
        _playerWindow.Activate();
        if (maximize)
        {
            _playerWindow.Maximize();
        }
        UpdateTransport();
    }

    private void ReturnVideo_Click(object sender, RoutedEventArgs e)
    {
        ReturnFromDetached();
    }

    private void ReturnFromDetached()
    {
        if (!_detached)
        {
            return;
        }

        var window = _playerWindow;
        _playerWindow = null;
        _detached = false;

        var position = window?.PositionMs ?? _player.PositionMs;
        var wasPlaying = window?.IsPlaying ?? false;

        TransportBar.Visibility = Visibility.Visible;
        DetachedBar.Visibility = Visibility.Collapsed;

        if (_current is not null)
        {
            if (string.Equals(_player.CurrentPath, _current.Path, StringComparison.OrdinalIgnoreCase))
            {
                // Same media: just seek/resume the paused main player.
                _player.Seek(position);
                if (wasPlaying && !_player.IsPlaying)
                {
                    _player.PlayPause();
                }
            }
            else
            {
                _player.PlayAt(_current.Path, position, wasPlaying);
            }
        }

        window?.CloseForReturn();
        UpdateTransport();
    }

    private void UpdateNowPlaying()
    {
        if (DetachedTitle is not null && _current is not null)
        {
            DetachedTitle.Text = _current.DisplayTitle;
        }

        if (_current is null)
        {
            BarTitle.Text = "Ничего не играет";
            BarSubtitle.Text = "Выберите песню двойным кликом";
            DetachButton.IsEnabled = false;
            return;
        }

        BarTitle.Text = _current.DisplayTitle;
        BarSubtitle.Text = _current.Subtitle;
        DetachButton.IsEnabled = true;
    }

    private void UpdateQueueCount()
    {
        QueueCountText.Text = $"{Queue.Count} песен";
    }

    private void UpdateTransport()
    {
        var length = Math.Max(1, _player.LengthMs);
        SeekSlider.Maximum = length;

        if (!_isSeeking)
        {
            SeekSlider.Value = Math.Clamp(_player.PositionMs, 0, length);
        }

        CurrentTimeText.Text = FormatTime(_player.PositionMs);
        TotalTimeText.Text = FormatTime(_player.LengthMs);
        PlayPauseIcon.Symbol = _player.IsPlaying ? Symbol.Pause : Symbol.Play;
        var muted = _player.Volume == 0;
        MuteIcon.Symbol = muted ? Symbol.Mute : Symbol.Volume;
        if (PopupMuteIcon is not null)
        {
            PopupMuteIcon.Symbol = muted ? Symbol.Mute : Symbol.Volume;
        }
        if (VolumeValueText is not null)
        {
            VolumeValueText.Text = _player.Volume.ToString();
        }

        if (!_suppressVolume &&
            VolumeSlider is not null &&
            Math.Abs(VolumeSlider.Value - _player.Volume) > 0.5)
        {
            _suppressVolume = true;
            VolumeSlider.Value = _player.Volume;
            _suppressVolume = false;
        }
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var span = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
