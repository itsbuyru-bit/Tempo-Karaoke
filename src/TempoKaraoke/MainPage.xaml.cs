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
using TempoKaraoke.Models;
using TempoKaraoke.Services;
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
    private bool _suppressVolume;
    private bool _isPopulatingFolders;
    private int _queueIndex = -1;

    public ObservableCollection<MediaItem> Filtered { get; } = new();

    public ObservableCollection<MediaItem> Queue { get; } = new();

    public MainPage()
    {
        InitializeComponent();

        _player = new PlayerService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _player.StateChanged += (_, _) => UpdateTransport();
        _player.EndReached += (_, _) => PlayNext();
        _player.Failed += (_, message) => NowPlayingSubtitle.Text = message;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateTransport();

        VideoView.Initialized += OnVideoViewInitialized;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ShowLibrary();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.SelectedItemContainer?.Tag as string ?? "library";
        switch (tag)
        {
            case "library":
                ShowLibrary();
                break;
            case "queue":
                ShowLibrary();
                QueueList.Focus(FocusState.Programmatic);
                break;
            case "home":
                ShowPlaceholder("Главная", "Здесь появятся недавние файлы и быстрый доступ к медиатеке.");
                break;
            case "playlists":
                ShowPlaceholder("Плейлисты", "Именованные плейлисты и экспорт M3U — следующий этап.");
                break;
            case "settings":
                ShowPlaceholder("Параметры", "Пути к библиотеке, тема и обновление индекса — следующий этап.");
                break;
        }
    }

    private void ShowLibrary()
    {
        LibraryPanel.Visibility = Visibility.Visible;
        PlayerPanel.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
    }

    private void ShowPlaceholder(string title, string text)
    {
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = text;
        LibraryPanel.Visibility = Visibility.Collapsed;
        PlayerPanel.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        VolumeSlider.Value = _player.Volume;
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
        var root = Environment.GetEnvironmentVariable("TEMPO_LIBRARY") ?? @"G:\Karaoke";
        ScanButton.IsEnabled = false;
        StatusText.Text = "Читаю медиатеку…";

        try
        {
            var items = await Task.Run(() => EnumerateVideos(root));
            _all.Clear();
            _all.AddRange(items);
            PopulateFolders();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось прочитать {root}: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private static List<MediaItem> EnumerateVideos(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .Take(500)
            .Select(MediaItem.FromPath)
            .OrderBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void PopulateFolders()
    {
        _isPopulatingFolders = true;
        FolderFilter.Items.Clear();
        FolderFilter.Items.Add("Все папки");

        foreach (var folder in _all
                     .Select(item => item.Folder)
                     .Where(folder => !string.IsNullOrWhiteSpace(folder))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase))
        {
            FolderFilter.Items.Add(folder);
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
        if (QueueList.SelectedItem is MediaItem item)
        {
            PlayQueueItem(item);
        }
    }

    private void QueueList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && QueueList.SelectedItem is MediaItem item)
        {
            PlayQueueItem(item);
            e.Handled = true;
        }
    }

    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            Queue.Add(item);
            UpdateQueueCount();
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

    private void PlayNow(MediaItem item)
    {
        _current = item;
        _queueIndex = Queue.IndexOf(item);

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
            var next = _queueIndex < 0 ? 0 : _queueIndex + 1;
            if (next < Queue.Count)
            {
                PlayQueueIndex(next);
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
            TimeText.Text = $"{FormatTime((long)e.NewValue)} / {FormatTime(_player.LengthMs)}";
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolume)
        {
            return;
        }

        _player.SetVolume((int)e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _player.ToggleMute();
        _suppressVolume = true;
        VolumeSlider.Value = _player.Volume;
        _suppressVolume = false;
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
        if (_detached || _current is null)
        {
            return;
        }

        var position = _player.PositionMs;
        var wasPlaying = _player.IsPlaying;
        var volume = _player.Volume;

        _player.Pause();
        _detached = true;

        PlayerColumn.Width = new GridLength(0);
        PlayerPanel.Visibility = Visibility.Collapsed;
        TransportBar.Visibility = Visibility.Collapsed;
        DetachedBar.Visibility = Visibility.Visible;

        _playerWindow = new PlayerWindow(
            _current.Path,
            position,
            wasPlaying,
            volume,
            PlayNext,
            PlayPrevious,
            ReturnFromDetached);
        _playerWindow.Activate();
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

        PlayerPanel.Visibility = Visibility.Visible;
        PlayerColumn.Width = new GridLength(2, GridUnitType.Star);
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
        if (_current is null)
        {
            NowPlayingTitle.Text = "Ничего не играет";
            NowPlayingSubtitle.Text = "Выберите песню двойным кликом";
            BarTitle.Text = "Ничего не играет";
            BarSubtitle.Text = "Выберите песню двойным кликом";
            DetachButton.IsEnabled = false;
            return;
        }

        NowPlayingTitle.Text = _current.DisplayTitle;
        NowPlayingSubtitle.Text = _current.Subtitle;
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

        TimeText.Text = $"{FormatTime(_player.PositionMs)} / {FormatTime(_player.LengthMs)}";
        PlayPauseIcon.Symbol = _player.IsPlaying ? Symbol.Pause : Symbol.Play;
        MuteIcon.Symbol = _player.Volume == 0 ? Symbol.Mute : Symbol.Volume;

        if (!_suppressVolume && Math.Abs(VolumeSlider.Value - _player.Volume) > 0.5)
        {
            _suppressVolume = true;
            VolumeSlider.Value = _player.Volume;
            _suppressVolume = false;
        }
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "00:00";
        }

        var span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
