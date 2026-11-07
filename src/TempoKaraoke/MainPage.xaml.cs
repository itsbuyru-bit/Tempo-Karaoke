using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using TempoKaraoke.Diagnostics;
using TempoKaraoke.Models;
using TempoKaraoke.Networking;
using TempoKaraoke.Security;
using TempoKaraoke.Core.Services;
using TempoKaraoke.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Networking.Connectivity;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace TempoKaraoke;

public sealed partial class MainPage : Page, IRemoteHost, INotifyPropertyChanged
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".webm", ".m4v", ".wmv",
    };

    private readonly List<MediaItem> _all = new();
    private readonly Dictionary<string, MediaItem> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaGroup> _visibleGroupsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _libraryLoadLock = new(1, 1);
    private readonly SemaphoreSlim _dialogLock = new(1, 1);
    private CancellationTokenSource? _scanCts;
    private readonly MediaCache _mediaCache = new();
    private readonly ConcurrentDictionary<string, CachedMedia> _mediaCacheEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _libraryWatchers = new();
    private readonly HashSet<string> _pendingLibraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pendingLibrarySync = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _libraryWatchTimer;
    private QueueUndoSnapshot? _queueUndo;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _undoTimer;

    private sealed record QueueUndoEntry(
        MediaItem Item,
        int Index,
        string? RequesterId,
        string? RequesterName);

    private sealed record QueueUndoSnapshot(
        IReadOnlyList<QueueUndoEntry> Entries,
        int CurrentIndex,
        string MessageKey);
    private readonly ConcurrentQueue<(string Path, CachedMedia Media, long SeenUtc)> _pendingCacheWrites = new();
    private int _pendingCacheCount;
    private readonly object _cacheFlushLock = new();
    private readonly SearchSession _searchSession = new();
    private bool _durationScanStarted;
    private bool _scanRunning;
    private string? _resumePath;
    private long _resumePositionMs;
    private DateTime _lastPositionSave = DateTime.MinValue;
    private bool _updatingSelectAll;
    private bool _updatingLibrarySelectAll;
    private bool _playPauseHovered;
    private readonly PlayerService _player;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _remoteIdleTimer;
    private int _countdownRemaining;
    private MediaItem? _current;
    private string[]? _mainSwapOptions;
    private PlayerWindow? _playerWindow;
    private bool _detached;
    private bool _isSeeking;
    private readonly HashSet<string> _thumbnailLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _metadataLoading = new(StringComparer.OrdinalIgnoreCase);
    private bool _shuffle;
    private RepeatMode _repeatMode = RepeatMode.None;
    private bool _repeat => _repeatMode != RepeatMode.None;

    private enum RepeatMode
    {
        None,
        All,
        One,
    }
    private bool _suppressVolume;
    private bool _isPopulatingFolders;
    private bool _isInitializingSettings;
    private bool _isRestoringQueue;
    private bool _playlistSelectionMode;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _copyIconTimer;
    private string? _folderDetailPath;
    private int _folderSortMode;
    private readonly Dictionary<string, bool> _folderExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressLibrarySelection;
    private int _libraryTabIndex;
    private int _homeSection;
    private Storyboard? _libraryStoryboard;
    private string _quickSearchQuery = string.Empty;
    private readonly Dictionary<string, int> _versionCountByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _populatingQuickSearchFilters;
    private readonly List<MediaItem> _pendingPlaylistItems = new();
    private int _queueIndex = -1;
    private readonly Dictionary<MediaItem, string?> _queueRequesters = new();
    private readonly List<RemoteGuestRequest> _guestRequests = new();
    private readonly List<RemoteArchivedRequest> _guestRequestArchive = new();
    private readonly object _guestRequestSync = new();
    private readonly Dictionary<MediaItem, string?> _queueRequesterNames = new();
    private string _sortMode = "az";
    private ObservableCollection<MediaGroup> _filteredGroups = new();

    public ObservableCollection<MediaGroup> FilteredGroups => _filteredGroups;

    public ObservableCollection<MediaGroup> SearchResults { get; } = new();

    private readonly QueueEngine _queue = new();
    public ObservableCollection<MediaItem> Queue => _queue.Items;

    public ObservableCollection<MediaGroup> Recent { get; } = new();

    public ObservableCollection<Playlist> Playlists { get; } = new();

    public ObservableCollection<MediaItem> PlaylistItems { get; } = new();

    public ObservableCollection<RemoteClientRow> RemoteClients { get; } = new();

    private Playlist? _openPlaylist;
    private readonly RemoteClientDirectory _remoteClientDirectory = new();
    private RemoteServer? _remoteServer;
    private readonly DispatcherTimer _remoteClientsTimer;
    private readonly SemaphoreSlim _remoteStopLock = new(1, 1);
    private string _hotspotSsid = "TempoKaraoke";
    private string _hotspotPassword = string.Empty;
    private bool _remoteDirectMode;
    private bool _isCheckingUpdates;
    private UpdateInfo? _pendingUpdate;
    private UpdateUiState _updateState = UpdateUiState.Idle;
    private double _updateDownloadProgress;

    /// <summary>Состояние карточки «Версия приложения» (`Settings > Updates`).</summary>
    private enum UpdateUiState
    {
        /// <summary>Нейтральная подпись текущей сборки до первой проверки.</summary>
        Idle,

        /// <summary>Идёт запрос к GitHub.</summary>
        Checking,

        UpToDate,

        UpdateAvailable,

        Downloading,

        Installing,

        InstallFailed,

        /// <summary>Не удалось проверить обновления (сеть, сервер).</summary>
        Failed,

        Dismissed,

        NotConfigured,
    }

    /// <summary>
    /// Идёт проверка или установка обновления. Используется в x:Bind для
    /// <c>ProgressRing</c> и блокировки кнопок.
    /// </summary>
    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            if (_isCheckingUpdates == value)
            {
                return;
            }

            _isCheckingUpdates = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCheckingUpdates)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FrameworkElement HeaderDragArea => AppTitleBarDragRegion;

    public ObservableCollection<string> VideoRoots { get; } = new();

    public ObservableCollection<FolderInfo> FolderGroups { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        LocalizationService.Apply(RootLayout, SettingsService.Language);
        Loaded += (_, _) => LocalizationService.Apply(RootLayout, SettingsService.Language);
        LibraryList.ItemTemplate = (DataTemplate)Resources["MediaGroupCardTemplate"];

        _suppressLibrarySelection = true;
        LibrarySelector.SelectedItem = AllVideosTab;
        _suppressLibrarySelection = false;

        StartupTrace.Mark("MainPage ctor");

        _player = new PlayerService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _player.StateChanged += (_, _) =>
        {
            UpdateTransport();
            UpdateNowPlaying();
        };
        _player.EndReached += (_, _) => OnPlaybackEnded();
        _player.Failed += (_, message) => BarSubtitle.Text = message;

        // «Применить / Откатить» останавливает воспроизведение перед миграцией
        // и перезагружает плейлисты/очередь после переписывания путей.

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateTransport();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();

        _remoteIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _remoteIdleTimer.Tick += (_, _) => _ = CheckRemoteIdleAsync();

        _remoteClientsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _remoteClientsTimer.Tick += (_, _) => RefreshRemoteClients();

        VideoView.Initialized += OnVideoViewInitialized;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        SettingsService.UpdateStateChanged += OnUpdateStateChanged;
        App.UpdateActionRequested += OnUpdateActionRequested;
        MediaItem.TextResolver = LocalizationService.Get;
        InitializeSettings();
        NavView.PaneOpened += (_, _) =>
        {
            UpdateHeader();
            UpdateBadgeVisibility();
        };
        NavView.PaneClosed += (_, _) =>
        {
            UpdateHeader();
            UpdateBadgeVisibility();
        };
        ShowLibrary();
        StartupTrace.Mark("MainPage ctor done");
    }

    /// <summary>
    /// Всё, что читает диск и сеть, выполняется после первого кадра: пользователь
    /// уже видит интерфейс, а не чёрное окно. Ошибки не должны ломать запуск.
    /// </summary>
    private void InitializeAfterFirstRender()
    {
        StartupTrace.Mark("Deferred init start");

        try
        {
            var loaded = PlaylistService.Load();
            foreach (var playlist in loaded)
            {
                if (playlist.Name.Contains("плейлись", StringComparison.CurrentCultureIgnoreCase))
                {
                    playlist.Name = playlist.Name.Replace(
                        "плейлись",
                        "плейлист",
                        StringComparison.CurrentCultureIgnoreCase);
                }

                Playlists.Add(playlist);
            }

            PlaylistService.Save(Playlists);
            Playlists.CollectionChanged += Playlists_CollectionChanged;
            RebuildPlaylistsNav();
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось загрузить плейлисты при старте.");
        }

        try
        {
            RestoreQueue();
            Queue.CollectionChanged += (_, _) => SaveQueue();
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось восстановить очередь при старте.");
        }

        try
        {
            RestoreRecent();
            Recent.CollectionChanged += (_, _) => UpdateRecentEmpty();
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось восстановить недавние при старте.");
        }

        try
        {
            RefreshRemoteClients();
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось обновить список устройств пульта.");
        }

        StartupTrace.Mark("Deferred init done");
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        PlaylistDetailView.Visibility = Visibility.Collapsed;
        RemoteView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;

        if (args.SelectedItemContainer is null ||
            ReferenceEquals(args.SelectedItemContainer, SearchResultsNavItem))
        {
            return;
        }

        var tag = args.SelectedItemContainer.Tag as string ?? "library";

        var selectedPlaylist = args.SelectedItem as Playlist
            ?? Playlists.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, tag, StringComparison.Ordinal));
        if (selectedPlaylist is not null)
        {
            PlaylistsNavItem.IsExpanded = true;
            OpenPlaylist(selectedPlaylist, syncNavigation: false);
            GlobalSearchButton.Visibility = Visibility.Visible;
            LocalizationService.Apply(RootLayout, SettingsService.Language);
            return;
        }

        // Top-level navigation always leaves the About page behind.
        AboutView.Visibility = Visibility.Collapsed;

        // Верхнеуровневые разделы NavigationView не копим в историю:
        // стрелка «Назад» нужна только для поиска/деталей, а не для переключения меню.
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
                ShowPlaylists();
                break;
            case "remote":
                ShowRemote();
                break;
            case "remote-participants":
                ShowRemoteParticipants();
                break;
            case "settings":
                ShowSettings();
                break;
            case "about":
                ShowAbout();
                break;
        }

        GlobalSearchButton.Visibility = tag is "home" or "library" or "queue" or "playlists"
            ? Visibility.Visible
            : Visibility.Collapsed;
        LocalizationService.Apply(RootLayout, SettingsService.Language);
    }

    private void UpdateGlobalSearchVisibility()
    {
        if (GlobalSearchButton is null)
        {
            return;
        }

        var tag = (NavView.SelectedItem as NavigationViewItem)?.Tag as string;
        GlobalSearchButton.Visibility = tag is "home" or "library" or "queue" or "playlists"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HeaderMenu_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
        UpdateHeader();
    }

    private void HeaderBack_Click(object sender, RoutedEventArgs e)
    {
        if (SearchResultsView is not null && SearchResultsView.Visibility == Visibility.Visible)
        {
            if (NavView.SelectedItem is null)
            {
                NavView.SelectedItem = LibraryNavItem;
            }

            ShowLibrary();
            return;
        }
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
        HeaderBackButton.Visibility = open &&
            SearchResultsView is not null &&
            SearchResultsView.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem firstItem)
        {
            firstItem.Margin = new Thickness(0, open ? 0 : 48, 0, 0);
        }
    }

    private int _navigationEpoch;
    private bool _pinStatusIsWarning;

    /// <summary>
    /// Single entry point for top-level navigation. Every other view is fully
    /// collapsed before the new one becomes visible, so a nested playlist can no
    /// longer be drawn on top of the menu the user came from.
    /// </summary>
    private void ShowTopLevelView(FrameworkElement target, bool keepLibraryDetail = false)
    {
        _navigationEpoch++;

        foreach (var view in new FrameworkElement[]
                 {
                     HomeView,
                     LibraryPanel,
                     QueueView,
                     PlaceholderView,
                     SettingsView,
                     PlaylistsView,
                     PlaylistDetailView,
                     SearchResultsView,
                     RemoteView,
                     RemoteParticipantsView,
                     AboutView,
                 })
        {
            if (!ReferenceEquals(view, target))
            {
                view.Visibility = Visibility.Collapsed;
            }
        }

        if (!keepLibraryDetail && FolderDetailView is not null)
        {
            FolderDetailView.Visibility = Visibility.Collapsed;
        }

        target.Visibility = Visibility.Visible;
    }

    private void ShowLibrary()
    {
        ShowTopLevelView(LibraryPanel);
        if (NavView is not null &&
            (NavView.SelectedItem is null || ReferenceEquals(NavView.SelectedItem, SearchResultsNavItem)) &&
            LibraryNavItem is not null)
        {
            NavView.SelectedItem = LibraryNavItem;
        }

        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        ApplyLanguageDeferred(LibraryPanel);
    }

    private void ShowSearchResults(string query)
    {
        ShowTopLevelView(SearchResultsView);
        var matches = _searchSession.Search(query, limit: 0).Items;
        var groups = MediaGroupingService.Group(matches);

        SearchResults.Clear();
        _visibleGroupsByPath.Clear();
        foreach (var group in groups)
        {
            foreach (var version in group.Versions)
            {
                _visibleGroupsByPath[version.Path] = group;
            }

            ApplyCachedMetadata(group);
            SearchResults.Add(group);
        }

        SearchResultsTitle.Text = LocalizationService.Format("Search_ResultsFor", query);

        PlaylistDetailView.Visibility = Visibility.Collapsed;
        RemoteView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Visible;
        NavView.SelectedItem = SearchResultsNavItem;
        UpdateSearchResultsEmptyState();
        UpdateHeader();
    }

    private void UpdateSearchResultsEmptyState()
    {
        var empty = SearchResults.Count == 0;
        SearchResultsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        SearchResultsEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LibrarySelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressLibrarySelection || AllVideosPanel is null || FoldersPanel is null)
        {
            return;
        }

        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index < 0 || index == _libraryTabIndex)
        {
            return;
        }

        var direction = index > _libraryTabIndex ? 1 : -1;
        _libraryTabIndex = index;
        ShowLibrarySection(index, direction);
    }

    private void ShowLibrarySection(int index, int direction)
    {
        var incoming = index == 0 ? AllVideosPanel : FoldersPanel;
        var outgoing = index == 0 ? FoldersPanel : AllVideosPanel;
        var incomingTransform = index == 0 ? AllVideosTransform : FoldersTransform;
        var outgoingTransform = index == 0 ? FoldersTransform : AllVideosTransform;

        var width = Math.Max(1, LibraryContentHost.ActualWidth);

        _libraryStoryboard?.Stop();
        _libraryStoryboard = null;

        if (width <= 1 || !new UISettings().AnimationsEnabled)
        {
            incoming.Visibility = Visibility.Visible;
            incoming.Opacity = 1;
            incomingTransform.X = 0;
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 1;
            outgoingTransform.X = 0;
            return;
        }

        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0;
        incomingTransform.X = direction * width;
        outgoing.Opacity = 1;
        outgoingTransform.X = 0;

        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        var inX = new DoubleAnimation
        {
            To = 0,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(inX, incomingTransform);
        Storyboard.SetTargetProperty(inX, "X");

        var inOpacity = new DoubleAnimation
        {
            To = 1,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(inOpacity, incoming);
        Storyboard.SetTargetProperty(inOpacity, "Opacity");

        var outX = new DoubleAnimation
        {
            To = -direction * width,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(outX, outgoingTransform);
        Storyboard.SetTargetProperty(outX, "X");

        var outOpacity = new DoubleAnimation
        {
            To = 0,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(outOpacity, outgoing);
        Storyboard.SetTargetProperty(outOpacity, "Opacity");

        storyboard.Children.Add(inX);
        storyboard.Children.Add(inOpacity);
        storyboard.Children.Add(outX);
        storyboard.Children.Add(outOpacity);
        storyboard.Completed += (_, _) =>
        {
            storyboard.Stop();
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 1;
            outgoingTransform.X = 0;
            incoming.Visibility = Visibility.Visible;
            incoming.Opacity = 1;
            incomingTransform.X = 0;
            _libraryStoryboard = null;
        };

        _libraryStoryboard = storyboard;
        storyboard.Begin();
    }

    private void LibraryContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LibraryContentHost.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
    }

    private void SelectLibraryTab(int index)
    {
        var item = index == 0 ? AllVideosTab : FoldersTab;
        if (!ReferenceEquals(LibrarySelector.SelectedItem, item))
        {
            LibrarySelector.SelectedItem = item;
        }
    }

    private void ShowHome()
    {
        ShowTopLevelView(HomeView);
        HomeView.Visibility = Visibility.Visible;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        RefreshHomeSection();
    }

    private void HomeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0)
        {
            _homeSection = index;
            RefreshHomeSection();
        }
    }

    private void RefreshHomeSection()
    {
        if (RecentList is null || HomeSectionTitle is null || RecentEmptyText is null)
        {
            return;
        }

        IReadOnlyList<MediaGroup> groups = _homeSection switch
        {
            1 => BuildActivityGroups(LibraryActivityStore.Favorites),
            2 => BuildActivityGroups(LibraryActivityStore.HistoryPaths),
            _ => Recent.ToList(),
        };

        RecentList.ItemsSource = groups;
        HomeSectionTitle.Text = LocalizationService.Get(_homeSection switch
        {
            1 => "Home_FavoritesTitle",
            2 => "Home_HistoryTitle",
            _ => "Ui_RecentMediaFiles.Text",
        });
        RecentEmptyText.Text = LocalizationService.Get(_homeSection switch
        {
            1 => "Home_NoFavorites",
            2 => "Home_NoHistory",
            _ => "Ui_NothingPlayedYet.Text",
        });
        RecentEmptyPanel.Visibility = groups.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshHomeMetadata(groups);
    }

    private List<MediaGroup> BuildActivityGroups(IReadOnlyList<string> paths)
    {
        var groups = new List<MediaGroup>();
        foreach (var path in paths)
        {
            if (!_itemsByPath.TryGetValue(path, out var item))
            {
                continue;
            }

            var group = new MediaGroup(path, item);
            ApplyCachedMetadata(group);
            groups.Add(group);
        }

        return groups;
    }

    private void RefreshHomeMetadata(IEnumerable<MediaGroup> groups)
    {
        foreach (var group in groups)
        {
            if (!_itemsByPath.TryGetValue(group.Best.Path, out var match))
            {
                continue;
            }

            group.Best.ApplyMetadata(match.DurationMs, match.Width, match.Height, match.ThumbnailPath);
            group.SetDuration(match.DurationMs);
            if (group.Thumbnail is null)
            {
                group.Thumbnail = LoadThumbnail(match.ThumbnailPath);
            }
        }
    }

    private void ShowQueue()
    {
        ShowTopLevelView(QueueView);
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Visible;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        QueueList.Focus(FocusState.Programmatic);
        LocalizeQueueControls();
        ApplyLanguageDeferred(QueueView);
    }

    /// <summary>Обновляет текст статуса обновлений после смены языка.</summary>
    private void RefreshUpdateStatusText()
    {
        if (UpdateStatusText is null)
        {
            return;
        }

        var currentVersion = AppInfo.DisplayVersion;
        var (text, brushKey) = _updateState switch
        {
            UpdateUiState.Checking => (
                LocalizationService.Get("Update_Checking"),
                "TextFillColorSecondaryBrush"),
            UpdateUiState.UpToDate => (
                LocalizationService.Format("Update_UpToDate", currentVersion),
                "SystemFillColorSuccessBrush"),
            UpdateUiState.UpdateAvailable => (
                LocalizationService.Format("Update_Available", _pendingUpdate?.Version ?? string.Empty, currentVersion),
                "AccentTextFillColorPrimaryBrush"),
            UpdateUiState.Downloading => (
                LocalizationService.Format("Update_Downloading", _updateDownloadProgress.ToString("P0")),
                "AccentTextFillColorPrimaryBrush"),
            UpdateUiState.Installing => (
                LocalizationService.Get("Update_Installing"),
                "AccentTextFillColorPrimaryBrush"),
            UpdateUiState.InstallFailed => (
                LocalizationService.Get("Update_InstallFailed"),
                "SystemFillColorCriticalBrush"),
            UpdateUiState.Failed => (
                LocalizationService.Get("Update_NetworkError"),
                "SystemFillColorCriticalBrush"),
            UpdateUiState.Dismissed => (
                LocalizationService.Get("Update_Dismissed.Text"),
                "TextFillColorSecondaryBrush"),
            UpdateUiState.NotConfigured => (
                LocalizationService.Get("Update_NotConfigured"),
                "TextFillColorSecondaryBrush"),
            _ => (
                LocalizationService.Format("Update_CurrentVersion", currentVersion),
                "TextFillColorSecondaryBrush"),
        };

        UpdateStatusText.Text = text;
        if (LookupThemeBrush(brushKey) is { } brush)
        {
            UpdateStatusText.Foreground = brush;
        }

        UpdateUpToDateIcon.Visibility = _updateState == UpdateUiState.UpToDate
            ? Visibility.Visible
            : Visibility.Collapsed;

        var hasUpdate = _pendingUpdate is not null;
        UpdateNowButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateNotesLink.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateLaterButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        if (hasUpdate)
        {
            UpdateNowButton.Content = LocalizationService.Format(
                "Settings_UpdatesInstallButton.Content",
                _pendingUpdate!.Version);
        }

        CheckUpdatesButton.IsEnabled = !_isCheckingUpdates;
        UpdateNowButton.IsEnabled = !_isCheckingUpdates;
    }

    /// <summary>Тематическая кисть по ключу; null, если ресурс недоступен.</summary>
    private static Brush? LookupThemeBrush(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Brush;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshLocalizedMediaText()
    {
        foreach (var item in Queue)
        {
            item.RefreshLocalizedText();
        }

        foreach (var item in PlaylistItems)
        {
            item.RefreshLocalizedText();
        }
    }

    private void LocalizeQueueControls()
    {
        string L(string key) => LocalizationService.Get(key);
        QueueClearLabel.Text = L("Queue_ClearLabel.Text");
        QueueAddLabel.Text = L("Queue_AddLabel.Text");
        ClearSelectionButton.Content = L("Queue_ClearSelection.Content");
        PlaySelectedNextButton.Content = L("Menu_PlayNext.Text");
        AddSelectedButton.Content = L("Queue_AddSelected.Content");
    }

    private void ShowPlaylists()
    {
        ShowTopLevelView(PlaylistsView);
        PlaylistDetailView.Visibility = Visibility.Collapsed;
        _openPlaylist = null;
        PlaylistItems.Clear();

        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Visible;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        CreatePlaylistPanel.Visibility = Visibility.Collapsed;
        UpdatePlaylistsState();
        LocalizationService.Apply(PlaylistsView, SettingsService.Language);
        ApplyLanguageDeferred(PlaylistsView);
    }

    private void ApplyLanguageDeferred(FrameworkElement view)
    {
        var epoch = _navigationEpoch;
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(
            () =>
            {
                // Ignore a render that belongs to a screen the user already left.
                if (epoch != _navigationEpoch && view.Visibility != Visibility.Visible)
                {
                    return;
                }

                LocalizationService.Apply(view, SettingsService.Language);
                if (view is not null && ReferenceEquals(view, LibraryPanel))
                {
                    RefreshLibraryHeader();
                }
            });
    }

    /// <summary>
    /// Восстанавливает вычисляемые подписи библиотеки после переприменения
    /// локализации: Apply сбрасывает их к значениям из XAML («0 песен»,
    /// «Читаю медиатеку…»), а данные при этом остаются загруженными.
    /// </summary>
    private void RefreshLibraryHeader()
    {
        if (_all.Count == 0)
        {
            return;
        }

        CountText.Text = SongCount(FilteredGroups.Count);
        UpdateLibraryStatusSummary();
    }

    private void ShowRemote()
    {
        ShowTopLevelView(RemoteView);
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        PlaylistDetailView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteView.Visibility = Visibility.Visible;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        UpdateRemoteNetworkState();
        ApplyLanguageDeferred(RemoteView);
    }

    private void ShowRemoteParticipants()
    {
        ShowTopLevelView(RemoteParticipantsView);
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        PlaylistDetailView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Visible;
        RefreshRemoteClients();
        LocalizationService.Apply(RemoteParticipantsView, SettingsService.Language);
        ApplyLanguageDeferred(RemoteParticipantsView);
    }

    private void RemoteModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _remoteDirectMode = ReferenceEquals(sender.SelectedItem, RemoteDirectTab);
        UpdateRemoteNetworkState();
    }

    private void UpdateRemoteNetworkState()
    {
        string L(string key) => LocalizationService.Get(key);
        var hotspotAddress = RemoteServer.FindHotspotAddress();
        var server = _remoteServer;
        var running = server?.IsRunning == true;
        var directActive = running &&
            !string.Equals(hotspotAddress, "127.0.0.1", StringComparison.Ordinal) &&
            server!.Url.Contains($"//{hotspotAddress}:", StringComparison.OrdinalIgnoreCase);
        var localActive = running && !directActive;
        var localAddress = RemoteServer.FindLanAddress();
        var directMode = _remoteDirectMode;
        var activeInMode = running && (directMode ? directActive : localActive);

        StateDisconnected.Visibility = activeInMode ? Visibility.Collapsed : Visibility.Visible;
        StateConnected.Visibility = activeInMode ? Visibility.Visible : Visibility.Collapsed;
        RemoteModeSelector.IsEnabled = !activeInMode;

        RemoteWifiTab.Text = L("Remote_TabWifi");
        RemoteDirectTab.Text = L("Remote_TabDirect");
        RemoteIntroBar.Title = L(directMode ? "Remote_TabDirect" : "Remote_TabWifi");
        var intro = L(directMode ? "Remote_IntroDirect" : "Remote_IntroWifi");
        var networkText = directMode
            ? string.Empty
            : string.Equals(localAddress, "127.0.0.1", StringComparison.Ordinal)
                ? L("Remote_DeviceIpUnknown")
                : LocalizationService.Format("Remote_DeviceIp", localAddress);
        RemoteIntroBar.Message = string.IsNullOrWhiteSpace(networkText)
            ? intro
            : intro + Environment.NewLine + networkText;

        RemoteDirectNetworkPanel.Visibility = directMode && directActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (directActive)
        {
            RemoteSsidText.Text = _hotspotSsid;
            RemotePasswordText.Text = _hotspotPassword;
            RemoteDirectIpText.Text = LocalizationService.Format("Remote_Ip", hotspotAddress);
        }

        RemoteStatePanel.Visibility = activeInMode ? Visibility.Visible : Visibility.Collapsed;
        RemoteStateText.Text = activeInMode
            ? directMode
                ? LocalizationService.Get($"Wifi_State_{WiFiDirectCoordinator.State}")
                : L("Remote_StateActive")
            : string.Empty;
        RemoteStateIp.Text = activeInMode
            ? LocalizationService.Format("Remote_Ip", directMode ? hotspotAddress : localAddress)
            : string.Empty;

        RemoteToggleButton.Visibility = !directMode && !activeInMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemoteToggleLabel.Text = L("Remote_ToggleStart");
        RemoteToggleButton.IsEnabled = !activeInMode &&
            !string.Equals(localAddress, "127.0.0.1", StringComparison.Ordinal);

        RemoteHotspotButton.Visibility = directMode && !activeInMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemoteHotspotButton.IsEnabled = !activeInMode;

        RemoteDetails.Visibility = activeInMode ? Visibility.Visible : Visibility.Collapsed;
        RemoteWifiQrPanel.Visibility = directMode && directActive && !string.IsNullOrWhiteSpace(_hotspotPassword)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (RemoteWifiQrPanel.Visibility == Visibility.Visible &&
            RemoteWifiQrImage.Source is null)
        {
            try
            {
                RemoteWifiQrImage.Source = new BitmapImage(
                    new Uri(RemoteServer.CreateWifiQr(_hotspotSsid, _hotspotPassword)));
            }
            catch
            {
                RemoteWifiQrPanel.Visibility = Visibility.Collapsed;
            }
        }

        if (activeInMode)
        {
            RemoteQrSteps.Text = L(directMode ? "Remote_QrStepsDirect" : "Remote_QrStepsWifi");
            RemoteStatusText.Text = L("Remote_HostHint");
        }
        else
        {
            RemoteQrSteps.Text = string.Empty;
            RemoteStatusText.Text = string.Empty;
        }

        RemoteNetworkHint.Text = activeInMode
            ? L(directMode ? "Remote_HintDirectActive" : "Remote_HintWifiActive")
            : L(directMode ? "Remote_HintDirectInactive" : "Remote_HintWifiInactive");
        LocalizationService.Apply(RemoteView, SettingsService.Language);
        UpdateWifiDirectStateUi();
    }

    private async void RemoteHotspot_Click(object sender, RoutedEventArgs e)
    {
        RemoteModeSelector.SelectedItem = RemoteDirectTab;
        ClearStaleNowPlaying();
        RemoteHotspotInfoBar.IsOpen = false;
        SetHotspotBusy(true);

        if (_remoteServer is null)
        {
            try
            {
                _remoteServer = new RemoteServer(this, _remoteClientDirectory);
                await _remoteServer.StartAsync();
                RemoteUrlText.Text = _remoteServer.Url;
                RemotePinValue.Text = _remoteServer.Pin;
                RemoteAdminPinValue.Text = _remoteServer.AdminPin;
                await SetQrImageAsync(RemoteQrImage, _remoteServer.QrPng);
                RemoteStatusText.Text = LocalizationService.Get("Remote_StatusStartingRemote");
                _remoteClientsTimer.Start();
                RefreshRemoteClients();
                UpdateRemoteNetworkState();
            }
            catch (Exception ex)
            {
                _remoteServer = null;
                RemoteNetworkHint.Text = LocalizationService.Format("Remote_ErrorStart", ex.Message);
                ShowHotspotError(ex.Message);
                SetHotspotBusy(false);
                UpdateRemoteNetworkState();
                return;
            }
        }

        await TryStartHotspotAsync();
    }

    private void SetHotspotBusy(bool busy)
    {
        RemoteHotspotButton.IsEnabled = !busy;
        RemoteHotspotIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        RemoteHotspotProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RemoteHotspotProgress.IsActive = busy;
        RemoteHotspotButtonText.Text = LocalizationService.Get(
            busy ? "Remote_HotspotStarting" : "Ui_StartAccessPoint_103.Text");
    }

    private void ShowHotspotError(string message)
    {
        RemoteHotspotInfoBar.Title = LocalizationService.Get("Remote_HotspotErrorTitle");
        RemoteHotspotInfoBar.Message = message;
        RemoteHotspotInfoBar.IsOpen = true;
    }

    private void OnWiFiDirectStateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            UpdateWifiDirectStateUi();
        }
        else
        {
            DispatcherQueue.TryEnqueue(UpdateWifiDirectStateUi);
        }
    }

    /// <summary>Shows the explicit Wi-Fi Direct state instead of flashing QR vs. "not started".</summary>
    private void UpdateWifiDirectStateUi()
    {
        if (RemoteWifiStateText is null)
        {
            return;
        }

        var state = WiFiDirectCoordinator.State;
        var text = LocalizationService.Get($"Wifi_State_{state}");
        if (state == WiFiDirectState.Error && !string.IsNullOrWhiteSpace(WiFiDirectCoordinator.LastError))
        {
            text += " — " + WiFiDirectCoordinator.LastError;
        }

        RemoteWifiStateText.Text = text;
    }

    private async void ShowWifiDirectBlocked(WiFiEnvironment environment)
    {
        var host = _remoteServer?.Url ?? string.Empty;
        _ = host;
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Ui_WifiBlockedTitle"),
            Content = LocalizationService.Format("Ui_WifiBlockedMessage", PrivacyMasking.MaskSsid(environment.Ssid)),
            PrimaryButtonText = LocalizationService.Get("Ui_OpenWifiSettings.Content"),
            CloseButtonText = LocalizationService.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("ms-settings:network-wifi"));
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot show the Wi-Fi Direct warning: {ex.Message}", "wifi-direct");
        }

        ShowHotspotError(LocalizationService.Format("Ui_WifiBlockedMessage", PrivacyMasking.MaskSsid(environment.Ssid)));
    }

    private async Task<bool> ConfirmWifiDirectRiskAsync(WiFiEnvironment environment)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Ui_WifiRiskTitle"),
            Content = LocalizationService.Format("Ui_WifiRiskMessage", PrivacyMasking.MaskSsid(environment.Ssid)),
            PrimaryButtonText = LocalizationService.Get("Ui_ContinueAnyway.Content"),
            CloseButtonText = LocalizationService.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot show the Wi-Fi Direct risk dialog: {ex.Message}", "wifi-direct");
            return false;
        }
    }

    private static async Task SetQrImageAsync(Image target, byte[] png)
    {
        if (png.Length == 0)
        {
            target.Source = null;
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(stream);
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            target.Source = bitmap;
        }
        catch (Exception ex)
        {
            target.Source = null;
            LogService.Warn($"Не удалось показать QR: {ex.Message}");
        }
    }

    private async Task TryStartHotspotAsync()
    {
        if (_remoteServer is null)
        {
            return;
        }

        if (WiFiDirectCoordinator.State is WiFiDirectState.Starting
            or WiFiDirectState.Advertising
            or WiFiDirectState.WaitingForConnection
            or WiFiDirectState.Connected)
        {
            // Idempotent: never restart an active publisher or recreate the QR code.
            UpdateRemoteNetworkState();
            return;
        }

        var environment = await WiFiDirectCoordinator.InspectAsync();
        if (environment.IsConnected && SettingsService.WifiDirectSafeMode)
        {
            SetHotspotBusy(false);
            ShowWifiDirectBlocked(environment);
            UpdateWifiDirectStateUi();
            return;
        }

        if (environment.IsConnected && !await ConfirmWifiDirectRiskAsync(environment))
        {
            SetHotspotBusy(false);
            return;
        }

        RemoteHotspotInfoBar.IsOpen = false;
        RemoteHotspotButton.IsEnabled = false;
        RemoteHotspotIcon.Visibility = Visibility.Collapsed;
        RemoteHotspotProgress.Visibility = Visibility.Visible;
        RemoteHotspotProgress.IsActive = true;
        RemoteHotspotButtonText.Text = LocalizationService.Get("Remote_HotspotStarting");
        RemoteStatusText.Text = LocalizationService.Get("Remote_StatusStartingAp");

        HotspotResult result;
        try
        {
            result = await WiFiDirectCoordinator.StartAsync();
        }
        finally
        {
            RemoteHotspotProgress.IsActive = false;
            RemoteHotspotProgress.Visibility = Visibility.Collapsed;
            RemoteHotspotIcon.Visibility = Visibility.Visible;
            RemoteHotspotButton.IsEnabled = true;
        }

        if (result.Started)
        {
            try
            {
                RemoteHotspotInfoBar.IsOpen = false;
                _hotspotSsid = result.Ssid;
                _hotspotPassword = result.Password;
                RemoteSsidText.Text = result.Ssid;
                RemotePasswordText.Text = result.Password;
                RemoteDirectNetworkPanel.Visibility = Visibility.Visible;
                RemoteWifiQrPanel.Visibility = Visibility.Visible;
                RemoteStateText.Text = LocalizationService.Get("Remote_WaitingForPhone");

                for (var attempt = 0; attempt < 24; attempt++)
                {
                    if (!string.Equals(RemoteServer.FindHotspotAddress(), "127.0.0.1", StringComparison.Ordinal))
                    {
                        break;
                    }

                    await Task.Delay(250);
                }

                _remoteServer.RefreshEndpoint(preferHotspot: true);
                RemoteUrlText.Text = _remoteServer.Url;
                RemotePinValue.Text = _remoteServer.Pin;
                RemoteAdminPinValue.Text = _remoteServer.AdminPin;
                await SetQrImageAsync(RemoteQrImage, _remoteServer.QrPng);
                await SetQrImageAsync(
                    RemoteWifiQrImage,
                    RemoteServer.CreateWifiQrBytes(result.Ssid, result.Password));

                RemoteModeSelector.SelectedItem = RemoteDirectTab;
                UpdateRemoteNetworkState();
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось показать параметры Wi-Fi Direct: {ex.Message}");
                RemoteHotspotButtonText.Text = LocalizationService.Get("Ui_StartAccessPoint_103.Text");
                ShowHotspotError(ex.Message);
                UpdateRemoteNetworkState();
            }
        }
        else
        {
            RemoteHotspotButtonText.Text = LocalizationService.Get("Ui_StartAccessPoint_103.Text");
            RemoteHotspotInfoBar.Title = LocalizationService.Get("Remote_HotspotErrorTitle");
            RemoteHotspotInfoBar.Message = result.Message;
            RemoteHotspotInfoBar.IsOpen = true;
            UpdateRemoteNetworkState();
        }
    }

    private async void RemoteHotspotRetry_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteServer is null)
        {
            RemoteHotspot_Click(sender, e);
            return;
        }

        await TryStartHotspotAsync();
    }

    private async void RemoteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteServer is { IsRunning: true } && !_remoteDirectMode)
        {
            await StopRemoteAsync();
            RemoteModeSelector.SelectedItem = RemoteWifiTab;
            UpdateRemoteNetworkState();
            return;
        }

        if (_remoteDirectMode || RemoteServer.FindLanAddress() == "127.0.0.1")
        {
            UpdateRemoteNetworkState();
            return;
        }

        ClearStaleNowPlaying();

        try
        {
            _remoteServer = new RemoteServer(this, _remoteClientDirectory);
            await _remoteServer.StartAsync();
            RemoteUrlText.Text = _remoteServer.Url;
            RemotePinValue.Text = _remoteServer.Pin;
            RemoteAdminPinValue.Text = _remoteServer.AdminPin;
            await SetQrImageAsync(RemoteQrImage, _remoteServer.QrPng);

            RemoteModeSelector.SelectedItem = RemoteWifiTab;
            _remoteClientsTimer.Start();
            RefreshRemoteClients();
            UpdateRemoteNetworkState();
        }
        catch (Exception ex)
        {
            _remoteServer = null;
            RemoteDetails.Visibility = Visibility.Collapsed;
            RemoteNetworkHint.Text = LocalizationService.Format("Remote_ErrorStart", ex.Message);
            UpdateRemoteNetworkState();
        }
    }

    private async void RemoteStop_Click(object sender, RoutedEventArgs e)
    {
        await StopRemoteAsync();
        UpdateRemoteNetworkState();
    }

    /// <summary>Полностью выключает пульт: сервер, точка доступа и UI-состояние.</summary>
    private async Task StopRemoteAsync()
    {
        await _remoteStopLock.WaitAsync();
        try
        {
            _remoteIdleTimer.Stop();
            _remoteClientsTimer.Stop();
            await HotspotService.StopAsync();
            if (_remoteServer is not null)
            {
                try
                {
                    await _remoteServer.DisposeAsync();
                }
                catch
                {
                    // Сервер мог уже остановиться.
                }

                _remoteServer = null;
            }

            RemoteDetails.Visibility = Visibility.Collapsed;
            RemoteQrImage.Source = null;
            RemoteWifiQrImage.Source = null;
            _hotspotPassword = string.Empty;
            UpdateRemoteNetworkState();
            RefreshRemoteClients();
        }
        finally
        {
            _remoteStopLock.Release();
        }
    }

    /// <summary>Гасит пульт, если телефоны не подключались дольше заданного времени.</summary>
    private async Task CheckRemoteIdleAsync()
    {
        if (_remoteServer is not { IsRunning: true } server ||
            !SettingsService.RemoteAutoStopEnabled ||
            server.ClientCount > 0)
        {
            return;
        }

        var idle = DateTime.UtcNow - server.LastClientActivityUtc;
        if (idle < TimeSpan.FromMinutes(SettingsService.RemoteAutoStopMinutes))
        {
            return;
        }

        await StopRemoteAsync();
        RemoteStateText.Text = string.Empty;
        RemoteNetworkHint.Text = LocalizationService.Format(
            "Remote_AutoStopped", SettingsService.RemoteAutoStopMinutes);
    }

    private void RemoteCopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RemoteUrlText.Text))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(RemoteUrlText.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            RemoteStatusText.Text = LocalizationService.Get("Remote_LinkCopied");
            ShowCopyConfirmation();
        }
        catch
        {
            RemoteStatusText.Text = LocalizationService.Get("Remote_LinkCopyFailed");
        }
    }

    private void ShowCopyConfirmation()
    {
        if (RemoteCopyIcon is null || RemoteCopyCheckIcon is null)
        {
            return;
        }

        RemoteCopyIcon.Opacity = 0;
        RemoteCopyCheckIcon.Opacity = 1;
        _copyIconTimer?.Stop();
        _copyIconTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _copyIconTimer.Interval = TimeSpan.FromSeconds(2);
        _copyIconTimer.IsRepeating = false;
        _copyIconTimer.Tick += (_, _) =>
        {
            RemoteCopyIcon.Opacity = 1;
            RemoteCopyCheckIcon.Opacity = 0;
            _copyIconTimer?.Stop();
        };
        _copyIconTimer.Start();
    }

    /// <summary>Обновляет плашку роли и список устройств в параметрах.</summary>
    private void RefreshRemoteClients()
    {
        if (RemoteRoleBadge is null)
        {
            return;
        }

        var clients = _remoteClientDirectory.Snapshot();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = RemoteClients
            .GroupBy(row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var client in clients)
        {
            var key = !string.IsNullOrWhiteSpace(client.MacAddress)
                ? client.MacAddress
                : client.ClientId;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            keys.Add(key);
            if (!existing.TryGetValue(key, out var row))
            {
                row = new RemoteClientRow { Key = key };
                RemoteClients.Add(row);
            }

            row.ClientId = client.ClientId;
            row.MacAddress = client.MacAddress ?? string.Empty;
            row.NetworkName = string.IsNullOrWhiteSpace(client.NetworkName)
                ? client.DisplayName
                : client.NetworkName;
            row.IpAddress = client.IpAddress;
            row.RoleText = client.RoleText;
            row.ConnectionText = client.ConnectionText;
            row.IsOnline = client.IsConnected;
            row.RefreshLanguage();
            if (!row.IsEditing && !string.IsNullOrWhiteSpace(client.DisplayName))
            {
                row.Name = client.DisplayName;
            }
            else if (string.IsNullOrWhiteSpace(row.Name))
            {
                row.Name = client.DisplayName;
            }

            row.OrderedSongs =
                Queue.Count(item => !string.IsNullOrWhiteSpace(item.QueuedBy) &&
                    string.Equals(item.QueuedBy, row.Name, StringComparison.CurrentCultureIgnoreCase)) +
                _guestRequestArchive.Count(request =>
                    string.Equals(request.Requester, row.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        foreach (var row in RemoteClients.ToList())
        {
            if (!keys.Contains(row.Key))
            {
                RemoteClients.Remove(row);
            }
        }

        // Имена в очереди обновляются сразу после переименования устройства.
        foreach (var item in Queue)
        {
            item.SetQueuedBy(ResolveRequester(item));
        }

        if (RemoteClientsEmptyText is not null)
        {
            RemoteClientsEmptyText.Visibility = RemoteClients.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (RemoteParticipantsBadge is not null)
        {
            var online = RemoteClients.Count(row => row.IsOnline);
            RemoteParticipantsBadge.Value = online;
            RemoteParticipantsBadge.Visibility = online > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        UpdateRemoteRoleBadge();
        WiFiDirectCoordinator.NotifyClientCount(_remoteServer?.ClientCount ?? 0);
    }

    private void UpdateRemoteRoleBadge()
    {
        if (RemoteRoleBadge is null)
        {
            return;
        }

        var badge = _remoteServer?.IsRunning == true
            ? _remoteClientDirectory.BadgeText()
            : null;

        RemoteRoleBadge.Visibility = string.IsNullOrWhiteSpace(badge)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(badge))
        {
            RemoteRoleText.Text = badge;
        }
    }

    private void RemoteClientName_GotFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is RemoteClientRow row)
        {
            row.IsEditing = true;
        }
    }

    private void RemoteClientName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: RemoteClientRow row })
        {
            return;
        }

        SaveRemoteClientName(row);
    }

    /// <summary>Enter подтверждает имя: уводим фокус, сохранение делает LostFocus.</summary>
    private void RemoteClientName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || sender is not TextBox textBox)
        {
            return;
        }

        e.Handled = true;
        if (!FocusManager.TryMoveFocus(FocusNavigationDirection.Next) &&
            textBox.Tag is RemoteClientRow row)
        {
            SaveRemoteClientName(row);
        }
    }

    private void SaveRemoteClientName(RemoteClientRow row)
    {
        row.IsEditing = false;
        if (!row.HasMac)
        {
            _remoteClientDirectory.SetClientName(row.ClientId, row.Name);
            RefreshRemoteClients();
            return;
        }

        if (_remoteClientDirectory.SetAlias(row.MacAddress, row.Name))
        {
            SetRemoteClientsHint(string.IsNullOrWhiteSpace(row.Name)
                ? LocalizationService.Get("Remote_NameRemoved")
                : LocalizationService.Get("Remote_NameSaved"));
            RefreshRemoteClients();
        }
    }

    private void BanRemoteClient_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RemoteClientRow row)
        {
            return;
        }

        _remoteServer?.BanClient(row.ClientId);
        row.IsOnline = false;
        SetRemoteClientsHint(LocalizationService.Get("Remote_DeviceBlocked"));
        RefreshRemoteClients();
    }

    private void RemoveRemoteClient_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RemoteClientRow row ||
            !_remoteClientDirectory.RemoveAlias(row.MacAddress))
        {
            SetRemoteClientsHint(LocalizationService.Get("Remote_DeviceNotFound"));
            return;
        }

        row.Name = string.Empty;
        SetRemoteClientsHint(LocalizationService.Get("Remote_NameRemoved"));
        RefreshRemoteClients();
    }

    private void SetRemoteClientsHint(string text)
    {
        if (RemoteClientsHint is not null)
        {
            RemoteClientsHint.Text = text;
        }
    }

    private void Playlists_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildPlaylistsNav();
    }

    private void RebuildPlaylistsNav()
    {
        foreach (var playlist in Playlists)
        {
            playlist.PropertyChanged -= Playlist_NavPropertyChanged;
            playlist.PropertyChanged += Playlist_NavPropertyChanged;
        }

        PlaylistsNavItem.MenuItems.Clear();
        foreach (var playlist in Playlists)
        {
            var item = new NavigationViewItem
            {
                Content = playlist.Name,
                Tag = playlist.Id,
                Icon = new FontIcon { Glyph = "\uE8D6" },
                ContextFlyout = CreatePlaylistNavFlyout(playlist),
            };
            PlaylistsNavItem.MenuItems.Add(item);
        }

        PlaylistsNavItem.IsExpanded = Playlists.Count > 0;
    }

    private void Playlist_NavPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Playlist playlist || e.PropertyName != nameof(Playlist.Name))
        {
            return;
        }

        if (FindPlaylistNavItem(playlist.Id) is { } item)
        {
            item.Content = playlist.Name;
        }

        if (ReferenceEquals(_openPlaylist, playlist))
        {
            PlaylistDetailTitle.Text = playlist.Name;
        }
    }

    private NavigationViewItem? FindPlaylistNavItem(string playlistId)
    {
        return PlaylistsNavItem.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, playlistId, StringComparison.Ordinal));
    }

    private MenuFlyout CreatePlaylistNavFlyout(Playlist playlist)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("PlaylistNavPlay.Text", "\uE768", () => PlayPlaylistQuick(playlist)));
        flyout.Items.Add(CreateMenuItem("PlaylistNavQueue.Text", "\uE710", () => QueuePlaylistNext(playlist)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("PlaylistNavRename.Text", "\uE8AC", () => _ = RenamePlaylistAsync(playlist)));
        flyout.Items.Add(CreateMenuItem("PlaylistNavDelete.Text", "\uE74D", () => _ = DeletePlaylistWithConfirmAsync(playlist)));
        return flyout;
    }

    private void UpdatePlaylistsState()
    {
        var hasPlaylists = Playlists.Count > 0;
        var creating = CreatePlaylistPanel.Visibility == Visibility.Visible;

        PlaylistsEmptyPanel.Visibility = !creating && !hasPlaylists
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistsList.Visibility = !creating && hasPlaylists
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistsHeaderCreateButton.Visibility = !creating && hasPlaylists
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PlaylistsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist)
        {
            OpenPlaylist(playlist);
        }
    }

    private void OpenPlaylist(Playlist playlist, bool syncNavigation = true)
    {
        ShowTopLevelView(PlaylistDetailView);
        ResetPlaylistSelection();
        _openPlaylist = playlist;
        PlaylistsView.Visibility = Visibility.Collapsed;
        PlaylistDetailView.Visibility = Visibility.Visible;
        PlaylistDetailTitle.Text = playlist.Name;
        PlayPlaylistLabel.Text = LocalizationService.Get("Playlist_PlayAll.Content");
        AddPlaylistToQueueLabel.Text = LocalizationService.Get("Playlist_AddAllToQueue.Content");
        RefreshPlaylistItems();
        LocalizationService.Apply(PlaylistDetailView, SettingsService.Language);
        ApplyLanguageDeferred(PlaylistDetailView);

        if (syncNavigation)
        {
            PlaylistsNavItem.IsExpanded = true;
            NavView.SelectedItem = FindPlaylistNavItem(playlist.Id) ?? (object)playlist;
        }
    }

    private Playlist? ResolveNavigationPlaylist(object sender)
    {
        var id = (sender as FrameworkElement)?.Tag as string;
        return Playlists.FirstOrDefault(playlist =>
            string.Equals(playlist.Id, id, StringComparison.Ordinal));
    }

    private void PlaylistNavPlay_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveNavigationPlaylist(sender) is { } playlist)
        {
            OpenPlaylist(playlist);
            PlayPlaylistQuick(playlist);
        }
    }

    private void PlaylistNavQueue_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveNavigationPlaylist(sender) is { } playlist)
        {
            QueuePlaylistNext(playlist);
        }
    }

    private async void PlaylistNavRename_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveNavigationPlaylist(sender) is { } playlist)
        {
            await RenamePlaylistAsync(playlist);
        }
    }

    private async void PlaylistNavDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveNavigationPlaylist(sender) is { } playlist)
        {
            await DeletePlaylistWithConfirmAsync(playlist);
        }
    }

    private void ResetPlaylistSelection()
    {
        _playlistSelectionMode = false;
        foreach (var item in PlaylistItems)
        {
            item.IsPlaylistSelected = false;
        }
    }

    private void RefreshPlaylistItems()
    {        PlaylistItems.Clear();
        if (_openPlaylist is null)
        {
            return;
        }

        foreach (var path in _openPlaylist.Items)
        {
            var item = _all.FirstOrDefault(candidate =>
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = MediaItem.FromPath(path);
                ApplyCachedMetadata(item);
            }

            PlaylistItems.Add(item);
        }

        PlaylistDetailTitle.Text = _openPlaylist.Name;
        PlaylistDetailSubtitle.Text = PlaylistItems.Count switch
        {
            0 => LocalizationService.Get("Playlist_Empty"),
            1 => LocalizationService.FormatPlural("Plural_VideoCount", 1, 1),
            _ => LocalizationService.FormatPlural("Plural_VideoCount", PlaylistItems.Count, PlaylistItems.Count),
        };
        PlaylistItemsList.Visibility = PlaylistItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistDetailEmpty.Visibility = PlaylistItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var playlistPlayingMarked = false;
        foreach (var item in PlaylistItems)
        {
            var isPlaying = _current is not null && !playlistPlayingMarked &&
                string.Equals(item.Path, _current.Path, StringComparison.OrdinalIgnoreCase);
            item.IsPlaying = isPlaying;
            playlistPlayingMarked |= isPlaying;
        }

        for (var index = 0; index < PlaylistItems.Count; index++)
        {
            PlaylistItems[index].IsAlternateRow = index % 2 == 1;
        }

        UpdatePlaylistSelectionVisuals();
        UpdatePlaylistRowBackgrounds();
    }

    private void PlaylistBack_Click(object sender, RoutedEventArgs e)
    {
        ResetPlaylistSelection();
        _openPlaylist = null;
        PlaylistItems.Clear();
        PlaylistDetailView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Visible;
        UpdatePlaylistsState();
    }

    private void PlayPlaylist_Click(object sender, RoutedEventArgs e)
    {
        PlayPlaylistFrom(0);
    }

    private void PlaylistItemsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (PlaylistItemsList.SelectedIndex >= 0)
        {
            PlayPlaylistFrom(PlaylistItemsList.SelectedIndex);
        }
    }

    private void PlaylistItemsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && PlaylistItemsList.SelectedIndex >= 0)
        {
            PlayPlaylistFrom(PlaylistItemsList.SelectedIndex);
            e.Handled = true;
        }
    }

    private void PlayPlaylistFrom(int index)
    {
        if (_openPlaylist is null || index < 0 || index >= PlaylistItems.Count)
        {
            return;
        }

        _isRestoringQueue = true;
        _queue.Clear();
        _queueRequesters.Clear();
        foreach (var item in PlaylistItems)
        {
            var entry = item.CreateQueueCopy();
            _queue.Add(entry);
            _queueRequesters[entry] = null;
            entry.SetQueuedBy("Компьютер");
        }

        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(index);
    }

    private void PlaylistItem_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetPlaylistItemPointerState(sender, true);

    private void PlaylistItem_PointerExited(object sender, PointerRoutedEventArgs e) =>
        SetPlaylistItemPointerState(sender, false);

    private void SetPlaylistItemPointerState(object sender, bool entered)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        var item = root.DataContext as MediaItem;
        item?.IsHovered = entered;

        if (root is Grid row)
        {
            row.Background = entered || item?.IsAlternateRow != true
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
                : ThemedBrush(row, "QueueAlternateRowBrush");
        }

        if (root.FindName("PlaylistItemDelete") is Button delete)
        {
            delete.Opacity = entered ? 1 : 0;
            delete.IsHitTestVisible = entered;
        }

        if (root.FindName("PlaylistItemMoviesIcon") is FontIcon movies)
        {
            movies.Opacity = entered ? 0 : 1;
        }

        if (root.FindName("PlaylistItemPlay") is Button play)
        {
            play.Opacity = entered ? 1 : 0;
            play.IsHitTestVisible = entered;
        }

        if (root.FindName("PlaylistItemCheck") is CheckBox check)
        {
            var visible = item?.IsPlaying != true &&
                (entered || (item?.IsPlaylistSelected ?? false) || _playlistSelectionMode);
            check.Opacity = visible ? 1 : 0;
            check.IsHitTestVisible = visible;
        }
    }

    private void PlaylistItemCheck_Checked(object sender, RoutedEventArgs e)
    {
        _playlistSelectionMode = true;
        UpdatePlaylistSelectionVisuals();
    }

    private void PlaylistItemCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdatePlaylistSelectionVisuals();
    }

    private void UpdatePlaylistSelectionVisuals()
    {
        foreach (var item in PlaylistItems)
        {
            if (PlaylistItemsList.ContainerFromItem(item) is ListViewItem container &&
                FindElementByName(container, "PlaylistItemCheck") is CheckBox check)
            {
                var visible = item.IsHovered || _playlistSelectionMode || item.IsPlaylistSelected;
                check.Opacity = visible ? 1 : 0;
                check.IsHitTestVisible = visible;
            }
        }
    }

    private void UpdatePlaylistRowBackgrounds()
    {
        foreach (var item in PlaylistItems)
        {
            if (PlaylistItemsList.ContainerFromItem(item) is ListViewItem container &&
                FindElementByName(container, "PlaylistItemRow") is Grid row)
            {
                row.Background = item.IsAlternateRow && !item.IsHovered
                    ? ThemedBrush(row, "QueueAlternateRowBrush")
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
        }
    }

    private void PlaylistItemsList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        var item = args.Item as MediaItem;
        if (FindElementByName(args.ItemContainer, "PlaylistItemRow") is Grid row)
        {
            row.Background = item?.IsAlternateRow == true && item.IsHovered == false
                ? ThemedBrush(row, "QueueAlternateRowBrush")
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        if (item is null ||
            FindElementByName(args.ItemContainer, "PlaylistItemCheck") is not CheckBox check)
        {
            return;
        }

        var visible = item.IsHovered || _playlistSelectionMode || item.IsPlaylistSelected;
        check.Opacity = visible ? 1 : 0;
        check.IsHitTestVisible = visible;
    }

    private void PlaylistItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            PlayPlaylistItem(item);
        }
    }

    private void PlaylistItemPlay_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: FontIcon icon })
        {
            icon.Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush");
        }
    }

    private void PlaylistItemPlay_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: FontIcon icon })
        {
            icon.Foreground = ThemeBrush("TextFillColorSecondaryBrush");
        }
    }

    private void PlayPlaylistItem(MediaItem item)
    {
        var index = Queue.IndexOf(item);
        if (index >= 0)
        {
            PlayQueueItem(item);
            return;
        }

        _queueIndex = -1;
        _current = item;
        RecordRecent(item);
        MarkPlayingItem(item);
        StartPlayback(item);
        UpdateNowPlaying();
    }

    private void PlaylistItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement row && row.DataContext is MediaItem item)
        {
            ShowTrackContextMenu(row, item, e.GetPosition(row), isQueue: false);
            e.Handled = true;
        }
    }

    private void QueueItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement row && row.DataContext is MediaItem item)
        {
            ShowTrackContextMenu(row, item, e.GetPosition(row), isQueue: true);
            e.Handled = true;
        }
    }

    /// <summary>ПКМ по карточке видео: те же действия, что и в меню «···».</summary>
    private void MediaCard_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement card || card.DataContext is not MediaGroup group)
        {
            return;
        }

        if (!args.TryGetPosition(card, out var position))
        {
            position = new Point(0, 0);
        }

        ShowLibraryItemContextMenu(card, group, position);
        args.Handled = true;
    }

    private void ShowLibraryItemContextMenu(FrameworkElement target, MediaGroup group, Point position)
    {
        var started = Stopwatch.StartNew();
        var item = group.Best;
        var flyout = new MenuFlyout();

        // ПКМ не должен оставлять системный «занятый» курсор, пока открыто меню.
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        flyout.Closed += (_, _) => ProtectedCursor = null;

        flyout.Items.Add(CreateMenuItem("ContextMenu_Play.Text", "\uE768", () => PlayNow(item)));
        flyout.Items.Add(CreateMenuItem("Menu_PlayNext.Text", "\uED35", () => EnqueueItemNext(item)));

        var addTo = new MenuFlyoutSubItem
        {
            Text = LocalizationService.Get("ContextMenu_AddTo.Text"),
            Icon = CreateMenuIcon("\uE710"),
        };
        addTo.Items.Add(CreateMenuItem("ContextMenu_Queue.Text", "\uE8D6", () => AddItemToQueueFromMenu(item)));
        addTo.Items.Add(new MenuFlyoutSeparator());
        addTo.Items.Add(CreateMenuItem("ContextMenu_NewPlaylist.Text", "\uE710", () => _ = CreatePlaylistWithItemAsync(item)));
        foreach (var playlist in Playlists.ToList())
        {
            var targetPlaylist = playlist;
            addTo.Items.Add(CreateMenuItem(targetPlaylist.Name, "\uE8D6", () => AddItemToPlaylist(targetPlaylist, item), localize: false));
        }

        flyout.Items.Add(addTo);

        var favorite = LibraryActivityStore.IsFavorite(item.Path);
        flyout.Items.Add(CreateMenuItem(
            favorite ? "ContextMenu_RemoveFavorite" : "ContextMenu_AddFavorite",
            "\uE734",
            () =>
            {
                LibraryActivityStore.ToggleFavorite(item.Path);
                RefreshHomeSection();
            }));

        flyout.Items.Add(CreateMenuItem("ContextMenu_Properties.Text", "\uE946", () => _ = ShowPropertiesAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("ContextMenu_Select.Text", "\uE73A", () => EnterCardSelection(group)));

        flyout.ShowAt(target, position);
        if (started.ElapsedMilliseconds > 100)
        {
            LogService.Warn($"[context] media menu took {started.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>Включает режим выбора чек-боксами и отмечает карточку.</summary>
    private void EnterCardSelection(MediaGroup group)
    {
        foreach (var collection in new[] { FilteredGroups, Recent, SearchResults })
        {
            foreach (var card in collection)
            {
                card.IsSelectionVisible = true;
            }
        }

        group.IsSelected = true;
        UpdateLibrarySelectionBar();
    }

    private void CardSelectCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateLibrarySelectionBar();
    }

    /// <summary>Панель управления выделенными карточками библиотеки.</summary>
    private void UpdateLibrarySelectionBar()
    {
        if (LibrarySelectionBar is null || LibrarySelectionText is null || LibraryToolbar is null)
        {
            return;
        }

        var groups = FilteredGroups;
        var selected = groups.Count(group => group.IsSelected);
        var active = selected > 0;

        LibrarySelectionBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        LibraryToolbar.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        LibrarySelectionText.Text = LocalizationService.FormatPlural("Selection_Count", selected, selected);

        if (LibrarySelectAllCheck is not null)
        {
            _updatingLibrarySelectAll = true;
            LibrarySelectAllCheck.IsChecked = groups.Count > 0 && selected == groups.Count;
            _updatingLibrarySelectAll = false;
        }
    }

    private void LibrarySelectAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingLibrarySelectAll)
        {
            return;
        }

        var select = LibrarySelectAllCheck.IsChecked == true;
        foreach (var group in FilteredGroups)
        {
            group.IsSelectionVisible = true;
            group.IsSelected = select;
        }

        UpdateLibrarySelectionBar();
    }

    private void ClearLibrarySelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in FilteredGroups)
        {
            group.IsSelected = false;
            group.IsSelectionVisible = false;
        }

        UpdateLibrarySelectionBar();
    }

    private List<MediaItem> SelectedLibraryItems() =>
        FilteredGroups.Where(group => group.IsSelected).Select(group => group.Best).ToList();

    private void PlaySelectedLibraryNext_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedLibraryItems();
        if (items.Count == 0)
        {
            return;
        }

        // Вставляем с конца, чтобы итоговый порядок совпал с порядком карточек.
        for (var index = items.Count - 1; index >= 0; index--)
        {
            EnqueueItemNext(items[index]);
        }
    }

    private void AddSelectedLibraryToQueue_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedLibraryItems();
        if (items.Count == 0)
        {
            return;
        }

        var added = 0;
        foreach (var item in items)
        {
            if (Queue.Any(entry => string.Equals(entry.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddToQueue(item);
            added++;
        }

        SetPlaylistStatus(LocalizationService.Get(
            added > 0 ? "Playlist_StatusAddedToQueue" : "Playlist_StatusAlreadyInQueue"));
    }

    private void AddSelectedLibraryToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        ShowAddToPlaylistFlyout(element, SelectedLibraryItems());
    }

    private void ShowTrackContextMenu(
        FrameworkElement row,
        MediaItem item,
        Point position,
        bool isQueue)
    {
        var index = isQueue ? Queue.IndexOf(item) : PlaylistItems.IndexOf(item);
        var count = isQueue ? Queue.Count : PlaylistItems.Count;

        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem(
            "ContextMenu_Play.Text",
            "\uE768",
            () =>
            {
                if (isQueue)
                {
                    PlayQueueItem(item);
                }
                else
                {
                    PlayPlaylistItem(item);
                }
            }));
        flyout.Items.Add(CreateMenuItem("Menu_PlayNext.Text", "\uED35", () => EnqueueItemNext(item)));

        var addTo = new MenuFlyoutSubItem
        {
            Text = LocalizationService.Get("ContextMenu_AddTo.Text"),
            Icon = CreateMenuIcon("\uE710"),
        };
        addTo.Items.Add(CreateMenuItem("ContextMenu_Queue.Text", "\uE8D6", () => AddItemToQueueFromMenu(item)));
        addTo.Items.Add(new MenuFlyoutSeparator());
        addTo.Items.Add(CreateMenuItem("ContextMenu_NewPlaylist.Text", "\uE710", () => _ = CreatePlaylistWithItemAsync(item)));
        foreach (var playlist in Playlists.ToList())
        {
            var target = playlist;
            addTo.Items.Add(CreateMenuItem(
                target.Name,
                "\uE8D6",
                () => AddItemToPlaylist(target, item),
                localize: false));
        }

        flyout.Items.Add(addTo);
        var favorite = LibraryActivityStore.IsFavorite(item.Path);
        flyout.Items.Add(CreateMenuItem(
            favorite ? "ContextMenu_RemoveFavorite" : "ContextMenu_AddFavorite",
            "\uE734",
            () =>
            {
                LibraryActivityStore.ToggleFavorite(item.Path);
                RefreshHomeSection();
            }));
        flyout.Items.Add(CreateMenuItem(
            "Common_Delete.Text",
            "\uE74D",
            () => RemoveTrackFromCurrentList(item, isQueue)));

        var moveUp = CreateMenuItem("ContextMenu_MoveUp.Text", "\uE74A", () => MoveTrack(item, isQueue, -1));
        moveUp.IsEnabled = index > 0;
        flyout.Items.Add(moveUp);

        var moveDown = CreateMenuItem("ContextMenu_MoveDown.Text", "\uE74B", () => MoveTrack(item, isQueue, 1));
        moveDown.IsEnabled = index >= 0 && index < count - 1;
        flyout.Items.Add(moveDown);

        flyout.Items.Add(CreateMenuItem("ContextMenu_Properties.Text", "\uE946", () => _ = ShowPropertiesAsync(item)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem(
            "ContextMenu_Select.Text",
            "\uE762",
            () =>
            {
                if (isQueue)
                {
                    SelectQueueItem(item);
                }
                else
                {
                    SelectPlaylistItem(item);
                }
            }));

        flyout.ShowAt(row, new FlyoutShowOptions { Position = position });
    }

    private static MenuFlyoutItem CreateMenuItem(string keyOrText, string glyph, Action action, bool localize = true)
    {
        var menuItem = new MenuFlyoutItem
        {
            Text = localize ? LocalizationService.Get(keyOrText) : keyOrText,
            Icon = CreateMenuIcon(glyph),
        };
        menuItem.Click += (_, _) => action();
        return menuItem;
    }

    private static FontIcon CreateMenuIcon(string glyph) => new()
    {
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 16,
        Glyph = glyph,
    };

    private void AddItemToQueueFromMenu(MediaItem item)
    {
        if (Queue.Any(entry => string.Equals(entry.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
        {
            SetPlaylistStatus(LocalizationService.Get("Playlist_StatusAlreadyInQueue"));
            return;
        }

        AddToQueue(item);
        SetPlaylistStatus(LocalizationService.Get("Playlist_StatusAddedToQueue"));
    }

    private void RemoveTrackFromCurrentList(MediaItem item, bool isQueue)
    {
        if (isQueue)
        {
            RemoveQueueItem(item);
        }
        else
        {
            RemovePlaylistItem(item);
        }
    }

    private void MoveTrack(MediaItem item, bool isQueue, int delta)
    {
        if (isQueue)
        {
            MoveQueueItem(item, delta);
        }
        else
        {
            MovePlaylistItem(item, delta);
        }
    }

    private void MoveQueueItem(MediaItem item, int delta)
    {
        var index = Queue.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Queue.Count)
        {
            return;
        }

        _queue.Move(index, target);
        _queueIndex = _current is null ? -1 : Queue.IndexOf(_current);
        SaveQueue();
        UpdateQueueCount();
    }

    private void MovePlaylistItem(MediaItem item, int delta)
    {
        if (_openPlaylist is null)
        {
            return;
        }

        var items = _openPlaylist.Items;
        var index = items.FindIndex(path =>
            string.Equals(path, item.Path, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        PlaylistService.Save(Playlists);
        RefreshPlaylistItems();
    }

    private void SelectQueueItem(MediaItem item)
    {
        item.IsSelected = true;
        UpdateQueueRows();
        UpdateQueueCheckboxVisibility();
        UpdateSelectionBar();
    }

    private void RemovePlaylistItem(MediaItem item)
    {
        if (_openPlaylist is null || !_openPlaylist.RemoveItem(item.Path))
        {
            return;
        }

        PlaylistService.Save(Playlists);
        RefreshPlaylistItems();
        SetPlaylistStatus(LocalizationService.Get("Playlist_StatusRemoved"));
    }

    private void MovePlaylistItemDown(MediaItem item) => MovePlaylistItem(item, 1);

    private void AddItemToPlaylist(Playlist playlist, MediaItem item)
    {
        if (playlist.AddItem(item.Path))
        {
            PlaylistService.Save(Playlists);
            UpdatePlaylistsState();
            SetPlaylistStatus(LocalizationService.Format("Playlist_StatusAddedToName", playlist.Name));
        }
        else
        {
            SetPlaylistStatus(LocalizationService.Format("Playlist_StatusAlreadyContains", playlist.Name));
        }
    }

    private async Task CreatePlaylistWithItemAsync(MediaItem item)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = LocalizationService.Get("Playlist_NamePlaceholder"),
            Text = LocalizationService.Get("Playlist_New"),
        };
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_New"),
            Content = nameBox,
            PrimaryButtonText = LocalizationService.Get("Common_Create"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var baseName = string.IsNullOrWhiteSpace(nameBox.Text) ? LocalizationService.Get("Playlist_New") : nameBox.Text.Trim();
        var name = baseName;
        var suffix = 2;
        while (Playlists.Any(playlist => string.Equals(playlist.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name = $"{baseName} ({suffix++})";
        }

        var created = new Playlist { Name = name };
        created.AddItem(item.Path);
        Playlists.Add(created);
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
        SetPlaylistStatus(LocalizationService.Format("Playlist_StatusTrackAdded", name));
    }

    private void SelectPlaylistItem(MediaItem item)
    {
        _playlistSelectionMode = true;
        item.IsPlaylistSelected = true;
        UpdatePlaylistSelectionVisuals();
    }

    private void EnqueueItemNext(MediaItem item)
    {
        var current = _queueIndex >= 0 && _queueIndex < Queue.Count
            ? Queue[_queueIndex]
            : null;
        var queued = Queue.FirstOrDefault(entry =>
            string.Equals(entry.Path, item.Path, StringComparison.OrdinalIgnoreCase));

        if (queued is not null && ReferenceEquals(queued, current))
        {
            return;
        }

        if (queued is not null)
        {
            _queue.Remove(queued);
        }
        else
        {
            queued = item.CreateQueueCopy();
        }

        var insert = current is not null && Queue.Contains(current)
            ? Queue.IndexOf(current) + 1
            : 0;
        _queue.Insert(Math.Clamp(insert, 0, Queue.Count), queued);
        queued.SetQueuedBy("Компьютер");
        _queueIndex = current is not null && Queue.Contains(current)
            ? Queue.IndexOf(current)
            : _queueIndex;
        UpdateQueueCount();
        SetPlaylistStatus(LocalizationService.Get("Playlist_StatusPlayNext"));
    }

    private void RemovePlaylistItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            RemovePlaylistItem(item);
        }
    }

    private async void AddPlaylistFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_openPlaylist is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        foreach (var extension in VideoExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        var added = false;
        foreach (var file in files)
        {
            added |= _openPlaylist.AddItem(file.Path);
        }

        if (added)
        {
            PlaylistService.Save(Playlists);
            RefreshPlaylistItems();
        }
    }

    private async void ExportPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_openPlaylist is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = _openPlaylist.Name,
        };
        picker.FileTypeChoices.Add("M3U", new List<string> { ".m3u" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await M3uService.WriteAsync(file.Path, PlaylistItems);
            PlaylistDetailSubtitle.Text = LocalizationService.Format("Playlist_ExportDone", file.Name);
        }
        catch
        {
            PlaylistDetailSubtitle.Text = LocalizationService.Get("Playlist_ExportFailed");
        }
    }

    private async void ImportPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_openPlaylist is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var paths = await M3uService.ReadAsync(file.Path);
            var added = 0;
            foreach (var path in paths)
            {
                if (_openPlaylist.AddItem(path))
                {
                    added++;
                }
            }

            if (added > 0)
            {
                PlaylistService.Save(Playlists);
                RefreshPlaylistItems();
            }
            else
            {
                PlaylistDetailSubtitle.Text = LocalizationService.Get("Playlist_ImportNoNew");
            }
        }
        catch
        {
            PlaylistDetailSubtitle.Text = LocalizationService.Get("Playlist_ImportFailed");
        }
    }

    private void ShowCreatePlaylist_Click(object sender, RoutedEventArgs e)
    {
        ShowCreatePlaylistPanel();
    }

    private void ShowCreatePlaylistPanel(IEnumerable<MediaItem>? pendingItems = null)
    {
        _pendingPlaylistItems.Clear();
        if (pendingItems is not null)
        {
            _pendingPlaylistItems.AddRange(pendingItems);
        }

        NewPlaylistName.Text = LocalizationService.Get("Playlist_Untitled");
        CreatePlaylistPanel.Visibility = Visibility.Visible;
        UpdatePlaylistsState();
        NewPlaylistName.Focus(FocusState.Programmatic);
        NewPlaylistName.SelectAll();
    }

    private void CancelCreatePlaylist_Click(object sender, RoutedEventArgs e)
    {
        CreatePlaylistPanel.Visibility = Visibility.Collapsed;
        UpdatePlaylistsState();
    }

    private void CreatePlaylist_Click(object sender, RoutedEventArgs e)
    {
        CreatePlaylistFromInput();
    }

    private void CreatePlaylistFromInput()
    {
        var name = NewPlaylistName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = LocalizationService.Get("Playlist_Untitled");
        }

        var unique = name;
        var suffix = 2;
        while (Playlists.Any(item => string.Equals(item.Name, unique, StringComparison.CurrentCultureIgnoreCase)))
        {
            unique = $"{name} ({suffix++})";
        }

        var playlist = new Playlist { Name = unique };
        foreach (var pending in _pendingPlaylistItems)
        {
            playlist.AddItem(pending.Path);
        }

        _pendingPlaylistItems.Clear();
        Playlists.Add(playlist);
        PlaylistService.Save(Playlists);
        CreatePlaylistPanel.Visibility = Visibility.Collapsed;
        UpdatePlaylistsState();
    }

    private void NewPlaylistName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CreatePlaylistFromInput();
            e.Handled = true;
        }
    }

    private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Playlist playlist)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_DeleteTitle"),
            Content = LocalizationService.Format("Playlist_DeleteMessage", playlist.Name),
            PrimaryButtonText = LocalizationService.Get("Common_Delete.Text"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            var wasOpen = ReferenceEquals(_openPlaylist, playlist);
            if (wasOpen)
            {
                PlaylistBack_Click(this, new RoutedEventArgs());
            }

            Playlists.Remove(playlist);
            PlaylistService.Save(Playlists);
            UpdatePlaylistsState();
            if (wasOpen)
            {
                PlaylistsNavItem.IsExpanded = true;
                NavView.SelectedItem = PlaylistsNavItem;
            }
        }
    }

    private void ShowPlaceholder(string title, string text)
    {
        ShowTopLevelView(PlaceholderView);
        PlaceholderTitle.Text = title;
        PlaceholderText.Text = text;
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
    }

    private void ShowSettings()
    {
        ShowTopLevelView(SettingsView);
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        ApplyLanguageDeferred(SettingsView);
        BuildSettingsCatalog();
        ShowSettingsCatalog();
    }

    private void ShowAbout()
    {
        ShowTopLevelView(AboutView);
        HomeView.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        PlaylistsView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Collapsed;
        RemoteView.Visibility = Visibility.Collapsed;
        RemoteParticipantsView.Visibility = Visibility.Collapsed;
        AboutView.Visibility = Visibility.Visible;
        AboutVersionText.Text = LocalizationService.Format("About_Version", AppInfo.DisplayVersion);
        ApplyLanguageDeferred(AboutView);
    }

    private void InitializeSettings()
    {
        _isInitializingSettings = true;
        RefreshVideoRoots();
        PopulateLanguageComboBox();
        ThemeComboBox.SelectedIndex = SettingsService.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };
        CountdownToggle.IsOn = SettingsService.CountdownEnabled;
        CountdownSecondsBox.Value = SettingsService.CountdownSeconds;
        RemoteAutoStopToggle.IsOn = SettingsService.RemoteAutoStopEnabled;
        RemoteAutoStopMinutesBox.Value = SettingsService.RemoteAutoStopMinutes;
        if (WifiSafeModeToggle is not null)
        {
            WifiSafeModeToggle.IsOn = SettingsService.WifiDirectSafeMode;
        }
        if (PersistentPinToggle is not null)
        {
            PersistentPinToggle.IsOn = SettingsService.GetSecurity().UsePersistentPin;
            UpdatePinControlsState();
        }
        LoggingToggle.IsOn = SettingsService.LoggingEnabled;
        _ = RefreshWifiDiagnosticsAsync();
        if (UpdateService.IsSupported)
        {
            CheckUpdatesToggle.IsOn = SettingsService.CheckUpdatesOnStartup;
            UpdateBadgeVisibility();
            // До первой ручной проверки показываем нейтральную подпись текущей сборки.
            _updateState = _pendingUpdate is null ? UpdateUiState.Idle : UpdateUiState.UpdateAvailable;
            RefreshUpdateStatusText();
        }
        else
        {
            // MSIX-сборка обновляется средствами Windows/Store, установщик не нужен.
            SectionUpdates.Visibility = Visibility.Collapsed;
        }
        _isInitializingSettings = false;
        ApplyTheme(SettingsService.Theme);
    }

    /// <summary>Builds the language selector from localization/manifest.json.</summary>
    private void PopulateLanguageComboBox()
    {
        if (LanguageComboBox is null)
        {
            return;
        }

        var wasInitializing = _isInitializingSettings;
        _isInitializingSettings = true;
        LanguageComboBox.Items.Clear();
        LanguageComboBox.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get("Settings_UseSystemLanguage"),
            Tag = "system",
        });

        foreach (var language in LocalizationService.Languages)
        {
            LanguageComboBox.Items.Add(new ComboBoxItem
            {
                Content = language.NativeName,
                Tag = language.Tag,
            });
        }

        SelectLanguageInComboBox(LocalizationService.CurrentLanguage);
        _isInitializingSettings = wasInitializing;
    }

    private void SelectLanguageInComboBox(string tag)
    {
        if (LanguageComboBox is null)
        {
            return;
        }

        var wasInitializing = _isInitializingSettings;
        _isInitializingSettings = true;
        LanguageComboBox.SelectedItem = !SettingsService.LanguageConfigured
            ? LanguageComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, "system", StringComparison.OrdinalIgnoreCase))
            : LanguageComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
        _isInitializingSettings = wasInitializing;
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

        // Системные кнопки окна рисуются отдельно от XAML — передаём тему явно.
        if (App.Window is MainWindow window)
        {
            window.ApplyTitleBarTheme(elementTheme);
        }

        // Кисти статуса обновлений взяты из темы — перекрашиваем после смены.
        RefreshUpdateStatusText();
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
        await LoadPreviewAsync(true);
    }

    private void OpenVideoRoot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Проводник может быть недоступен в ограниченной среде.
        }
    }

    private void RemoveVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string root)
        {
            return;
        }

        var roots = SettingsService.VideoRoots;
        roots.RemoveAll(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase));
        SettingsService.SetVideoRoots(roots);
        RefreshVideoRoots();

        // Remove matching entries locally instead of starting a full library
        // rescan while the settings card is still handling its click.
        _scanCts?.Cancel();
        var removed = _all
            .Where(item => IsPathInsideRoot(item.Path, root))
            .ToList();
        foreach (var item in removed)
        {
            _all.Remove(item);
            _itemsByPath.Remove(item.Path);
        }

        _searchSession.SetItems(_all);
        RebuildVersionCounts();
        ApplyFilter();
        if (roots.Count > 0)
        {
            StartLibraryWatchers(roots);
        }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async void OpenIndexingSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:search-indexing"));
        }
        catch
        {
            // Settings URI may be unavailable in a restricted environment.
        }
    }

    private async void RefreshLibraries_Click(object sender, RoutedEventArgs e)
    {
        // Re-index metadata in the background. A full library reload here blocks
        // the UI while thousands of preview bitmaps are re-created.
        _scanCts?.Cancel();
        _durationScanStarted = false;
        ShowScanProgress(true, 0, 0);
        await Task.Yield();
        StartDurationScan();
        if (!_scanRunning)
        {
            ShowScanProgress(false, 0, 0);
        }
    }

    /// <summary>Показывает ход индексации и в «Параметрах», и в строке статуса библиотеки.</summary>
    private void ShowScanProgress(bool active, int done, int total)
    {
        if (ScanProgressPanel is not null)
        {
            ScanProgressPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ScanProgressBar is not null)
        {
            ScanProgressBar.Value = total > 0 ? Math.Min(100, done * 100.0 / total) : 0;
        }

        if (LibraryScanProgressBar is not null)
        {
            LibraryScanProgressBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            LibraryScanProgressBar.Value = total > 0 ? Math.Min(100, done * 100.0 / total) : 0;
        }

        if (ScanProgressText is not null)
        {
            ScanProgressText.Text = total > 0
                ? LocalizationService.Format("Library_ScanningCount", done, total)
                : LocalizationService.Get("Library_Scanning");
        }

        if (StatusText is not null)
        {
            // Keep the library header showing the actual card count while
            // metadata/thumbnails are being prepared in the background.
            UpdateLibraryStatusSummary();
        }
    }

    private void UpdateLibraryStatusSummary()
    {
        if (StatusText is null)
        {
            return;
        }

        if (_all.Count == 0)
        {
            StatusText.Visibility = Visibility.Collapsed;
            return;
        }

        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = LocalizationService.ShownSummary(
            FilteredGroups.Count,
            FilteredGroups.Sum(group => group.VersionCount));
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        var language = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        try
        {
            if (string.Equals(language, "system", StringComparison.OrdinalIgnoreCase))
            {
                LocalizationService.UseSystemLanguage();
            }
            else
            {
                LocalizationService.SetLanguage(language);
            }

            LocalizeQueueControls();
            RefreshLocalizedMediaText();
            PopulateFolders();
            ApplyFilter();
            UpdateQueueCount();
            UpdateWifiDirectStateUi();
            AboutVersionText.Text = LocalizationService.Format("About_Version", AppInfo.DisplayVersion);
            BuildSettingsCatalog();
            RefreshUpdateStatusText();
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось применить язык: {ex.Message}");
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)    {
        if (_isInitializingSettings)
        {
            return;
        }

        var theme = (ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
        SettingsService.Theme = theme;
        ApplyTheme(theme);
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
        UpdateNowPlaying();
        UpdateShuffleVisual();
        UpdateRepeatVisual();
        RestoreEqualizer();
        _remoteIdleTimer.Start();
        App.Window.Closed += (_, _) => _ = HotspotService.StopAsync();
        App.Window.Activated += OnWindowActivated;
        WiFiDirectCoordinator.StateChanged += OnWiFiDirectStateChanged;
        UpdateWifiDirectStateUi();
        _ = LoadPreviewAsync();
        if (UpdateService.IsSupported && SettingsService.CheckUpdatesOnStartup)
        {
            _ = CheckForUpdatesAsync(userInitiated: false);
        }
        UpdateBadgeVisibility();

        StartupTrace.Mark("MainPage loaded");

        // Даём UI отрисовать первый кадр, затем догружаем плейлисты/очередь/пульт.
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                StartupTrace.Mark("First render");
                InitializeAfterFirstRender();
                StartupTrace.Mark("UI interactive");
                StartupTrace.DumpTimeline();

                // Холодный старт из тоста: страница уже готова к навигации.
                var pending = App.TakePendingUpdateAction();
                if (pending is not null)
                {
                    _ = HandleUpdateActionAsync(pending);
                }
            });
    }

    private void OnNetworkStatusChanged(object sender)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateRemoteNetworkState();
            if (_remoteServer is { IsRunning: true } server)
            {
                server.RefreshEndpoint(preferHotspot: _remoteDirectMode);
                _ = SetQrImageAsync(RemoteQrImage, server.QrPng);
                RemoteUrlText.Text = server.Url;
            }
        });
    }

    private async void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _remoteIdleTimer.Stop();
        _remoteClientsTimer.Stop();
        _scanCts?.Cancel();
        StopLibraryWatchers();
        SavePlaybackPosition(force: true);
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        WiFiDirectCoordinator.StateChanged -= OnWiFiDirectStateChanged;
        SettingsService.UpdateStateChanged -= OnUpdateStateChanged;
        App.UpdateActionRequested -= OnUpdateActionRequested;
        if (App.Window is not null)
        {
            App.Window.Activated -= OnWindowActivated;
        }
        VideoView.Initialized -= OnVideoViewInitialized;
        _playerWindow?.Close();
        await _player.DetachAsync();
        await HotspotService.StopAsync();
        if (_remoteServer is not null)
        {
            var server = _remoteServer;
            _remoteServer = null;
            await server.DisposeAsync();
        }
    }

    private void OnVideoViewInitialized(object? sender, InitializedEventArgs e)
    {
        _mainSwapOptions = e.SwapChainOptions;
        _player.Attach(VideoView, e.SwapChainOptions);
        UpdateTransport();
    }

    private async Task LoadPreviewAsync(bool rescan = false)
    {
        await _libraryLoadLock.WaitAsync();
        try
        {
            StopLibraryWatchers();
            _scanCts?.Cancel();
            ShowScanProgress(true, 0, 0);
            var roots = SettingsService.VideoRoots;
            if (_all.Count > 0)
            {
                StatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Text = LocalizationService.Get("Library_Reading");
                StatusText.Visibility = Visibility.Visible;
            }

            var cache = await Task.Run(() => _mediaCache.Load());
            _mediaCacheEntries.Clear();
            foreach (var entry in cache)
            {
                _mediaCacheEntries[entry.Key] = entry.Value;
            }

            var items = await Task.Run(() =>
            {
                var all = new List<MediaItem>();
                foreach (var root in roots)
                {
                    try
                    {
                        all.AddRange(EnumerateVideos(root));
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn($"Не удалось прочитать папку {root}: {ex.Message}");
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
            RebuildVersionCounts();
            _itemsByPath.Clear();
            _thumbnailCache.Clear();
            foreach (var item in _all)
            {
                _itemsByPath[item.Path] = item;
                ApplyCachedMetadata(item);
            }

            // Чистим записи об удалённых файлах и их превью, чтобы media.db не рос вечно.
            var removedThumbnails = _mediaCache.Prune(_itemsByPath.Keys);
            foreach (var thumbnail in removedThumbnails)
            {
                TryDeleteFile(thumbnail);
            }

            // The library is indexed when a folder is added and by the explicit
            // refresh action. Normal startup must not rescan thousands of files.
            var needsMetadataScan = rescan;

            _searchSession.SetItems(_all);
            PopulateFolders();
            ApplyFilter();
            RestorePlaybackState();
            if (needsMetadataScan)
            {
                _durationScanStarted = false;
                StartDurationScan();
            }
            else
            {
                ShowScanProgress(false, 0, 0);
            }

            StartLibraryWatchers(roots);
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Format("Library_ReadFailed", ex.Message);
            StatusText.Visibility = Visibility.Visible;
            LogService.Warn($"Ошибка чтения библиотеки: {ex}");
        }
        finally
        {
            _libraryLoadLock.Release();
        }
    }

    private bool HasValidCache(MediaItem item) =>
        _mediaCacheEntries.TryGetValue(item.Path, out var cached) &&
        cached.ModifiedUtcTicks == item.ModifiedUtc.Ticks &&
        cached.SizeBytes == item.SizeBytes;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Превью — вспомогательный файл.
        }
    }

    private void StartLibraryWatchers(IEnumerable<string> roots)
    {
        StopLibraryWatchers();
        if (_libraryWatchTimer is null)
        {
            _libraryWatchTimer = DispatcherQueue.CreateTimer();
            _libraryWatchTimer.Interval = TimeSpan.FromMilliseconds(1200);
            _libraryWatchTimer.IsRepeating = false;
            _libraryWatchTimer.Tick += (_, _) => _ = ApplyPendingLibraryChangesAsync();
        }

        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += (_, e) => QueueLibraryPath(e.FullPath);
                watcher.Changed += (_, e) => QueueLibraryPath(e.FullPath);
                watcher.Deleted += (_, e) => QueueLibraryPath(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    QueueLibraryPath(e.OldFullPath);
                    QueueLibraryPath(e.FullPath);
                };
                watcher.Error += (_, e) =>
                    LogService.Warn($"FileSystemWatcher {root}: {e.GetException().Message}");
                _libraryWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось включить автообновление {root}: {ex.Message}");
            }
        }
    }

    private void StopLibraryWatchers()
    {
        _libraryWatchTimer?.Stop();
        foreach (var watcher in _libraryWatchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch
            {
                // Освобождение watcher не должно мешать закрытию окна.
            }
        }

        _libraryWatchers.Clear();
        lock (_pendingLibrarySync)
        {
            _pendingLibraryPaths.Clear();
        }
    }

    private void QueueLibraryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !VideoExtensions.Contains(Path.GetExtension(path)))
        {
            return;
        }

        lock (_pendingLibrarySync)
        {
            _pendingLibraryPaths.Add(path);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_libraryWatchTimer is null)
            {
                return;
            }

            _libraryWatchTimer.Stop();
            _libraryWatchTimer.Start();
        });
    }

    private async Task ApplyPendingLibraryChangesAsync()
    {
        string[] paths;
        lock (_pendingLibrarySync)
        {
            if (_pendingLibraryPaths.Count == 0)
            {
                return;
            }

            paths = _pendingLibraryPaths.ToArray();
            _pendingLibraryPaths.Clear();
        }

        var changed = new List<MediaItem>();
        var removedAny = false;
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var item = MediaItem.FromPath(path);
                if (_mediaCacheEntries.TryGetValue(path, out var cached) &&
                    cached.ModifiedUtcTicks == item.ModifiedUtc.Ticks &&
                    cached.SizeBytes == item.SizeBytes)
                {
                    ApplyCachedMetadata(item);
                }

                _itemsByPath[path] = item;
                _thumbnailCache.Remove(path);
                changed.Add(item);
            }
            else if (_itemsByPath.Remove(path))
            {
                _mediaCacheEntries.TryRemove(path, out _);
                _thumbnailCache.Remove(path);
                removedAny = true;
            }
        }

        if (changed.Count == 0 && !removedAny)
        {
            return;
        }

        _all.Clear();
        _all.AddRange(_itemsByPath.Values
            .OrderBy(item => item.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase));
        _searchSession.SetItems(_all);
        PopulateFolders();
        ApplyFilter();
        StatusText.Text = LocalizationService.Format("Library_AutoUpdated", paths.Length);
        StatusText.Visibility = Visibility.Visible;

        if (removedAny)
        {
            foreach (var thumbnail in _mediaCache.Prune(_itemsByPath.Keys))
            {
                TryDeleteFile(thumbnail);
            }
        }

        await RefreshChangedMetadataAsync(changed).ConfigureAwait(false);
    }

    private async Task RefreshChangedMetadataAsync(IReadOnlyList<MediaItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        using var metadata = new MediaMetadataService();
        foreach (var item in items)
        {
            try
            {
                var info = metadata.Read(item.Path);
                var thumbnail = await ThumbnailService
                    .TryCreateAsync(item.Path, CancellationToken.None)
                    .ConfigureAwait(false);
                var record = new CachedMedia(
                    item.ModifiedUtc.Ticks,
                    item.SizeBytes,
                    info.DurationMs,
                    info.Width,
                    info.Height,
                    thumbnail);
                _mediaCacheEntries[item.Path] = record;
                _mediaCache.SaveBatch(new[]
                {
                    (item.Path, record, DateTime.UtcNow.Ticks),
                });
                DispatcherQueue.TryEnqueue(() => ApplyMetadata(item.Path, record));
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось обновить метаданные {item.Path}: {ex.Message}");
            }
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
        FolderFilter.Items.Add(LocalizationService.AllFoldersLabel);
        foreach (var group in FolderGroups)
        {
            FolderFilter.Items.Add(group.Name);
        }

        FolderFilter.SelectedIndex = 0;
        _isPopulatingFolders = false;
        PopulateFolderRoots();
    }

    private void PopulateFolderRoots()
    {
        if (FolderRootsPanel is null)
        {
            return;
        }

        FolderRootsPanel.Children.Clear();
        var roots = SettingsService.VideoRoots
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var root in roots)
        {
            var entries = BuildFolderEntries(root);
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = root;
            }

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Padding = new Thickness(4, 2, 4, 2),
            };
            header.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 18,
                Glyph = "\uE8B7",
                VerticalAlignment = VerticalAlignment.Center,
            });

            var headerText = new StackPanel { Spacing = 1 };
            headerText.Children.Add(new TextBlock
            {
                Text = name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            headerText.Children.Add(new TextBlock
            {
                Text = root,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            header.Children.Add(headerText);

            var content = new StackPanel
            {
                Padding = new Thickness(12, 4, 12, 8),
                Spacing = 4,
            };

            if (entries.Count == 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Get("Library_FolderEmpty"),
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                });
            }
            else
            {
                var items = new ItemsControl
                {
                    ItemsSource = entries,
                    ItemTemplate = (DataTemplate)Resources["FolderEntryCardTemplate"],
                    ItemsPanel = (ItemsPanelTemplate)Resources["FolderItemsPanel"],
                };
                content.Children.Add(items);
            }

            var expanded = _folderExpandedStates.TryGetValue(root, out var saved) && saved;
            var expander = new Expander
            {
                Header = header,
                Content = content,
                IsExpanded = expanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };
            expander.Expanding += (_, _) => _folderExpandedStates[root] = true;
            expander.Collapsed += (_, _) => _folderExpandedStates[root] = false;
            FolderRootsPanel.Children.Add(expander);
        }
    }

    private List<FolderEntry> BuildFolderEntries(string folderPath)
    {
        var entries = new List<FolderEntry>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(folderPath))
            {
                var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
                var count = _all.Count(item =>
                    item.Path.StartsWith(
                        directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase));
                entries.Add(new FolderEntry(name, directory, isFolder: true, count));
            }

            foreach (var file in Directory.EnumerateFiles(folderPath)
                         .Where(path => VideoExtensions.Contains(Path.GetExtension(path))))
            {
                var item = _all.FirstOrDefault(candidate =>
                               string.Equals(candidate.Path, file, StringComparison.OrdinalIgnoreCase))
                           ?? MediaItem.FromPath(file);
                entries.Add(new FolderEntry(
                    Path.GetFileNameWithoutExtension(file),
                    file,
                    isFolder: false,
                    count: 1,
                    item)
                {
                    Thumbnail = LoadThumbnail(item.ThumbnailPath),
                });
            }
        }
        catch
        {
            // Папка может быть недоступна или удалена.
        }

        var ordered = entries
            .OrderBy(entry => entry.IsFolder ? 0 : 1);
        if (_folderSortMode == 1)
        {
            ordered = ordered.ThenByDescending(entry =>
            {
                try
                {
                    return entry.IsFolder
                        ? Directory.GetLastWriteTimeUtc(entry.Path)
                        : entry.Item?.ModifiedUtc ?? DateTime.MinValue;
                }
                catch
                {
                    return DateTime.MinValue;
                }
            });
        }
        else
        {
            ordered = ordered.ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        return ordered.ToList();
    }

    private List<MediaItem> ResolveFolderTracks(string folderPath)
    {
        var tracks = _all.Where(item =>
            item.Path.StartsWith(
                folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)).ToList();
        if (tracks.Count == 0 && Directory.Exists(folderPath))
        {
            try
            {
                tracks = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
                    .Select(MediaItem.FromPath)
                    .ToList();
            }
            catch
            {
                // Недоступная папка обрабатывается как пустая.
            }
        }

        return tracks
            .OrderBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void PlayAllFolders_Click(object sender, RoutedEventArgs e)
    {
        var tracks = SettingsService.VideoRoots
            .Where(Directory.Exists)
            .SelectMany(ResolveFolderTracks)
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (tracks.Count == 0)
        {
            BarSubtitle.Text = LocalizationService.Get("Library_NoVideosConnected");
            return;
        }

        _isRestoringQueue = true;
        _queue.Clear();
        _queueRequesters.Clear();
        foreach (var item in tracks)
        {
            var entry = item.CreateQueueCopy();
            _queue.Add(entry);
            _queueRequesters[entry] = null;
            entry.SetQueuedBy("Компьютер");
        }

        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(0);
    }

    private void FolderBack_Click(object sender, RoutedEventArgs e) => FolderBackToRoots();

    private void PlayFolderDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_folderDetailPath is not null)
        {
            PlayFolderByPath(_folderDetailPath);
        }
    }

    private void PlayFolderByPath(string folderPath)
    {
        var tracks = ResolveFolderTracks(folderPath);
        if (tracks.Count == 0)
        {
            BarSubtitle.Text = LocalizationService.Get("Library_NoVideosInFolder");
            return;
        }

        _isRestoringQueue = true;
        _queue.Clear();
        _queueRequesters.Clear();
        foreach (var item in tracks)
        {
            var entry = item.CreateQueueCopy();
            _queue.Add(entry);
            _queueRequesters[entry] = null;
            entry.SetQueuedBy("Компьютер");
        }

        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(0);
    }

    private void AddFolderToQueue(FolderEntry entry)
    {
        var tracks = ResolveFolderTracks(entry.Path);
        foreach (var track in tracks)
        {
            AddToQueue(track);
        }

        BarSubtitle.Text = tracks.Count == 0
            ? LocalizationService.Get("Library_NoVideosInFolder")
            : LocalizationService.Format("Library_QueueAddedCount", tracks.Count);
    }

    private void QueueFolderNext(FolderEntry entry)
    {
        var tracks = ResolveFolderTracks(entry.Path);
        var current = _queueIndex >= 0 && _queueIndex < Queue.Count
            ? Queue[_queueIndex]
            : null;
        var insert = current is not null && Queue.Contains(current)
            ? Queue.IndexOf(current) + 1
            : 0;
        foreach (var item in tracks)
        {
            var queueEntry = item.CreateQueueCopy();
            _queue.Insert(Math.Clamp(insert, 0, Queue.Count), queueEntry);
            _queueRequesters[queueEntry] = null;
            queueEntry.SetQueuedBy("Компьютер");
            insert++;
        }

        _queueIndex = current is null ? _queueIndex : Queue.IndexOf(current);
        UpdateQueueCount();
        SaveQueue();
    }

    private void FolderEntry_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetFolderCardOverlay(sender, true);
    }

    private void FolderEntry_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetFolderCardOverlay(sender, false);
    }

    private static void SetFolderCardOverlay(object sender, bool visible)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        if (FindElementByName(root, "FolderCardOverlay") is Grid overlay)
        {
            overlay.Opacity = visible ? 1 : 0;
        }

        foreach (var name in new[] { "FolderCardCheck", "FolderCardPlay", "FolderCardMore" })
        {
            if (FindElementByName(root, name) is Control control)
            {
                control.IsHitTestVisible = visible;
            }
        }
    }

    private void FolderEntry_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not FolderEntry entry)
        {
            return;
        }

        if (entry.IsFolder)
        {
            OpenFolderDetail(entry.Path);
        }
        else if (entry.Item is not null)
        {
            PlayPlaylistItem(entry.Item);
        }
    }

    private void FolderEntry_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is FolderEntry { IsVideo: true, Item: not null } entry)
        {
            PlayPlaylistItem(entry.Item);
        }
        else if ((sender as FrameworkElement)?.DataContext is FolderEntry { IsFolder: true } folder)
        {
            OpenFolderDetail(folder.Path);
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void FolderEntryPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderEntry entry)
        {
            return;
        }

        if (entry.IsFolder)
        {
            PlayFolderByPath(entry.Path);
        }
        else if (entry.Item is not null)
        {
            PlayPlaylistItem(entry.Item);
        }
    }

    private void FolderEntryMore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderEntry entry)
        {
            return;
        }

        var anchor = FindButtonAnchor(sender);
        ShowFolderEntryMenu(anchor, entry, new Point(anchor.ActualWidth, 0));
    }

    private static FrameworkElement FindButtonAnchor(object sender)
    {
        if (sender is FrameworkElement element)
        {
            var parent = VisualTreeHelper.GetParent(element);
            while (parent is not null && parent is not Grid)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is Grid grid && FindElementByName(grid, "FolderCardOverlay") is not null)
            {
                return grid;
            }
        }

        return sender as FrameworkElement ?? (FrameworkElement)App.Window.Content;
    }

    private void FolderEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement row && row.DataContext is FolderEntry entry)
        {
            ShowFolderEntryMenu(row, entry, e.GetPosition(row));
            e.Handled = true;
        }
    }

    private void ShowFolderEntryMenu(FrameworkElement anchor, FolderEntry entry, Point position)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem(
            "ContextMenu_Play.Text",
            "\uE768",
            () =>
            {
                if (entry.IsFolder)
                {
                    PlayFolderByPath(entry.Path);
                }
                else if (entry.Item is not null)
                {
                    PlayPlaylistItem(entry.Item);
                }
            }));
        flyout.Items.Add(CreateMenuItem(
            "Menu_PlayNext.Text",
            "\uED35",
            () =>
            {
                if (entry.IsFolder)
                {
                    QueueFolderNext(entry);
                }
                else if (entry.Item is not null)
                {
                    EnqueueItemNext(entry.Item);
                }
            }));

        var addTo = new MenuFlyoutSubItem
        {
            Text = LocalizationService.Get("ContextMenu_AddTo.Text"),
            Icon = CreateMenuIcon("\uE710"),
        };
        addTo.Items.Add(CreateMenuItem(
            "ContextMenu_Queue.Text",
            "\uE8D6",
            () =>
            {
                if (entry.IsFolder)
                {
                    AddFolderToQueue(entry);
                }
                else if (entry.Item is not null)
                {
                    AddItemToQueueFromMenu(entry.Item);
                }
            }));
        addTo.Items.Add(new MenuFlyoutSeparator());
        addTo.Items.Add(CreateMenuItem(
            "ContextMenu_NewPlaylist.Text",
            "\uE710",
            () =>
            {
                if (entry.IsFolder)
                {
                    _ = CreatePlaylistWithPathsAsync(entry.Name, ResolveFolderTracks(entry.Path).Select(item => item.Path));
                }
                else if (entry.Item is not null)
                {
                    _ = CreatePlaylistWithItemAsync(entry.Item);
                }
            }));
        foreach (var playlist in Playlists.ToList())
        {
            var target = playlist;
            addTo.Items.Add(CreateMenuItem(
                target.Name,
                "\uE8D6",
                () =>
                {
                    if (entry.IsFolder)
                    {
                        foreach (var path in ResolveFolderTracks(entry.Path).Select(item => item.Path))
                        {
                            target.AddItem(path);
                        }

                        PlaylistService.Save(Playlists);
                    }
                    else if (entry.Item is not null)
                    {
                        AddItemToPlaylist(target, entry.Item);
                    }
                },
                localize: false));
        }

        flyout.Items.Add(addTo);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("ContextMenu_Select.Text", "\uE762", () => SetFolderCardOverlay(anchor, true)));
        flyout.ShowAt(anchor, new FlyoutShowOptions { Position = position });
    }

    private async Task CreatePlaylistWithPathsAsync(string baseName, IEnumerable<string> paths)
    {
        var nameBox = new TextBox { Text = baseName };
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_New"),
            Content = nameBox,
            PrimaryButtonText = LocalizationService.Get("Common_Create"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(nameBox.Text) ? LocalizationService.Get("Playlist_New") : nameBox.Text.Trim();
        var playlist = new Playlist { Name = name };
        foreach (var path in paths)
        {
            playlist.AddItem(path);
        }

        Playlists.Add(playlist);
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
    }

    private void OpenFolderDetail(string path)
    {
        ShowTopLevelView(LibraryPanel, keepLibraryDetail: true);
        _folderDetailPath = path;
        FoldersHomePanel.Visibility = Visibility.Collapsed;
        FolderDetailView.Visibility = Visibility.Visible;
        RefreshFolderDetail();
        AnimateFolderTransition(FolderDetailView, FolderDetailTransform, 24);
        LocalizationService.Apply(FolderDetailView, SettingsService.Language);
        ApplyLanguageDeferred(FolderDetailView);
    }

    private void FolderBackToRoots()
    {
        _folderDetailPath = null;
        FoldersHomePanel.Visibility = Visibility.Visible;
        FolderDetailView.Visibility = Visibility.Collapsed;
        AnimateFolderTransition(FoldersHomePanel, FoldersHomeTransform, -24);
    }

    private static void AnimateFolderTransition(
        FrameworkElement view,
        TranslateTransform transform,
        double fromX)
    {
        if (!new UISettings().AnimationsEnabled)
        {
            view.Opacity = 1;
            transform.X = 0;
            return;
        }

        view.Opacity = 0;
        transform.X = fromX;
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        var opacity = new DoubleAnimation
        {
            To = 1,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(opacity, view);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var move = new DoubleAnimation
        {
            To = 0,
            Duration = duration,
            EasingFunction = ease,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(move, transform);
        Storyboard.SetTargetProperty(move, "X");

        storyboard.Children.Add(opacity);
        storyboard.Children.Add(move);
        storyboard.Begin();
    }

    private void RefreshFolderDetail()
    {
        if (_folderDetailPath is null)
        {
            return;
        }

        var name = Path.GetFileName(_folderDetailPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _folderDetailPath;
        }

        FolderDetailTitle.Text = name;
        var root = SettingsService.VideoRoots.FirstOrDefault(candidate =>
            _folderDetailPath.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            _folderDetailPath.StartsWith(
                candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
        var crumbs = new List<FolderCrumb>();
        if (!string.IsNullOrWhiteSpace(root))
        {
            var rootLabel = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            crumbs.Add(new FolderCrumb(
                string.IsNullOrWhiteSpace(rootLabel) ? root : rootLabel,
                root));

            var relative = Path.GetRelativePath(root, _folderDetailPath);
            var current = root;
            foreach (var segment in relative.Split(
                         Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                crumbs.Add(new FolderCrumb(segment, current));
            }
        }
        else
        {
            crumbs.Add(new FolderCrumb(_folderDetailPath, _folderDetailPath));
        }

        FolderBreadcrumb.ItemsSource = crumbs;
        FolderDetailGrid.ItemsSource = BuildFolderEntries(_folderDetailPath);
    }

    private void FolderBreadcrumb_ItemClicked(
        BreadcrumbBar sender,
        BreadcrumbBarItemClickedEventArgs args)
    {
        if (FolderBreadcrumb.ItemsSource is not IReadOnlyList<FolderCrumb> crumbs ||
            args.Index < 0 ||
            args.Index >= crumbs.Count - 1)
        {
            return;
        }

        var target = crumbs[args.Index];
        if (SettingsService.VideoRoots.Any(candidate =>
                string.Equals(
                    candidate.TrimEnd(Path.DirectorySeparatorChar),
                    target.Path.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)))
        {
            FolderBackToRoots();
            return;
        }

        _folderDetailPath = target.Path;
        RefreshFolderDetail();
    }

    private void FolderSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderSortBox is null)
        {
            return;
        }

        _folderSortMode = FolderSortBox.SelectedIndex;
        PopulateFolderRoots();
    }

    private void FolderDetailSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderDetailSortBox is null)
        {
            return;
        }

        _folderSortMode = FolderDetailSortBox.SelectedIndex;
        RefreshFolderDetail();
    }    private void ApplyFilter()
    {
        var folder = FolderFilter.SelectedItem as string;
        var folderActive = !string.IsNullOrWhiteSpace(folder) && !LocalizationService.IsAllFolders(folder);

        IEnumerable<MediaItem> result = _all;
        if (folderActive)
        {
            result = result.Where(item =>
                string.Equals(item.Folder, folder, StringComparison.CurrentCultureIgnoreCase));
        }

        var sort = _sortMode;
        result = sort switch
        {
            "date" => result
                .OrderByDescending(item => item.ModifiedUtc)
                .ThenBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
            "folder" => result
                .OrderBy(item => item.Folder, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
            _ => result
                .OrderBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase),
        };

        ShowFiltered(result);
    }

    private void ShowFiltered(IEnumerable<MediaItem> result)
    {
        if (_mediaCacheEntries.Count == 0)
        {
            foreach (var entry in _mediaCache.Load())
            {
                _mediaCacheEntries[entry.Key] = entry.Value;
            }
        }

        var groups = MediaGroupingService.Group(result);
        var collection = new ObservableCollection<MediaGroup>();

        _visibleGroupsByPath.Clear();
        foreach (var group in groups)
        {
            foreach (var version in group.Versions)
            {
                _visibleGroupsByPath[version.Path] = group;
            }

            ApplyCachedMetadata(group);
            collection.Add(group);
        }

        // Populate only the first screen eagerly; the rest are loaded as cards
        // are realized, which keeps startup and filtering responsive.
        foreach (var group in collection.Take(24))
        {
            _mediaCacheEntries.TryGetValue(group.Best.Path, out var cached);
            var thumbnailPath = cached?.ThumbnailPath;
            if (!string.IsNullOrWhiteSpace(thumbnailPath) && !File.Exists(thumbnailPath))
            {
                thumbnailPath = Path.ChangeExtension(thumbnailPath, ".bmp");
            }

            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                thumbnailPath = ResolveThumbnailPath(group.Best.Path, thumbnailPath);
            }

            if (!string.IsNullOrWhiteSpace(thumbnailPath))
            {
                group.Thumbnail = LoadThumbnail(thumbnailPath);
            }
            else
            {
                _ = CreateThumbnailForGroupAsync(group);
            }
        }

        _filteredGroups = collection;
        if (LibraryList is not null)
        {
            LibraryList.ItemsSource = _filteredGroups;
        }

        CountText.Text = SongCount(FilteredGroups.Count);
        StatusText.Text = LocalizationService.ShownSummary(
            FilteredGroups.Count,
            FilteredGroups.Sum(group => group.VersionCount));
        StatusText.Visibility = Visibility.Visible;
        UpdateLibraryEmptyState();
        RefreshRecentMetadata();
        UpdateLibrarySelectionBar();
    }

    private void StartDurationScan()
    {
        if (_durationScanStarted || _all.Count == 0)
        {
            return;
        }

        var groups = MediaGroupingService
            .Group(_all)
            .Where(NeedsMetadataScan)
            .ToList();
        if (groups.Count == 0)
        {
            ShowScanProgress(false, 0, 0);
            return;
        }

        _durationScanStarted = true;
        _scanRunning = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        ShowScanProgress(true, 0, 0);
        _ = ScanMetadataAsync(groups, _scanCts.Token);
    }

    private bool NeedsMetadataScan(MediaGroup group)
    {
        var item = group.Best;
        if (!_mediaCacheEntries.TryGetValue(item.Path, out var cached) ||
            cached.ModifiedUtcTicks != item.ModifiedUtc.Ticks ||
            cached.SizeBytes != item.SizeBytes)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(cached.ThumbnailPath) ||
               ResolveThumbnailPath(item.Path, cached.ThumbnailPath) is null;
    }

    private async Task ScanMetadataAsync(IReadOnlyList<MediaGroup> groups, CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var total = groups.Count;
        var completed = 0;
        var ui = DispatcherQueue;
        using var metadata = new MediaMetadataService();

        try
        {
            await Parallel.ForEachAsync(
                groups,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4,
                    CancellationToken = cancellationToken,
                },
                async (group, token) =>
                {
                    var item = group.Best;
                    var path = item.Path;
                    var cachedValid = _mediaCacheEntries.TryGetValue(path, out var cached) &&
                        cached.ModifiedUtcTicks == item.ModifiedUtc.Ticks &&
                        cached.SizeBytes == item.SizeBytes;

                    MediaMetadata metadataInfo;
                    if (cachedValid && (cached!.DurationMs > 0 || cached.Width > 0))
                    {
                        metadataInfo = new MediaMetadata(cached.DurationMs, cached.Width, cached.Height);
                    }
                    else
                    {
                        try
                        {
                            metadataInfo = await Task.Run(() => metadata.Read(path, token), token)
                                .WaitAsync(TimeSpan.FromSeconds(10), token);
                        }
                        catch (TimeoutException)
                        {
                            metadataInfo = new MediaMetadata(0, 0, 0);
                        }
                    }

                    var thumbnail = cachedValid ? cached!.ThumbnailPath : null;
                    if (string.IsNullOrWhiteSpace(thumbnail) || !File.Exists(thumbnail))
                    {
                        try
                        {
                            thumbnail = await ThumbnailService
                                .TryCreateAsync(path, token)
                                .WaitAsync(TimeSpan.FromSeconds(10), token);
                        }
                        catch (TimeoutException)
                        {
                            thumbnail = null;
                        }
                    }

                    var record = new CachedMedia(
                        item.ModifiedUtc.Ticks,
                        item.SizeBytes,
                        metadataInfo.DurationMs,
                        metadataInfo.Width,
                        metadataInfo.Height,
                        thumbnail);

                    _mediaCacheEntries[path] = record;
                    _pendingCacheWrites.Enqueue((path, record, DateTime.UtcNow.Ticks));
                    if (Interlocked.Increment(ref _pendingCacheCount) >= 200)
                    {
                        FlushCacheWrites();
                    }

                    ui.TryEnqueue(() => ApplyMetadata(path, record));

                    var done = Interlocked.Increment(ref completed);
                    if (done == total || done % 10 == 0)
                    {
                        ui.TryEnqueue(() => ShowScanProgress(true, done, total));
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Переиндексация отменена — это нормально при повторном скане.
        }
        catch (Exception ex)
        {
            LogService.Warn($"Ошибка сканирования метаданных: {ex}");
        }
        finally
        {
            FlushCacheWrites();
            _scanRunning = false;
            ui.TryEnqueue(() =>
            {
                ShowScanProgress(false, total, total);
                RefreshRecentMetadata();
                ApplyFilter();
            });
        }
    }

    private void FlushCacheWrites()
    {
        lock (_cacheFlushLock)
        {
            var batch = new List<(string Path, CachedMedia Media, long SeenUtc)>();
            while (_pendingCacheWrites.TryDequeue(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0)
            {
                Interlocked.Exchange(ref _pendingCacheCount, 0);
                return;
            }

            _mediaCache.SaveBatch(batch);
            Interlocked.Exchange(ref _pendingCacheCount, 0);
        }
    }

    private void ApplyCachedMetadata(MediaItem item)
    {
        if (_mediaCacheEntries.TryGetValue(item.Path, out var cached) &&
            cached.ModifiedUtcTicks == item.ModifiedUtc.Ticks &&
            cached.SizeBytes == item.SizeBytes)
        {
            item.ApplyMetadata(cached.DurationMs, cached.Width, cached.Height, cached.ThumbnailPath);
        }
    }

    private void ApplyCachedMetadata(MediaGroup group)
    {
        var item = group.Best;
        if (_mediaCacheEntries.TryGetValue(item.Path, out var cached) && cached.DurationMs > 0)
        {
            group.SetDuration(cached.DurationMs);
        }
    }

    private void LibraryList_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is MediaGroup group)
        {
            EnsureThumbnail(group);
        }
    }

    private void MediaThumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureThumbnail((sender as FrameworkElement)?.DataContext as MediaGroup);
    }

    private void MediaThumbnail_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        EnsureThumbnail(sender.DataContext as MediaGroup);
    }

    private void EnsureThumbnail(FrameworkElement? element)
    {
        EnsureThumbnail(element?.DataContext as MediaGroup);
    }

    private void EnsureThumbnail(MediaGroup? group)
    {
        if (group is null || group.Thumbnail is not null)
        {
            return;
        }

        _mediaCacheEntries.TryGetValue(group.Best.Path, out var cached);
        var thumbnail = ResolveThumbnailPath(group.Best.Path, cached?.ThumbnailPath);
        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            group.Thumbnail = LoadThumbnail(thumbnail);
        }
        else
        {
            _ = CreateThumbnailForGroupAsync(group);
        }

        if (group.Best.DurationMs <= 0)
        {
            _ = EnrichMediaAsync(group);
        }
    }

    private async Task EnrichMediaAsync(MediaGroup group)
    {
        if (!_metadataLoading.Add(group.Best.Path))
        {
            return;
        }

        try
        {
            using var metadata = new MediaMetadataService();
            var path = group.Best.Path;
            var info = await Task.Run(() => metadata.Read(path, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8));
            var thumbnailPath = await ThumbnailService
                .TryCreateAsync(path, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(8));

            DispatcherQueue.TryEnqueue(() =>
            {
                group.Best.ApplyMetadata(info.DurationMs, info.Width, info.Height, thumbnailPath);
                group.SetDuration(info.DurationMs);
                var resolved = ResolveThumbnailPath(path, thumbnailPath);
                if (resolved is not null)
                {
                    group.Thumbnail = LoadThumbnail(resolved);
                }
            });
        }
        catch
        {
            // Unsupported or inaccessible file: leave placeholders and move on.
        }
        finally
        {
            _metadataLoading.Remove(group.Best.Path);
        }
    }

    private async Task CreateThumbnailForGroupAsync(MediaGroup group)
    {
        if (!_thumbnailLoading.Add(group.Best.Path))
        {
            return;
        }

        try
        {
            var created = await Task.Run(() => ThumbnailService.TryCreateAsync(
                group.Best.Path,
                CancellationToken.None));
            if (string.IsNullOrWhiteSpace(created))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (group.Thumbnail is null)
                {
                    group.Thumbnail = LoadThumbnail(created);
                }
            });
        }
        catch
        {
            // A missing preview is not fatal.
        }
        finally
        {
            _thumbnailLoading.Remove(group.Best.Path);
        }
    }

    private static string? ResolveThumbnailPath(string videoPath, string? cachedPath)
    {
        if (!string.IsNullOrWhiteSpace(cachedPath))
        {
            if (File.Exists(cachedPath))
            {
                return cachedPath;
            }

            var cachedBmp = Path.ChangeExtension(cachedPath, ".bmp");
            if (File.Exists(cachedBmp))
            {
                return cachedBmp;
            }
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(videoPath))) + ".jpg";
        var candidate = Path.Combine(AppStorage.PathFor("Thumbnails"), hash);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var bmpCandidate = Path.ChangeExtension(candidate, ".bmp");
        if (File.Exists(bmpCandidate))
        {
            return bmpCandidate;
        }

        // Recovery path for installations where AppStorage selected a different
        // writable folder than the package LocalState used for older previews.
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packages = Path.Combine(local, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var package in Directory.EnumerateDirectories(packages))
                {
                    var recovered = Path.Combine(package, "LocalState", "Thumbnails", hash);
                    if (File.Exists(recovered))
                    {
                        return recovered;
                    }

                    var recoveredBmp = Path.ChangeExtension(recovered, ".bmp");
                    if (File.Exists(recoveredBmp))
                    {
                        return recoveredBmp;
                    }
                }
            }

            var portable = Path.Combine(local, "TempoKaraoke", "Thumbnails", hash);
            if (File.Exists(portable))
            {
                return portable;
            }
        }
        catch
        {
            // Cache recovery is best-effort.
        }

        return null;
    }

    private void ApplyMetadata(string path, CachedMedia record)
    {
        if (_itemsByPath.TryGetValue(path, out var item))
        {
            item.ApplyMetadata(record.DurationMs, record.Width, record.Height, record.ThumbnailPath);
        }

        if (_visibleGroupsByPath.TryGetValue(path, out var group))
        {
            group.SetDuration(record.DurationMs);
            group.Thumbnail = LoadThumbnail(ResolveThumbnailPath(path, record.ThumbnailPath));
        }
    }

    private ImageSource? LoadThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            LogService.Warn($"Thumbnail missing: {path}");
            return null;
        }

        if (_thumbnailCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 320 };
            try
            {
                bitmap.UriSource = new Uri(path);
                _thumbnailCache[path] = bitmap;
                return bitmap;
            }
            catch
            {
                // Fall through to the stream decoder.
            }
        }

        var image = new BitmapImage { DecodePixelWidth = 320 };
        _thumbnailCache[path] = image;
        _ = LoadThumbnailAsync(image, path);
        return image;
    }

    private static async Task LoadThumbnailAsync(BitmapImage image, string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var header = new byte[2];
            _ = await file.ReadAsync(header.AsMemory(0, header.Length));
            file.Position = 0;
            using var stream = file.AsRandomAccessStream();

            if (header[0] == (byte)'B' && header[1] == (byte)'M')
            {
                // Windows Shell may hand back a BMP stream even when the cache
                // file has a .jpg extension. Decode it through WIC first.
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                using var bitmap = await decoder.GetSoftwareBitmapAsync();
                using var converted = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId,
                    converted);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                converted.Seek(0);
                await image.SetSourceAsync(converted);
            }
            else
            {
                await image.SetSourceAsync(stream);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Thumbnail decode failed: {path}: {ex.Message}");
        }
    }

    private static string SongCount(int count) => LocalizationService.SongCount(count);

    private void UpdateLibraryEmptyState()
    {
        if (LibraryEmptyPanel is null)
        {
            return;
        }

        var isEmpty = FilteredGroups.Count == 0;
        LibraryEmptyPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!isEmpty)
        {
            return;
        }

        var hasQuery = SearchResultsView is not null && SearchResultsView.Visibility == Visibility.Visible;
        LibraryEmptyText.Text = _all.Count == 0
            ? LocalizationService.Get("Library_Empty")
            : hasQuery ? LocalizationService.Get("Library_NothingFound") : LocalizationService.Get("Library_NoVideos");
        LibraryEmptyHint.Text = _all.Count == 0
            ? LocalizationService.Get("Library_AddFolderHint")
            : hasQuery ? LocalizationService.Get("Library_CheckQuery") : LocalizationService.Get("Library_NoVideoFilesInFolder");
        LibraryEmptyResetButton.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetSearch_Click(object sender, RoutedEventArgs e)
    {
        QuickSearchBox.Text = string.Empty;
        if (FolderFilter.SelectedIndex > 0)
        {
            FolderFilter.SelectedIndex = 0;
        }

        ShowLibrary();
        GlobalSearchButton.Focus(FocusState.Programmatic);
    }

    // --- Быстрый поиск по Ctrl+F ---

    private void QuickSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenQuickSearch();
    }

    private void GlobalSearchButton_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickSearch();
    }

    private void OpenQuickSearch()
    {
        var wasOpen = QuickSearchOverlay.Visibility == Visibility.Visible;
        QuickSearchOverlay.Visibility = Visibility.Visible;
        if (!wasOpen)
        {
            QuickSearchBox.Text = string.Empty;
            PopulateQuickSearchFilters();
            RefreshQuickSearchResults(string.Empty);
        }

        _ = FocusQuickSearchBoxAsync();
    }

    /// <summary>
    /// Ставит курсор в поле быстрого поиска. Фокус выставляется после
    /// завершения обработки клика (иначе кнопка в шапке забирает его назад),
    /// а внутренний TextBox шаблона AutoSuggestBox появляется только после
    /// первого layout — поэтому несколько попыток с короткой паузой.
    /// </summary>
    private async Task FocusQuickSearchBoxAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (QuickSearchOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            if (FindElementByName(QuickSearchBox, "TextBox") is TextBox textBox)
            {
                textBox.Focus(FocusState.Keyboard);
                textBox.SelectAll();
                if (ReferenceEquals(FocusManager.GetFocusedElement(textBox.XamlRoot), textBox))
                {
                    return;
                }
            }

            await Task.Delay(30);
        }
    }

    private void PopulateQuickSearchFilters()
    {
        _populatingQuickSearchFilters = true;
        try
        {
            var selectedFolder = (QuickSearchFolderFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            QuickSearchFolderFilter.Items.Clear();
            QuickSearchFolderFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_AllFolders"),
                Tag = null,
            });
            foreach (var folder in _all
                         .Select(item => item.Folder)
                         .Where(folder => !string.IsNullOrWhiteSpace(folder))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase))
            {
                QuickSearchFolderFilter.Items.Add(new ComboBoxItem
                {
                    Content = folder,
                    Tag = folder,
                });
            }

            QuickSearchFolderFilter.SelectedIndex = FindFilterIndex(
                QuickSearchFolderFilter, selectedFolder);

            var selectedFormat = (QuickSearchFormatFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            QuickSearchFormatFilter.Items.Clear();
            QuickSearchFormatFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_AllFormats"),
                Tag = null,
            });
            foreach (var format in new[] { "mkv", "mp4", "avi", "mov", "webm", "m4v", "wmv" })
            {
                QuickSearchFormatFilter.Items.Add(new ComboBoxItem
                {
                    Content = format.ToUpperInvariant(),
                    Tag = format,
                });
            }

            QuickSearchFormatFilter.SelectedIndex = FindFilterIndex(
                QuickSearchFormatFilter, selectedFormat);

            var selectedDuration = (QuickSearchDurationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            QuickSearchDurationFilter.Items.Clear();
            QuickSearchDurationFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_AnyDuration"),
                Tag = "any",
            });
            QuickSearchDurationFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_ShortDuration"),
                Tag = "short",
            });
            QuickSearchDurationFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_MediumDuration"),
                Tag = "medium",
            });
            QuickSearchDurationFilter.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Search_LongDuration"),
                Tag = "long",
            });
            QuickSearchDurationFilter.SelectedIndex = FindFilterIndex(
                QuickSearchDurationFilter, selectedDuration);
        }
        finally
        {
            _populatingQuickSearchFilters = false;
        }
    }

    private static int FindFilterIndex(ComboBox combo, string? tag)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private void QuickSearchFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_populatingQuickSearchFilters)
        {
            RefreshQuickSearchResults(QuickSearchBox.Text);
        }
    }

    private SearchFilter BuildQuickSearchFilter()
    {
        var folder = (QuickSearchFolderFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var format = (QuickSearchFormatFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var duration = (QuickSearchDurationFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        return duration switch
        {
            "short" => new SearchFilter(folder, format, MaxDurationMs: 3 * 60_000),
            "medium" => new SearchFilter(folder, format, 3 * 60_000, 5 * 60_000),
            "long" => new SearchFilter(folder, format, MinDurationMs: 5 * 60_000),
            _ => new SearchFilter(folder, format),
        };
    }

    private void CloseQuickSearch()
    {
        if (QuickSearchOverlay is null)
        {
            return;
        }

        QuickSearchOverlay.Visibility = Visibility.Collapsed;
        QuickSearchBox.Text = string.Empty;
        QuickSearchBox.ItemsSource = null;
        QuickSearchBox.IsSuggestionListOpen = false;
        QuickSearchResults.ItemsSource = null;
        QuickSearchResults.SelectedIndex = -1;
        QuickSearchEmptyText.Visibility = Visibility.Collapsed;
        QuickSearchFooter.Visibility = Visibility.Collapsed;
        _quickSearchQuery = string.Empty;
    }

    private void QuickSearchOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CloseQuickSearch();
    }

    private void QuickSearchPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void QuickSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        RefreshQuickSearchResults(sender.Text);
    }

    private void RebuildVersionCounts()
    {
        _versionCountByPath.Clear();
        foreach (var group in MediaGroupingService.Group(_all))
        {
            foreach (var version in group.Versions)
            {
                _versionCountByPath[version.Path] = group.VersionCount;
            }
        }
    }

    private void RefreshQuickSearchResults(string query)
    {
        if (_versionCountByPath.Count == 0 && _all.Count > 0)
        {
            RebuildVersionCounts();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            QuickSearchResults.ItemsSource = null;
            QuickSearchResults.SelectedIndex = -1;
            QuickSearchEmptyText.Visibility = Visibility.Collapsed;
            QuickSearchFooter.Visibility = Visibility.Collapsed;
            _quickSearchQuery = string.Empty;
            return;
        }

        var page = _searchSession.Search(query, filter: BuildQuickSearchFilter());
        var visibleResults = page.Items;
        _quickSearchQuery = page.Query;
        QuickSearchResults.ItemsSource = visibleResults
            .Select(item => new QuickSearchResult(
                item,
                page.Query,
                _versionCountByPath.TryGetValue(item.Path, out var versions) ? versions : 1))
            .ToList();
        QuickSearchResults.SelectedIndex = -1;
        QuickSearchEmptyText.Visibility = visibleResults.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        QuickSearchFooter.Visibility = page.TotalCount == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        QuickSearchCountText.Text = LocalizationService.Format("QuickSearch_Found.Text", page.TotalCount);
        QuickSearchShowAllButton.Visibility = page.TotalCount > 6
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void QuickSearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            MoveQuickSearchSelection(1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            MoveQuickSearchSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter &&
                 QuickSearchResults.SelectedItem is QuickSearchResult selected)
        {
            e.Handled = true;
            CloseQuickSearch();
            PlayNow(selected.Item);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseQuickSearch();
        }
    }

    private void MoveQuickSearchSelection(int delta)
    {
        var count = QuickSearchResults.Items.Count;
        if (count == 0)
        {
            return;
        }

        var current = QuickSearchResults.SelectedIndex;
        var next = current < 0
            ? (delta > 0 ? 0 : count - 1)
            : Math.Clamp(current + delta, 0, count - 1);
        QuickSearchResults.SelectedIndex = next;
        QuickSearchResults.ScrollIntoView(QuickSearchResults.Items[next]);
    }

    private void QuickSearchPlayButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            SetQuickSearchPlayHover(button, true);
        }
    }

    private void QuickSearchPlayButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            SetQuickSearchPlayHover(button, false);
        }
    }

    private void SetQuickSearchPlayHover(Button button, bool hover)
    {
        if (FindElementByName(button, "VideoIcon") is UIElement video)
        {
            video.Opacity = hover ? 0 : 1;
        }

        if (FindElementByName(button, "PlayHoverIcon") is UIElement play)
        {
            play.Opacity = hover ? 1 : 0;
        }

        button.Background = hover
            ? ThemeBrush("SubtleFillColorSecondaryBrush")
            : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    private void QuickSearchPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            CloseQuickSearch();
            PlayNow(item);
        }
    }

    private void QuickSearchShowAll_Click(object sender, RoutedEventArgs e)
    {
        var query = _quickSearchQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        CloseQuickSearch();
        ShowSearchResults(query);
    }

    private async void QuickSearchAddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MediaItem item ||
            sender is not Button button)
        {
            return;
        }

        AddToQueue(item);
        var original = button.Content;
        button.Content = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 16,
            Foreground = ThemeBrush("SystemFillColorSuccessBrush"),
        };
        button.IsEnabled = false;
        try
        {
            await Task.Delay(900);
        }
        finally
        {
            button.Content = original;
            button.IsEnabled = true;
        }
    }

    private void QuickSearchMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor ||
            anchor.Tag is not MediaItem item)
        {
            return;
        }

        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem(
            "ContextMenu_Play.Text",
            "\uE768",
            () =>
            {
                CloseQuickSearch();
                PlayNow(item);
            }));
        flyout.Items.Add(CreateMenuItem("Menu_PlayNext.Text", "\uED35", () => EnqueueItemNext(item)));

        var addTo = new MenuFlyoutSubItem
        {
            Text = LocalizationService.Get("ContextMenu_AddToPlaylist"),
            Icon = CreateMenuIcon("\uE8D6"),
        };
        addTo.Items.Add(CreateMenuItem(
            "ContextMenu_NewPlaylist.Text",
            "\uE710",
            () => _ = CreatePlaylistWithItemAsync(item)));
        foreach (var playlist in Playlists.ToList())
        {
            var target = playlist;
            addTo.Items.Add(CreateMenuItem(
                target.Name,
                "\uE8D6",
                () => AddItemToPlaylist(target, item),
                localize: false));
        }

        flyout.Items.Add(addTo);
        var favorite = LibraryActivityStore.IsFavorite(item.Path);
        flyout.Items.Add(CreateMenuItem(
            favorite ? "ContextMenu_RemoveFavorite" : "ContextMenu_AddFavorite",
            "\uE734",
            () =>
            {
                LibraryActivityStore.ToggleFavorite(item.Path);
                RefreshHomeSection();
            }));
        flyout.Items.Add(CreateMenuItem(
            "ContextMenu_Properties.Text",
            "\uE946",
            () => _ = ShowPropertiesAsync(item)));
        flyout.Items.Add(CreateMenuItem(
            "Properties_OpenFolder",
            "\uE838",
            () => OpenContainingFolder(item.Path)));
        flyout.ShowAt(anchor);
    }

    private void QuickSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is MediaItem item)
        {
            CloseQuickSearch();
            PlayNow(item);
            return;
        }

        var query = args.QueryText.Trim();
        CloseQuickSearch();
        if (query.Length == 0)
        {
            return;
        }

        ShowSearchResults(query);
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
        _ = LoadPreviewAsync(true);
    }

    private void SortAz_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = "az";
        SortValueText.Text = LocalizationService.Get("Sort_AZ");
        ApplyFilter();
    }

    private void SortDate_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = "date";
        SortValueText.Text = LocalizationService.Get("Sort_Date");
        ApplyFilter();
    }

    private void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (FilteredGroups.Count == 0)
        {
            return;
        }

        _isRestoringQueue = true;
        _queue.Clear();
        _queueRequesters.Clear();
        foreach (var group in FilteredGroups.ToList())
        {
            var entry = group.Best.CreateQueueCopy();
            _queue.Add(entry);
            _queueRequesters[entry] = null;
            entry.SetQueuedBy("Компьютер");
        }
        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(0);
    }

    private void LibraryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
        }
    }

    private void LibraryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && LibraryList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
            e.Handled = true;
        }
    }

    private void SearchResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
        }
    }

    private void SearchResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && SearchResultsList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
            e.Handled = true;
        }
    }

    private void QueueList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Воспроизводим именно ту строку, по которой щёлкнули, даже если другие
        // выделены чекбоксами для массовых действий.
        if (e.OriginalSource is DependencyObject source &&
            FindQueueContainer(source) is { Content: MediaItem tapped })
        {
            PlayQueueItem(tapped);
            e.Handled = true;
        }
    }

    /// <summary>Ищет ListViewItem вверх по визуальному дереву (в WinUI нет ContainerFromElement).</summary>
    private ListViewItem? FindQueueContainer(DependencyObject? element)
    {
        while (element is not null && !ReferenceEquals(element, QueueList))
        {
            if (element is ListViewItem item)
            {
                return item;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void QueueList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var item = (focused is not null ? FindQueueContainer(focused)?.Content as MediaItem : null)
            ?? SelectedQueueItems().FirstOrDefault();
        if (item is not null)
        {
            PlayQueueItem(item);
            e.Handled = true;
        }
    }

    private void QueueItemCheck_Checked(object sender, RoutedEventArgs e)
    {
        SetQueueItemChecked(sender, true);
    }

    private void QueueItemCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        SetQueueItemChecked(sender, false);
    }

    private void SetQueueItemChecked(object sender, bool isChecked)
    {
        if (sender is CheckBox { Tag: MediaItem item })
        {
            item.IsSelected = isChecked;
            UpdateSelectionBar();
            UpdateQueueCheckboxVisibility();
        }
    }

    private List<MediaItem> SelectedQueueItems()
    {
        return Queue.Where(item => item.IsSelected).ToList();
    }

    private void UpdateSelectionBar()
    {
        if (SelectionBar is null || SelectionText is null || QueueActionsBar is null)
        {
            return;
        }

        var count = Queue.Count(item => item.IsSelected);
        var active = count > 0;
        foreach (var item in Queue)
        {
            item.IsSelectionActive = active;
        }

        SelectionText.Text = LocalizationService.FormatPlural("Selection_Count", count, count);
        SelectionBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        QueueActionsBar.Visibility = active ? Visibility.Collapsed : Visibility.Visible;

        if (SelectAllCheck is not null)
        {
            _updatingSelectAll = true;
            // Пока отмечены не все — галочка снята; полный набор — отмечена.
            SelectAllCheck.IsChecked = Queue.Count > 0 && count == Queue.Count;
            _updatingSelectAll = false;
        }
    }

    private void SelectAllCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSelectAll)
        {
            return;
        }

        var select = SelectAllCheck.IsChecked == true;
        foreach (var item in Queue)
        {
            item.IsSelected = select;
        }

        UpdateSelectionBar();
        UpdateQueueCheckboxVisibility();
    }

    private void AddQueueToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || Queue.Count == 0)
        {
            return;
        }

        var items = Queue.ToList();
        var flyout = new MenuFlyout();
        foreach (var playlist in Playlists)
        {
            var entry = new MenuFlyoutItem { Text = playlist.Name, Tag = playlist };
            entry.Click += (_, _) => AddItemsToPlaylist(playlist, items);
            flyout.Items.Add(entry);
        }

        if (Playlists.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var create = new MenuFlyoutItem { Text = LocalizationService.Get("ContextMenu_CreatePlaylist") };
        create.Click += (_, _) =>
        {
            NavView.SelectedItem = PlaylistsNavItem;
            ShowPlaylists();
            ShowCreatePlaylistPanel(items);
        };
        flyout.Items.Add(create);

        flyout.ShowAt(element);
    }

    private void AddSelectedToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        ShowAddToPlaylistFlyout(element, SelectedQueueItems());
    }

    /// <summary>Общий флайаут «Добавить в плейлист» для очереди и библиотеки.</summary>
    private void ShowAddToPlaylistFlyout(FrameworkElement target, IReadOnlyList<MediaItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var playlist in Playlists)
        {
            var entry = new MenuFlyoutItem { Text = playlist.Name, Tag = playlist };
            entry.Click += (_, _) => AddItemsToPlaylist(playlist, items);
            flyout.Items.Add(entry);
        }

        if (Playlists.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var create = new MenuFlyoutItem { Text = LocalizationService.Get("ContextMenu_CreatePlaylist") };
        create.Click += (_, _) =>
        {
            NavView.SelectedItem = PlaylistsNavItem;
            ShowPlaylists();
            ShowCreatePlaylistPanel(items);
        };
        flyout.Items.Add(create);

        flyout.ShowAt(target);
    }

    private void AddItemsToPlaylist(Playlist playlist, IEnumerable<MediaItem> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            changed |= playlist.AddItem(item.Path);
        }

        if (changed)
        {
            PlaylistService.Save(Playlists);
        }
    }

    private void PlaylistCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = ThemeBrush("CardBackgroundFillColorSecondaryBrush");
        }
    }

    private void PlaylistCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.Background = ThemeBrush("CardBackgroundFillColorDefaultBrush");
        }
    }

    private void PlaylistQuickPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Playlist playlist)
        {
            PlayPlaylistQuick(playlist);
        }
    }

    private void PlaylistCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not Playlist playlist)
        {
            return;
        }

        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateMenuItem("ContextMenu_Play.Text", "\uE768", () => PlayPlaylistQuick(playlist)));
        flyout.Items.Add(CreateMenuItem("Menu_PlayNext.Text", "\uED35", () => QueuePlaylistNext(playlist)));

        var addTo = new MenuFlyoutSubItem
        {
            Text = LocalizationService.Get("ContextMenu_AddTo.Text"),
            Icon = CreateMenuIcon("\uE710"),
        };
        addTo.Items.Add(CreateMenuItem("ContextMenu_Queue.Text", "\uE8D6", () => EnqueuePlaylistItems(playlist)));
        addTo.Items.Add(new MenuFlyoutSeparator());
        addTo.Items.Add(CreateMenuItem("ContextMenu_NewPlaylist.Text", "\uE710", () => _ = CreatePlaylistFromPlaylistAsync(playlist)));
        foreach (var target in Playlists.Where(item => !ReferenceEquals(item, playlist)).ToList())
        {
            var destination = target;
            addTo.Items.Add(CreateMenuItem(
                destination.Name,
                "\uE8D6",
                () => MergePlaylistInto(destination, playlist),
                localize: false));
        }

        flyout.Items.Add(addTo);
        flyout.Items.Add(CreateMenuItem("ContextMenu_Rename.Text", "\uE8AC", () => _ = RenamePlaylistAsync(playlist)));
        flyout.Items.Add(CreateMenuItem("Common_Delete.Text", "\uE74D", () => _ = DeletePlaylistWithConfirmAsync(playlist)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateMenuItem("ContextMenu_Select.Text", "\uE762", () =>
        {
            PlaylistsList.SelectionMode = ListViewSelectionMode.Multiple;
            PlaylistsList.IsItemClickEnabled = false;
        }));

        flyout.ShowAt(card, new FlyoutShowOptions { Position = e.GetPosition(card) });
        e.Handled = true;
    }

    private List<MediaItem> ResolvePlaylistTracks(Playlist playlist)
    {
        var result = new List<MediaItem>();
        foreach (var path in playlist.Items)
        {
            var item = _all.FirstOrDefault(candidate =>
                string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = IsRemote(path) ? MediaItem.FromUrl(path) : MediaItem.FromPath(path);
                ApplyCachedMetadata(item);
            }

            result.Add(item);
        }

        return result;
    }

    private void PlayPlaylistQuick(Playlist playlist)
    {
        var tracks = ResolvePlaylistTracks(playlist);
        if (tracks.Count == 0)
        {
            BarSubtitle.Text = LocalizationService.Get("Playlist_StatusEmpty");
            return;
        }

        _isRestoringQueue = true;
        _queue.Clear();
        _queueRequesters.Clear();
        foreach (var item in tracks)
        {
            var entry = item.CreateQueueCopy();
            _queue.Add(entry);
            _queueRequesters[entry] = null;
            entry.SetQueuedBy("Компьютер");
        }

        _isRestoringQueue = false;
        UpdateQueueCount();
        SaveQueue();
        PlayQueueIndex(0);
    }

    private void QueuePlaylistNext(Playlist playlist)
    {
        var tracks = ResolvePlaylistTracks(playlist);
        if (tracks.Count == 0)
        {
            return;
        }

        var current = _queueIndex >= 0 && _queueIndex < Queue.Count
            ? Queue[_queueIndex]
            : null;
        var insert = current is not null && Queue.Contains(current)
            ? Queue.IndexOf(current) + 1
            : 0;
        foreach (var item in tracks)
        {
            var queueEntry = item.CreateQueueCopy();
            _queue.Insert(Math.Clamp(insert, 0, Queue.Count), queueEntry);
            _queueRequesters[queueEntry] = null;
            queueEntry.SetQueuedBy("Компьютер");
            insert++;
        }

        _queueIndex = current is null ? _queueIndex : Queue.IndexOf(current);
        UpdateQueueCount();
        SaveQueue();
        BarSubtitle.Text = LocalizationService.Format("Playlist_AddedNext", playlist.Name);
    }

    private async Task RenamePlaylistAsync(Playlist playlist)
    {
        var nameBox = new TextBox { Text = playlist.Name };
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_RenameTitle"),
            Content = nameBox,
            PrimaryButtonText = LocalizationService.Get("Common_Save"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        playlist.Name = name;
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
        if (ReferenceEquals(_openPlaylist, playlist))
        {
            PlaylistDetailTitle.Text = playlist.Name;
        }
    }

    private async Task DeletePlaylistWithConfirmAsync(Playlist playlist)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_DeleteTitle"),
            Content = LocalizationService.Format("Playlist_DeleteMessage", playlist.Name),
            PrimaryButtonText = LocalizationService.Get("Common_Delete.Text"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var wasOpen = ReferenceEquals(_openPlaylist, playlist);
        if (wasOpen)
        {
            PlaylistBack_Click(this, new RoutedEventArgs());
        }

        Playlists.Remove(playlist);
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
        if (wasOpen)
        {
            PlaylistsNavItem.IsExpanded = true;
            NavView.SelectedItem = PlaylistsNavItem;
        }
    }

    private async Task CreatePlaylistFromPlaylistAsync(Playlist source)
    {
        var nameBox = new TextBox { Text = LocalizationService.Format("Playlist_CopySuffix", source.Name) };
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_New"),
            Content = nameBox,
            PrimaryButtonText = LocalizationService.Get("Common_Create"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        var baseName = string.IsNullOrWhiteSpace(nameBox.Text) ? LocalizationService.Get("Playlist_New") : nameBox.Text.Trim();
        var name = baseName;
        var suffix = 2;
        while (Playlists.Any(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name = $"{baseName} ({suffix++})";
        }

        var created = new Playlist { Name = name };
        foreach (var path in source.Items)
        {
            created.AddItem(path);
        }

        Playlists.Add(created);
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
    }

    private void MergePlaylistInto(Playlist destination, Playlist source)
    {
        var changed = false;
        foreach (var path in source.Items)
        {
            changed |= destination.AddItem(path);
        }

        if (changed)
        {
            PlaylistService.Save(Playlists);
        }

        UpdatePlaylistsState();
    }

    private async void EnqueuePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Playlist playlist)
        {
            await ConfirmAndEnqueuePlaylistAsync(playlist);
        }
    }

    private async void AddPlaylistToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_openPlaylist is null)
        {
            return;
        }

        await ConfirmAndEnqueuePlaylistAsync(_openPlaylist);
    }

    private async Task ConfirmAndEnqueuePlaylistAsync(Playlist playlist)
    {
        if (Queue.Count > 0)
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("Playlist_SaveQueueTitle"),
                Content = LocalizationService.Get("Playlist_SaveQueueMessage"),
                PrimaryButtonText = LocalizationService.Get("Playlist_SaveQueuePrimary"),
                SecondaryButtonText = LocalizationService.Get("Playlist_SaveQueueSecondary"),
                CloseButtonText = LocalizationService.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            var result = await ShowDialogAsync(dialog);
            if (result == ContentDialogResult.None)
            {
                return;
            }

            if (result == ContentDialogResult.Primary)
            {
                var name = await AskPlaylistNameAsync();
                if (name is null)
                {
                    return;
                }

                SaveCurrentQueueAsPlaylist(name);
            }
        }

        EnqueuePlaylistItems(playlist);
    }

    private async Task<string?> AskPlaylistNameAsync()
    {
        var nameBox = new TextBox
        {
            PlaceholderText = LocalizationService.Get("Playlist_NamePlaceholder"),
            Text = LocalizationService.Format("Playlist_QueueFrom", DateTime.Now),
        };

        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Playlist_NewNameTitle"),
            Content = nameBox,
            PrimaryButtonText = LocalizationService.Get("Common_Save"),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(nameBox.Text)
            ? LocalizationService.Format("Playlist_QueueFrom", DateTime.Now)
            : nameBox.Text.Trim();
    }

    private void SaveCurrentQueueAsPlaylist(string? name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? LocalizationService.Get("Playlist_CurrentQueue") : name.Trim();
        var playlistName = baseName;
        var suffix = 2;
        while (Playlists.Any(item => string.Equals(item.Name, playlistName, StringComparison.CurrentCultureIgnoreCase)))
        {
            playlistName = $"{baseName} ({suffix++})";
        }

        var playlist = new Playlist { Name = playlistName };
        foreach (var item in Queue)
        {
            playlist.AddItem(item.Path);
        }

        Playlists.Add(playlist);
        PlaylistService.Save(Playlists);
        UpdatePlaylistsState();
    }

    private void EnqueuePlaylistItems(Playlist playlist)
    {
        if (playlist.Items.Count == 0)
        {
            SetPlaylistStatus(LocalizationService.Get("Playlist_StatusEmpty"));
            return;
        }

        var added = 0;
        foreach (var path in playlist.Items)
        {
            var item = _itemsByPath.TryGetValue(path, out var known)
                ? known
                : IsRemote(path) ? MediaItem.FromUrl(path) : MediaItem.FromPath(path);
            AddToQueue(item);
            added++;
        }

        SetPlaylistStatus(LocalizationService.Format("Playlist_AddedToQueueCount", added));
    }

    private void SetPlaylistStatus(string text)
    {
        if (PlaylistDetailView is not null && PlaylistDetailView.Visibility == Visibility.Visible)
        {
            PlaylistDetailSubtitle.Text = text;
        }
        else
        {
            BarSubtitle.Text = text;
        }
    }    private void QueueItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetQueueRowPointerState(sender, true);
    }

    private void QueueItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetQueueRowPointerState(sender, false);
    }

    /// <summary>При наведении ShowPlay и чекбокс, в покое — Movies и Video только у играющего.</summary>
    private void SetQueueRowPointerState(object sender, bool entered)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        var item = root.DataContext as MediaItem;
        var anySelected = Queue.Any(entry => entry.IsSelected);
        item?.IsHovered = entered;

        if (root is Grid row)
        {
            row.Background = entered || item?.IsAlternateRow != true
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
                : ThemedBrush(row, "QueueAlternateRowBrush");
        }

        if (root.FindName("QueueItemDelete") is Button delete)
        {
            delete.Opacity = entered ? 1 : 0;
            delete.IsHitTestVisible = entered;
        }

        if (root.FindName("QueueItemMoviesIcon") is FontIcon movies)
        {
            movies.Opacity = entered ? 0 : 1;
        }

        if (root.FindName("QueueItemPlay") is Button play)
        {
            play.Opacity = entered ? 1 : 0;
            play.IsHitTestVisible = entered;
        }

        if (root.FindName("QueueItemCheck") is CheckBox check)
        {
            var visible = entered || (item?.IsSelected ?? false) || anySelected;
            check.Opacity = visible ? 1 : 0;
            check.IsHitTestVisible = visible;
        }
    }

    /// <summary>Показывает чекбоксы, пока есть отмеченные треки.</summary>
    private void UpdateQueueCheckboxVisibility()
    {
        var anySelected = Queue.Any(item => item.IsSelected);
        foreach (var item in Queue)
        {
            if (QueueList.ContainerFromItem(item) is ListViewItem container &&
                FindElementByName(container, "QueueItemCheck") is CheckBox check)
            {
                var visible = item.IsHovered || item.IsSelected || anySelected;
                check.Opacity = visible ? 1 : 0;
                check.IsHitTestVisible = visible;
            }
        }
    }

    private void QueueList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is null)
        {
            return;
        }

        var item = args.Item as MediaItem;
        if (FindElementByName(args.ItemContainer, "QueueRow") is Grid row)
        {
            row.Background = item?.IsAlternateRow == true && item.IsHovered == false
                ? ThemedBrush(row, "QueueAlternateRowBrush")
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        if (FindElementByName(args.ItemContainer, "QueueItemCheck") is not CheckBox check)
        {
            return;
        }

        var visible = (item?.IsHovered ?? false) ||
            (item?.IsSelected ?? false) ||
            Queue.Any(entry => entry.IsSelected);
        check.Opacity = visible ? 1 : 0;
        check.IsHitTestVisible = visible;
    }

    private void QueueItemPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            PlayQueueItem(item);
        }
    }

    private void QueueItemPlay_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: FontIcon icon })
        {
            icon.Foreground = ThemeBrush("AccentTextFillColorPrimaryBrush");
        }
    }

    private void QueueItemPlay_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Content: FontIcon icon })
        {
            icon.Foreground = ThemeBrush("TextFillColorSecondaryBrush");
        }
    }

    private void QueueList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        SaveQueue();
        UpdateQueueCount();
        if (_current is not null)
        {
            _queueIndex = Queue.IndexOf(_current);
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Queue)
        {
            item.IsSelected = false;
        }

        UpdateSelectionBar();
        UpdateQueueCheckboxVisibility();
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

        var currentItem = _queueIndex >= 0 && _queueIndex < Queue.Count
            ? Queue[_queueIndex]
            : null;
        var moving = selected
            .Where(item => !ReferenceEquals(item, currentItem))
            .ToList();
        if (moving.Count == 0)
        {
            return;
        }

        foreach (var item in moving)
        {
            _queue.Remove(item);
        }

        var insert = currentItem is not null
            ? Queue.IndexOf(currentItem) + 1
            : 0;
        insert = Math.Clamp(insert, 0, Queue.Count);
        foreach (var item in moving)
        {
            _queue.Insert(insert++, item);
        }

        _queueIndex = currentItem is not null ? Queue.IndexOf(currentItem) : -1;
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

        var undo = CaptureQueueUndo(selected, "Queue_UndoRemoved");
        foreach (var item in selected)
        {
            RemoveQueueItem(item, showUndo: false);
        }

        ShowQueueUndo(undo);
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

        _queue.Move(index, index - 1);
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

        _queue.Move(index, index + 1);
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
            MinWidth = 560,
            MaxWidth = 760,
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
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
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

        var modified = item.ModifiedUtc == default
            ? "—"
            : item.ModifiedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        AddPair(LocalizationService.Get("Properties_TitleLabel"), item.DisplayTitle, LocalizationService.Get("Properties_Artist"), item.Artist);
        AddPair(LocalizationService.Get("Properties_Duration"), item.DurationText, LocalizationService.Get("Properties_Resolution"), item.ResolutionText);
        AddPair(LocalizationService.Get("Properties_Size"), item.SizeText, LocalizationService.Get("Properties_Format"), item.Format);
        AddPair(LocalizationService.Get("Properties_Folder"), item.Folder, LocalizationService.Get("Properties_Modified"), modified);
        panel.Children.Add(grid);

        var pathPanel = new StackPanel { Spacing = 4 };
        pathPanel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("Properties_Location"),
            FontSize = 12,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
        });
        pathPanel.Children.Add(new TextBlock
        {
            Text = item.Path,
            MaxWidth = 720,
            FontSize = 14,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(pathPanel);

        var openFolder = new HyperlinkButton { Content = LocalizationService.Get("Properties_OpenFolder") };
        openFolder.Click += (_, _) => OpenContainingFolder(item.Path);
        panel.Children.Add(openFolder);

        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Properties_Header"),
            Content = panel,
            CloseButtonText = LocalizationService.Get("Common_Close"),
            MinWidth = 620,
            MaxWidth = 900,
            XamlRoot = XamlRoot,
        };

        await ShowDialogAsync(dialog);
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
        var item = (sender as FrameworkElement)?.Tag switch
        {
            MediaGroup group => group.Best,
            MediaItem media => media,
            _ => null,
        };

        if (item is not null)
        {
            AddToQueue(item);
        }
    }

    private void Versions_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MediaGroup group)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var version in group.Versions.OrderByDescending(item => item.SizeBytes))
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{version.Folder} · {version.Format} · {version.SizeText}",
                Tag = version,
            };
            item.Click += (_, _) => PlayNow(version);
            flyout.Items.Add(item);
        }

        flyout.ShowAt((FrameworkElement)sender);
    }

    private void VideoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        if (root.DataContext is MediaGroup group)
        {
            group.IsHovered = true;
        }

        if (FindElementByName(root, "CardOverlay") is Grid overlay)
        {
            overlay.Opacity = 1;
        }

        if (FindElementByName(root, "CardSurface") is Border surface)
        {
            surface.Background = ThemedBrush(surface, "VideoCardHoverBackgroundBrush");
            surface.BorderBrush = ThemedBrush(surface, "VideoCardHoverBorderBrush");
        }
    }

    private void VideoCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        if (root.DataContext is MediaGroup group)
        {
            group.IsHovered = false;
        }

        if (FindElementByName(root, "CardOverlay") is Grid overlay)
        {
            overlay.Opacity = 0;
        }

        if (FindElementByName(root, "CardSurface") is Border surface)
        {
            surface.Background = ThemedBrush(surface, "VideoCardBackgroundBrush");
            surface.BorderBrush = ThemedBrush(surface, "VideoCardBorderBrush");
        }
    }

    private static FrameworkElement? FindElementByName(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement element && element.Name == name)
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var found = FindElementByName(VisualTreeHelper.GetChild(parent, index), name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void PlayGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaGroup group)
        {
            PlayNow(group.Best);
        }
    }

    private void MoreGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not MediaGroup group)
        {
            return;
        }

        ShowLibraryItemContextMenu(
            element,
            group,
            new Point(Math.Max(element.ActualWidth - 8, 0), Math.Max(element.ActualHeight - 8, 0)));
    }

    private void AddToPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var item = element.Tag switch
        {
            MediaGroup group => group.Best,
            MediaItem media => media,
            _ => null,
        };

        if (item is null)
        {
            return;
        }

        var flyout = new MenuFlyout();

        var queueItem = new MenuFlyoutItem { Text = LocalizationService.Get("ContextMenu_Queue.Text") };
        queueItem.Click += (_, _) => AddToQueue(item);
        flyout.Items.Add(queueItem);

        if (Playlists.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            foreach (var playlist in Playlists)
            {
                var entry = new MenuFlyoutItem { Text = playlist.Name, Tag = playlist };
                entry.Click += (_, _) => AddToPlaylist(playlist, item);
                flyout.Items.Add(entry);
            }
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = LocalizationService.Get("ContextMenu_NewList") };
        create.Click += (_, _) =>
        {
            NavView.SelectedItem = PlaylistsNavItem;
            ShowPlaylists();
            ShowCreatePlaylistPanel(new[] { item });
        };
        flyout.Items.Add(create);

        flyout.ShowAt(element);
    }

    private void AddToPlaylist(Playlist playlist, MediaItem item)
    {
        if (playlist.AddItem(item.Path))
        {
            PlaylistService.Save(Playlists);
        }
    }

    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MediaItem item)
        {
            RemoveQueueItem(item);
        }
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        var undo = CaptureQueueUndo(Queue, "Queue_UndoCleared");
        _queue.Clear();
        _queueRequesters.Clear();
        _queueRequesterNames.Clear();
        _queueIndex = -1;
        UpdateQueueCount();
        SaveQueue();
        ShowQueueUndo(undo);
    }

    private QueueUndoSnapshot CaptureQueueUndo(
        IEnumerable<MediaItem> items,
        string messageKey)
    {
        var entries = items
            .Distinct()
            .Select(item => new QueueUndoEntry(
                item,
                Queue.IndexOf(item),
                _queueRequesters.GetValueOrDefault(item),
                _queueRequesterNames.GetValueOrDefault(item)))
            .Where(entry => entry.Index >= 0)
            .OrderBy(entry => entry.Index)
            .ToList();
        return new QueueUndoSnapshot(entries, _queueIndex, messageKey);
    }

    private void ShowQueueUndo(QueueUndoSnapshot snapshot)
    {
        if (snapshot.Entries.Count == 0)
        {
            return;
        }

        _queueUndo = snapshot;
        UndoBar.Message = LocalizationService.Format(snapshot.MessageKey, snapshot.Entries.Count);
        UndoBar.IsOpen = true;
        if (_undoTimer is null)
        {
            _undoTimer = DispatcherQueue.CreateTimer();
            _undoTimer.Interval = TimeSpan.FromSeconds(10);
            _undoTimer.IsRepeating = false;
            _undoTimer.Tick += (_, _) =>
            {
                UndoBar.IsOpen = false;
                _queueUndo = null;
            };
        }

        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _queueUndo;
        if (snapshot is null)
        {
            return;
        }

        foreach (var entry in snapshot.Entries.OrderBy(entry => entry.Index))
        {
            _queue.Insert(entry.Index, entry.Item);
            _queueRequesters[entry.Item] = entry.RequesterId;
            if (string.IsNullOrWhiteSpace(entry.RequesterName))
            {
                _queueRequesterNames.Remove(entry.Item);
            }
            else
            {
                _queueRequesterNames[entry.Item] = entry.RequesterName;
            }
        }

        if (snapshot.CurrentIndex >= 0 && Queue.Count > 0)
        {
            _queueIndex = Math.Clamp(snapshot.CurrentIndex, 0, Queue.Count - 1);
            _queue.SetCurrent(_queueIndex);
        }

        _queueUndo = null;
        _undoTimer?.Stop();
        UndoBar.IsOpen = false;
        UpdateQueueCount();
        UpdateSelectionBar();
        SaveQueue();
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
            BarSubtitle.Text = LocalizationService.Get("Library_NoVideoFiles");
            return;
        }

        foreach (var item in items)
        {
            AddToQueue(item);
        }
    }

    private void AddToQueue(MediaItem item, string? requesterId = null, string? requesterName = null)
    {
        var entry = item.CreateQueueCopy();
        _queue.Add(entry);
        _queueRequesters[entry] = requesterId;
        if (string.IsNullOrWhiteSpace(requesterName))
        {
            _queueRequesterNames.Remove(entry);
        }
        else
        {
            _queueRequesterNames[entry] = requesterName;
        }

        entry.SetQueuedBy(ResolveRequester(entry));
        UpdateQueueCount();
        _ = LoadDurationAsync(entry);
        if (QueueView.Visibility == Visibility.Visible)
        {
            QueueList.ScrollIntoView(entry);
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
        var missingFiles = 0;
        try
        {
            foreach (var entry in QueueStore.Load())
            {
                if (!IsRemote(entry.Path) && !File.Exists(entry.Path))
                {
                    missingFiles++;
                    LogService.Warn($"Пропущен отсутствующий файл очереди: {entry.Path}");
                    continue;
                }

                var item = IsRemote(entry.Path) ? MediaItem.FromUrl(entry.Path) : MediaItem.FromPath(entry.Path);
                _queue.Add(item);
                _queueRequesters[item] = entry.RequesterId;
                item.SetQueuedBy(ResolveRequester(item));
                _ = LoadDurationAsync(item);
            }
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "RestoreQueue");
            BarSubtitle.Text = LocalizationService.Get("Queue_RestoreFailed");
        }
        finally
        {
            _isRestoringQueue = false;
            UpdateQueueCount();
        }

        if (missingFiles > 0)
        {
            BarSubtitle.Text = LocalizationService.Format("Queue_MissingFiles", missingFiles);
        }
    }

    private void SaveQueue()
    {
        if (_isRestoringQueue)
        {
            return;
        }

        QueueStore.Save(Queue.Select(item =>
            new QueueEntry(item.Path, _queueRequesters.GetValueOrDefault(item))));
    }

    private bool RemoveQueueItemAt(int index, bool showUndo = true)
    {
        if (index < 0 || index >= Queue.Count)
        {
            return false;
        }

        var item = Queue[index];
        var undo = showUndo ? CaptureQueueUndo(new[] { item }, "Queue_UndoRemoved") : null;
        _queue.RemoveAt(index);
        _queueRequesters.Remove(item);
        _queueRequesterNames.Remove(item);
        if (index < _queueIndex)
        {
            _queueIndex--;
        }
        else if (index == _queueIndex)
        {
            _queueIndex = Math.Min(_queueIndex, Queue.Count - 1);
        }

        UpdateQueueCount();
        UpdateSelectionBar();
        SaveQueue();
        if (undo is not null)
        {
            ShowQueueUndo(undo);
        }

        return true;
    }

    private bool RemoveQueueItem(MediaItem item, bool showUndo = true)
    {
        var index = Queue.IndexOf(item);
        return index >= 0 && RemoveQueueItemAt(index, showUndo);
    }

    private void RestoreRecent()
    {
        foreach (var path in SettingsService.RecentPaths)
        {
            var item = IsRemote(path) ? MediaItem.FromUrl(path) : MediaItem.FromPath(path);
            Recent.Add(new MediaGroup(path, item));
        }

        UpdateRecentEmpty();
    }

    private void RecordRecent(MediaItem item)
    {
        var existing = Recent.FirstOrDefault(
            recent => string.Equals(recent.Best.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }

        Recent.Insert(0, CreateRecentGroup(item));
        while (Recent.Count > 12)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        SettingsService.RecentPaths = Recent.Select(recent => recent.Best.Path).ToList();
        LibraryActivityStore.RecordPlay(item.Path);
        UpdateRecentEmpty();
    }

    /// <summary>Карточка «Последних файлов»: одна версия с уже известными превью и длительностью.</summary>
    private MediaGroup CreateRecentGroup(MediaItem item)
    {
        var group = new MediaGroup(item.Path, item);
        if (item.DurationMs > 0)
        {
            group.SetDuration(item.DurationMs);
        }

        group.Thumbnail = LoadThumbnail(item.ThumbnailPath);
        return group;
    }

    /// <summary>Подтягивает длительность и превью из библиотеки в карточки «Последние файлы».</summary>
    private void RefreshRecentMetadata()
    {
        foreach (var group in Recent)
        {
            if (!_itemsByPath.TryGetValue(group.Best.Path, out var match))
            {
                continue;
            }

            group.Best.ApplyMetadata(match.DurationMs, match.Width, match.Height, match.ThumbnailPath);
            group.SetDuration(match.DurationMs);
            if (group.Thumbnail is null)
            {
                group.Thumbnail = LoadThumbnail(match.ThumbnailPath);
            }
        }
    }

    private void UpdateRecentEmpty()
    {
        if (RecentEmptyText is null)
        {
            return;
        }

        RefreshHomeSection();
    }

    private void RecentList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RecentList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
        }
    }

    private void RecentList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && RecentList.SelectedItem is MediaGroup group)
        {
            PlayNow(group.Best);
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
        MarkPlayingItem(item);
        StartPlayback(item);
        UpdateNowPlaying();
    }

    /// <summary>Запускает трек, продолжая сохранённую позицию, если она есть.</summary>
    private void StartPlayback(MediaItem item)
    {
        StopCountdown();
        var resume = TakePendingResume(item.Path);
        if (_detached && _playerWindow is not null)
        {
            _playerWindow.Play(item.Path);
            return;
        }

        if (resume > 0)
        {
            _player.PlayAt(item.Path, resume, true);
        }
        else
        {
            _player.Play(item.Path);
        }
    }

    private long TakePendingResume(string path)
    {
        if (_resumePath is null ||
            !string.Equals(_resumePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var position = _resumePositionMs;
        _resumePath = null;
        _resumePositionMs = 0;
        return position;
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
        MarkPlayingItem(item);
        StartPlayback(item);
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

    /// <summary>Конец трека: сразу следующая песня или отсчёт с оверлеем на видео.</summary>
    private void OnPlaybackEnded()
    {
        if (_repeatMode == RepeatMode.One && _current is not null)
        {
            StartPlayback(_current);
            UpdateNowPlaying();
            return;
        }

        if (SettingsService.CountdownEnabled && HasNextTrack())
        {
            StartNextCountdown();
            return;
        }

        PlayNext();
    }

    private bool HasNextTrack()
    {
        if (Queue.Count == 0)
        {
            return false;
        }

        if (_shuffle || _repeat)
        {
            return true;
        }

        return _queueIndex + 1 < Queue.Count;
    }

    private void StartNextCountdown()
    {
        _countdownRemaining = Math.Max(1, SettingsService.CountdownSeconds);
        CountdownValueText.Text = _countdownRemaining.ToString();
        FullScreenCountdownValueText.Text = _countdownRemaining.ToString();
        CountdownOverlay.Visibility = Visibility.Visible;
        _countdownTimer.Start();
        UpdateFullScreenCountdownVisibility();
    }

    private void OnCountdownTick()
    {
        _countdownRemaining--;
        var value = Math.Max(0, _countdownRemaining).ToString();
        CountdownValueText.Text = value;
        FullScreenCountdownValueText.Text = value;
        if (_countdownRemaining > 0)
        {
            return;
        }

        StopCountdown();
        PlayNext();
    }

    private void StopCountdown()
    {
        _countdownTimer.Stop();
        if (CountdownOverlay is not null)
        {
            CountdownOverlay.Visibility = Visibility.Collapsed;
        }

        if (FullScreenCountdownOverlay is not null)
        {
            FullScreenCountdownOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateFullScreenCountdownVisibility()
    {
        if (FullScreenCountdownOverlay is null)
        {
            return;
        }

        var show = _countdownTimer.IsEnabled && WindowHelper.IsFullScreen(App.Window);
        FullScreenCountdownOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CountdownPlayNow_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        PlayNext();
    }

    private void CountdownCancel_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _player.Stop();
        UpdateTransport();
    }

    private void CountdownToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        SettingsService.CountdownEnabled = CountdownToggle.IsOn;
    }

    private void CountdownSecondsBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializingSettings || double.IsNaN(sender.Value))
        {
            return;
        }

        SettingsService.CountdownSeconds = (int)sender.Value;
    }

    private void RemoteAutoStopToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        SettingsService.RemoteAutoStopEnabled = RemoteAutoStopToggle.IsOn;
    }

    private void RemoteAutoStopMinutesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isInitializingSettings || double.IsNaN(sender.Value))
        {
            return;
        }

        SettingsService.RemoteAutoStopMinutes = (int)sender.Value;
    }

    private void PersistentPinToggle_Toggled(object sender, RoutedEventArgs e)
    {
        UpdatePinControlsState();
    }

    private void UpdatePinControlsState()
    {
        if (PersistentPinToggle is null || HostPinBox is null)
        {
            return;
        }

        var enabled = PersistentPinToggle.IsOn;
        PinDetailsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        HostPinBox.IsEnabled = enabled;
        HostPinConfirmBox.IsEnabled = enabled;
        AdminPinBox.IsEnabled = enabled;
        AdminPinConfirmBox.IsEnabled = enabled;

        var security = SettingsService.GetSecurity();
        if (ResetPinsButton is not null)
        {
            ResetPinsButton.IsEnabled = security.UsePersistentPin ||
                                        security.HostPin is not null ||
                                        security.AdminPin is not null;
        }

        var missing = PinAuthentication.RolesMissingPersistentPin(security);
        if (missing.Count > 0)
        {
            var roles = string.Join(", ", missing.Select(role => LocalizationService.Get(
                role == PinRole.Host ? "Pin_HostRole" : "Pin_AdminRole")));
            ShowPinStatus(LocalizationService.Format("Pin_ConfigWarning", roles), warning: true);
        }
        else if (_pinStatusIsWarning)
        {
            PinSettingsStatus.Visibility = Visibility.Collapsed;
            _pinStatusIsWarning = false;
        }
    }

    private void ShowPinStatus(string text, bool warning)
    {
        if (PinSettingsStatus is null)
        {
            return;
        }

        PinSettingsStatus.Text = text;
        PinSettingsStatus.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _pinStatusIsWarning = warning;
    }

    private async void SavePins_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmPinDialogAsync("Pin_ConfirmSave.Title", "Pin_ConfirmSave.Message", "Ui_SavePins.Content"))
        {
            return;
        }

        var security = SettingsService.GetSecurity();
        security.UsePersistentPin = PersistentPinToggle.IsOn;
        var commonPin = false;

        if (security.UsePersistentPin)
        {
            var host = HostPinBox.Password.Trim();
            var hostConfirm = HostPinConfirmBox.Password.Trim();
            var admin = AdminPinBox.Password.Trim();
            var adminConfirm = AdminPinConfirmBox.Password.Trim();

            var hostResult = ValidatePin(host, hostConfirm, security.HostPin is { IsUsable: true });
            if (hostResult.ErrorKey is not null)
            {
                ShowPinStatus(LocalizationService.Get(hostResult.ErrorKey), warning: false);
                return;
            }

            var adminResult = ValidatePin(admin, adminConfirm, security.AdminPin is { IsUsable: true });
            if (adminResult.ErrorKey is not null)
            {
                ShowPinStatus(LocalizationService.Get(adminResult.ErrorKey), warning: false);
                return;
            }

            if (!hostResult.KeepExisting)
            {
                security.HostPin = PinHasher.Hash(host);
                commonPin |= PinPolicy.IsCommon(host);
            }

            if (!adminResult.KeepExisting)
            {
                security.AdminPin = PinHasher.Hash(admin);
                commonPin |= PinPolicy.IsCommon(admin);
            }
        }

        SettingsService.SetSecurity(security);
        ClearPinBoxes();
        UpdatePinControlsState();
        ShowPinStatus(
            commonPin
                ? LocalizationService.Get("Pin_Saved") + " " + LocalizationService.Get("Pin_CommonWarning")
                : LocalizationService.Get("Pin_Saved"),
            warning: false);
        RefreshRemotePinDisplay();
        LogService.Info("Access PIN settings saved (hash material only).", "security");
    }

    private async void ResetPins_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmPinDialogAsync("Pin_ConfirmReset.Title", "Pin_ConfirmReset.Message", "Ui_ResetPins.Content"))
        {
            return;
        }

        SettingsService.SetSecurity(new SecuritySettings());
        if (PersistentPinToggle is not null)
        {
            PersistentPinToggle.IsOn = false;
        }

        ClearPinBoxes();
        UpdatePinControlsState();
        ShowPinStatus(LocalizationService.Get("Pin_Reset"), warning: false);
        RefreshRemotePinDisplay();
        LogService.Info("Persistent PIN reset; random PINs are used again.", "security");
    }

    private static (string? ErrorKey, bool KeepExisting) ValidatePin(
        string pin,
        string confirmation,
        bool hasExistingPin)
    {
        if (pin.Length == 0 && confirmation.Length == 0)
        {
            // Keeping an existing record is allowed; otherwise the role would
            // silently have no persistent PIN.
            return hasExistingPin ? (null, true) : ("Pin_Empty", false);
        }

        if (pin.Length == 0 || confirmation.Length == 0)
        {
            return ("Pin_Empty", false);
        }

        if (!PinPolicy.Validate(pin).IsValid)
        {
            return ("Pin_Invalid", false);
        }

        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
        {
            return ("Pin_Mismatch", false);
        }

        return (null, false);
    }

    private async Task<bool> ConfirmPinDialogAsync(string titleKey, string messageKey, string primaryKey)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get(titleKey),
            Content = LocalizationService.Get(messageKey),
            PrimaryButtonText = LocalizationService.Get(primaryKey),
            CloseButtonText = LocalizationService.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot show the PIN confirmation dialog: {ex.Message}", "security");
            return false;
        }
    }

    private void ClearPinBoxes()
    {
        HostPinBox.Password = string.Empty;
        HostPinConfirmBox.Password = string.Empty;
        AdminPinBox.Password = string.Empty;
        AdminPinConfirmBox.Password = string.Empty;
    }

    private void RefreshRemotePinDisplay()
    {
        if (_remoteServer is null)
        {
            return;
        }

        RemotePinValue.Text = _remoteServer.Pin;
        RemoteAdminPinValue.Text = _remoteServer.AdminPin;
    }

    private void WifiSafeModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        SettingsService.WifiDirectSafeMode = WifiSafeModeToggle.IsOn;
        LogService.Info($"Wi-Fi Direct safe mode: {WifiSafeModeToggle.IsOn}", "wifi-direct");
        _ = RefreshWifiDiagnosticsAsync();
    }

    private async void RefreshWifiDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        await RefreshWifiDiagnosticsAsync();
    }

    private async Task RefreshWifiDiagnosticsAsync()
    {
        if (WifiDiagnosticsText is null)
        {
            return;
        }

        try
        {
            WifiDiagnosticsText.Text = await DiagnosticsService.BuildWifiDirectDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            WifiDiagnosticsText.Text = ex.Message;
            LogService.Warn($"Cannot refresh Wi-Fi diagnostics: {ex.Message}", "wifi-direct");
        }
    }

    private void LoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        SettingsService.LoggingEnabled = LoggingToggle.IsOn;
        if (LoggingToggle.IsOn)
        {
            LogService.Info("Логирование включено пользователем.");
        }
    }

    private void CheckUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingSettings)
        {
            return;
        }

        SettingsService.CheckUpdatesOnStartup = CheckUpdatesToggle.IsOn;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(userInitiated: true);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null || _isCheckingUpdates)
        {
            return;
        }

        if (!await ConfirmInstallUpdateAsync(_pendingUpdate))
        {
            return;
        }

        IsCheckingUpdates = true;
        _updateDownloadProgress = 0;
        _updateState = UpdateUiState.Downloading;
        RefreshUpdateStatusText();

        try
        {
            var progress = new Progress<double>(value =>
            {
                _updateDownloadProgress = value;
                if (_updateState == UpdateUiState.Downloading)
                {
                    RefreshUpdateStatusText();
                }
            });
            var installer = await UpdateService.DownloadAsync(_pendingUpdate, progress);

            // Установщик уже на диске: плашка меняется на NEW.
            UpdateBadgeVisibility();
            _updateState = UpdateUiState.Installing;
            RefreshUpdateStatusText();
            if (UpdateService.LaunchInstaller(installer))
            {
                // Установщик закроет приложение, заменит файлы и запустит новую версию.
                App.Window.Close();
                return;
            }

            _updateState = UpdateUiState.InstallFailed;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось установить обновление: {ex.Message}");
            _updateState = UpdateUiState.InstallFailed;
        }
        finally
        {
            IsCheckingUpdates = false;
            RefreshUpdateStatusText();
        }
    }

    /// <summary>Подтверждение скачивания и запуска установщика из assets релиза.</summary>
    private async Task<bool> ConfirmInstallUpdateAsync(UpdateInfo info)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Format("Update_ConfirmTitle", info.Version),
            Content = LocalizationService.Format(
                "Update_ConfirmMessage",
                info.Version,
                AppInfo.DisplayVersion),
            PrimaryButtonText = LocalizationService.Format(
                "Settings_UpdatesInstallButton.Content",
                info.Version),
            CloseButtonText = LocalizationService.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        try
        {
            // ShowDialogAsync выставляет RequestedTheme = RootLayout.ActualTheme.
            return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Cannot show the update confirmation: {ex.Message}");
            return false;
        }
    }

    /// <summary>Открывает страницу релиза с описанием изменений.</summary>
    private void OpenUpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        var url = _pendingUpdate?.ReleaseUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = LocalizationService.Format("Update_Failed", ex.Message);
        }
    }

    /// <summary>
    /// Проверяет релизы GitHub. При userInitiated=false (проверка при запуске)
    /// статус показывается только когда обновление действительно есть.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_isCheckingUpdates || !UpdateService.IsSupported)
        {
            return;
        }

        if (!UpdateService.IsConfigured)
        {
            if (userInitiated)
            {
                _updateState = UpdateUiState.NotConfigured;
                RefreshUpdateStatusText();
            }

            return;
        }

        IsCheckingUpdates = true;
        _updateState = UpdateUiState.Checking;
        RefreshUpdateStatusText();

        try
        {
            var check = UpdateService.CheckAsync();

            // Минимальная задержка: при мгновенном ответе сети пользователь должен
            // видеть, что проверка действительно выполнялась.
            if (userInitiated)
            {
                await Task.Delay(400);
            }

            ApplyUpdateCheckResult(await check, userInitiated);
        }
        catch (OperationCanceledException)
        {
            // Таймаут HttpClient (нет токена отмены) — показываем сетевую ошибку.
            if (userInitiated)
            {
                _updateState = UpdateUiState.Failed;
            }
        }
        finally
        {
            IsCheckingUpdates = false;
            RefreshUpdateStatusText();
        }
    }

    private void ApplyUpdateCheckResult(UpdateCheckResult result, bool userInitiated)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.Info is not null:
                _pendingUpdate = result.Info;
                _updateState = UpdateUiState.UpdateAvailable;
                SettingsService.PendingUpdateVersion = result.Info.Version;
                UpdateBadgeVisibility();

                // Тост — только для автоматической проверки при запуске.
                // Ручная проверка из «Параметров» ограничивается статусом и кнопкой.
                if (!userInitiated)
                {
                    ShowUpdateNotification(result.Info);
                }

                break;

            case UpdateCheckStatus.UpToDate:
                _pendingUpdate = null;
                SettingsService.PendingUpdateVersion = string.Empty;
                UpdateBadgeVisibility();
                _updateState = userInitiated ? UpdateUiState.UpToDate : UpdateUiState.Idle;
                break;

            case UpdateCheckStatus.NotConfigured:
                if (userInitiated)
                {
                    _updateState = UpdateUiState.NotConfigured;
                }

                break;

            case UpdateCheckStatus.Failed:
                // Детали — в лог, пользователю — понятное сообщение о подключении.
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    LogService.Warn($"Update check failed: {result.Error}");
                }

                if (userInitiated)
                {
                    _updateState = UpdateUiState.Failed;
                }

                break;
        }
    }

    private void ResetWindowPlacement_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            (App.Window as MainWindow)?.ResetPlacement();
            BarSubtitle.Text = LocalizationService.Get("Settings_ResetWindow_Done");
            LogService.Info("Положение окна сброшено пользователем.");
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось сбросить положение окна.");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(LogService.LogDirectory, "Не удалось открыть папку логов.");
    }

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(ReportService.ReportsDirectory, LocalizationService.Get("Diagnostics_OpenFolderFailed"));
    }

    private static void OpenFolder(string directory, string errorContext)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, errorContext);
        }
    }

    private async void CreateReport_Click(object sender, RoutedEventArgs e)
    {
        if (CreateReportButton is not null)
        {
            CreateReportButton.IsEnabled = false;
        }

        try
        {
            var path = await ReportService.CreateAsync();
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("Ui_ReportCreatedTitle"),
                Content = LocalizationService.Format("Ui_ReportCreatedMessage", path),
                PrimaryButtonText = LocalizationService.Get("Ui_OpenReportsFolder.Content"),
                CloseButtonText = LocalizationService.Get("Common_Close"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                OpenFolder(ReportService.ReportsDirectory, LocalizationService.Get("Diagnostics_OpenFolderFailed"));
            }
        }
        catch (Exception ex)
        {
            LogService.Crash(ex, "Не удалось собрать отчёт об ошибке.");
            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("Ui_ReportFailedTitle"),
                Content = LocalizationService.Format("Ui_ReportFailedMessage", ex.Message),
                CloseButtonText = LocalizationService.Get("Common_Close"),
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            if (CreateReportButton is not null)
            {
                CreateReportButton.IsEnabled = true;
            }
        }
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
            else if (_repeatMode == RepeatMode.All)
            {
                PlayQueueIndex(0);
            }
            else if (_repeatMode == RepeatMode.One && _current is not null)
            {
                StartPlayback(_current);
            }
            else
            {
                _player.Stop();
            }

            return;
        }

        if (FilteredGroups.Count == 0)
        {
            return;
        }

        var index = CurrentGroupIndex();
        var nextInList = index < 0 ? 0 : index + 1;
        if (nextInList < FilteredGroups.Count)
        {
            PlayNow(FilteredGroups[nextInList].Best);
        }
    }

    private int CurrentGroupIndex()
    {
        if (_current is null)
        {
            return -1;
        }

        for (var index = 0; index < FilteredGroups.Count; index++)
        {
            if (FilteredGroups[index].Versions.Contains(_current))
            {
                return index;
            }
        }

        return -1;
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

        if (FilteredGroups.Count == 0)
        {
            return;
        }

        var index = CurrentGroupIndex();
        if (index > 0)
        {
            PlayNow(FilteredGroups[index - 1].Best);
        }
        else
        {
            _player.Seek(0);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
        UpdateTransport();
    }

    private void PlayPause_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _playPauseHovered = true;
        SetPlayPauseHighlight(true);
    }

    private void PlayPause_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _playPauseHovered = false;
        SetPlayPauseHighlight(PlayPauseButton.FocusState != FocusState.Unfocused);
    }

    private void PlayPause_GotFocus(object sender, RoutedEventArgs e) => SetPlayPauseHighlight(true);

    private void PlayPause_LostFocus(object sender, RoutedEventArgs e) => SetPlayPauseHighlight(_playPauseHovered);

    /// <summary>Меняет направление градиента кольца, когда кнопка в фокусе/под курсором.</summary>
    private void SetPlayPauseHighlight(bool active)
    {
        if (PlayPauseRing is null)
        {
            return;
        }

        PlayPauseRing.BorderBrush = (Brush)Resources[active ? "EmptyStateGradientHover" : "EmptyStateGradient"];
        PlayPauseRing.BorderThickness = new Thickness(active ? 4 : 3);
    }

    private void TogglePlayback()
    {
        if (_detached && _playerWindow is not null)
        {
            _playerWindow.TogglePlayPause();
            return;
        }

        if (_player.IsPlaying || _player.CurrentPath is not null)
        {
            _player.PlayPause();
            return;
        }

        if (_current is not null)
        {
            PlayNow(_current);
            return;
        }

        if (QueueList.SelectedItem is MediaItem selectedQueueItem)
        {
            PlayQueueItem(selectedQueueItem);
            return;
        }

        if (Queue.Count > 0)
        {
            PlayQueueIndex(Math.Max(0, _queueIndex));
            return;
        }

        if (LibraryList.SelectedItem is MediaGroup selectedGroup)
        {
            PlayNow(selectedGroup.Best);
            return;
        }

        if (FilteredGroups.Count > 0)
        {
            PlayNow(FilteredGroups[0].Best);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        MarkPlayingItem(null);
        UpdateTransport();
    }

    /// <summary>
    /// Останавливает воспроизведение перед миграцией. Переиспользует штатный
    /// механизм плеера: основной VLC или откреплённое окно.
    /// </summary>
    private Task StopPlaybackForMigrationAsync()
    {
        if (_detached && _playerWindow is not null)
        {
        }
        else if (_player.IsPlaying || _player.CurrentPath is not null)
        {
            _player.Stop();
        }

        _current = null;
        _queueIndex = -1;
        MarkPlayingItem(null);
        UpdateNowPlaying();
        UpdateTransport();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Миграция переписала пути в JSON-хранилищах. Перечитываем их, чтобы
    /// следующее сохранение из UI не вернуло старые пути.
    /// </summary>
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
        _shuffle = !_shuffle;
        UpdateShuffleVisual();
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _repeatMode = _repeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None,
        };
        UpdateRepeatVisual();
    }

    private void UpdateShuffleVisual()
    {
        if (ShuffleButton is null || ShuffleIcon is null || ShuffleIconActive is null || ShuffleSlash is null)
        {
            return;
        }

        // Цвета заданы в XAML через ThemeResource: они разрешаются по фактической
        // теме элемента, поэтому иконка видна и в светлой, и в тёмной теме.
        ShuffleIcon.Visibility = _shuffle ? Visibility.Collapsed : Visibility.Visible;
        ShuffleIconActive.Visibility = _shuffle ? Visibility.Visible : Visibility.Collapsed;
        ShuffleSlash.Visibility = _shuffle ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(
            ShuffleButton,
            LocalizationService.Get(_shuffle ? "Player_ShuffleOn" : "Player_ShuffleOff"));
    }

    private void UpdateRepeatVisual()
    {
        if (RepeatButton is null || RepeatIcon is null || RepeatIconActive is null || RepeatSlash is null)
        {
            return;
        }

        var active = _repeatMode != RepeatMode.None;
        var glyph = _repeatMode == RepeatMode.One ? "\uE8ED" : "\uE8EE";
        RepeatIcon.Glyph = glyph;
        RepeatIconActive.Glyph = glyph;
        RepeatIcon.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        RepeatIconActive.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        RepeatSlash.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        var key = _repeatMode switch
        {
            RepeatMode.All => "Player_RepeatAll",
            RepeatMode.One => "Player_RepeatOne",
            _ => "Player_RepeatOff",
        };
        ToolTipService.SetToolTip(RepeatButton, LocalizationService.Get(key));
    }

    private void MoreFlyout_Opening(object sender, object e)
    {
        SyncPlaybackRateMenu(_player.Rate);
    }

    private void PlaybackRate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate))
        {
            return;
        }

        _player.SetRate(rate);
        SyncPlaybackRateMenu(rate);
    }

    private void SyncPlaybackRateMenu(float rate)
    {
        RateHalf.IsChecked = Math.Abs(rate - 0.5f) < 0.01f;
        RateThreeQuarters.IsChecked = Math.Abs(rate - 0.75f) < 0.01f;
        RateNormal.IsChecked = Math.Abs(rate - 1.0f) < 0.01f;
        RateOneAndQuarter.IsChecked = Math.Abs(rate - 1.25f) < 0.01f;
        RateOneAndHalf.IsChecked = Math.Abs(rate - 1.5f) < 0.01f;
    }

    private async void Equalizer_Click(object sender, RoutedEventArgs e)
    {
        await ShowEqualizerDialogAsync();
    }

    private void MediaInfoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        MediaInfo_Click(this, new RoutedEventArgs());
    }

    private void EqualizerAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowEqualizerDialogAsync();
    }

    private async Task ShowEqualizerDialogAsync()
    {
        var dialog = new EqualizerDialog(_player)
        {
            XamlRoot = XamlRoot,
            RequestedTheme = RootLayout.ActualTheme,
        };
        await dialog.ShowAsync();
    }

    /// <summary>Восстанавливает сохранённый эквалайзер до первого трека.</summary>
    private void RestoreEqualizer()
    {
        if (!SettingsService.EqualizerEnabled)
        {
            return;
        }

        var preset = SettingsService.EqualizerPreset;
        var bands = SettingsService.EqualizerBands;
        Equalizer equalizer;
        if (preset >= 0)
        {
            equalizer = new Equalizer((uint)preset);
        }
        else
        {
            equalizer = new Equalizer();
            for (var band = 0; band < equalizer.BandCount && band < bands.Count; band++)
            {
                equalizer.SetAmp(bands[band], (uint)band);
            }
        }

        _player.SetEqualizer(equalizer);
    }

    private async void MediaInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            BarSubtitle.Text = LocalizationService.Get("MediaInfo_NoActive");
            return;
        }

        // The info button and the context-menu "Properties" action must open
        // the same full properties dialog, not two different layouts.
        await ShowPropertiesAsync(_current);
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
        UpdateFullScreenCountdownVisibility();
    }

    private void FullScreenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WindowHelper.ToggleFullScreen(App.Window);
        UpdateFullScreenCountdownVisibility();
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (QuickSearchOverlay is not null && QuickSearchOverlay.Visibility == Visibility.Visible)
        {
            CloseQuickSearch();
            return;
        }

        if (SearchResultsView is not null && SearchResultsView.Visibility == Visibility.Visible)
        {
            ShowLibrary();
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

        var controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (controlDown && e.Key == VirtualKey.F)
        {
            OpenQuickSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Delete && focused is DependencyObject focusedElement &&
            FindQueueContainer(focusedElement) is { Content: MediaItem queueItem })
        {
            RemoveQueueItem(queueItem);
            e.Handled = true;
            return;
        }

        if (e.Key == (VirtualKey)0xBF)
        {
            OpenQuickSearch();
            e.Handled = true;
            return;
        }

        if (controlDown && e.Key == VirtualKey.Left)
        {
            _player.Seek(_player.PositionMs - 10000);
            e.Handled = true;
            return;
        }

        if (controlDown && e.Key == VirtualKey.Right)
        {
            _player.Seek(_player.PositionMs + 30000);
            e.Handled = true;
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
        MiniPlayerCard.Background = ThemeBrush("SubtleFillColorSecondaryBrush");
    }

    private void MiniPlayer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        MiniPlayerCard.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    /// <summary>
    /// Кисть из словарей темы приложения с учётом фактической темы элемента:
    /// Application.Resources без этого возвращает вариант темы приложения, а не окна.
    /// </summary>
    private static Brush ThemedBrush(FrameworkElement element, string key)
    {
        var dictionaryKey = element.ActualTheme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Default",
        };

        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(dictionaryKey, out var dictionary) &&
            dictionary is ResourceDictionary resources &&
            resources.TryGetValue(key, out var value) &&
            value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    /// <summary>
    /// Кисть WinUI по ключу с учётом фактической темы окна. Application.Resources
    /// отдаёт вариант темы приложения, поэтому цвета берём у пробных элементов,
    /// которые живут в дереве страницы и разрешают ThemeResource корректно.
    /// </summary>
    private Brush ThemeBrush(string key)
    {
        Brush? themed = key switch
        {
            "TextFillColorPrimaryBrush" => PrimaryTextProbe?.Foreground,
            "TextFillColorSecondaryBrush" => SecondaryTextProbe?.Foreground,
            "AccentTextFillColorPrimaryBrush" => AccentTextProbe?.Foreground,
            "LayerFillColorDefaultBrush" => LayerFillProbe?.Background,
            "CardBackgroundFillColorDefaultBrush" => CardFillProbe?.Background,
            "CardBackgroundFillColorSecondaryBrush" => CardFillSecondaryProbe?.Background,
            "SubtleFillColorSecondaryBrush" => SubtleFillProbe?.Background,
            "SystemFillColorSuccessBrush" => SuccessFillProbe?.Background,
            _ => null,
        };

        if (themed is not null)
        {
            return themed;
        }

        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128));
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
        try
        {
            var window = new PlayerWindow(
                _current.Path,
                position,
                wasPlaying,
                volume,
                PlayNext,
                PlayPrevious,
                ReturnFromDetached,
                HasNextTrack);
            _playerWindow = window;
            _detached = true;

            TransportBar.Visibility = Visibility.Collapsed;
            DetachedBar.Visibility = Visibility.Visible;
            DetachedTitle.Text = _current.DisplayTitle;

            window.Activate();
            if (maximize)
            {
                window.Maximize();
            }

            UpdateTransport();
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось открыть отдельное окно видео: {ex.Message}");
            _detached = false;
            _playerWindow = null;
            TransportBar.Visibility = Visibility.Visible;
            DetachedBar.Visibility = Visibility.Collapsed;
            BarSubtitle.Text = LocalizationService.Get("Player_DetachFailed");
            if (wasPlaying)
            {
                _player.PlayPause();
            }

            UpdateTransport();
        }
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
        var volume = window?.Volume ?? _player.Volume;
        var muted = window?.IsMuted ?? false;

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

        _player.SetVolume(muted ? 0 : Math.Clamp(volume, 0, 100));
        window?.CloseForReturn();
        UpdateTransport();
    }

    private void UpdateNowPlaying()
    {
        if (_current is null && !string.IsNullOrWhiteSpace(_player.CurrentPath))
        {
            var path = _player.CurrentPath;
            if (_itemsByPath.TryGetValue(path, out var resolved))
            {
                _current = resolved;
            }
            else
            {
                _current = Queue.FirstOrDefault(item =>
                    string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                    ?? MediaItem.FromPath(path);
            }
        }

        if (DetachedTitle is not null && _current is not null)
        {
            DetachedTitle.Text = _current.DisplayTitle;
        }

        if (_current is null)
        {
            BarTitle.Text = LocalizationService.Get("Player_NothingPlaying");
            BarSubtitle.Text = LocalizationService.Get("Player_ChooseSong");
            DetachButton.IsEnabled = false;
            return;
        }

        BarTitle.Text = _current.DisplayTitle;
        BarSubtitle.Text = _current.Subtitle;
        DetachButton.IsEnabled = true;
    }

    /// <summary>Сохраняет позицию текущего трека (не чаще раза в 5 секунд).</summary>
    private void SavePlaybackPosition(bool force = false)
    {
        // Пока сохранённая позиция ещё не применена к плееру, не затираем её нулём.
        if (_current is null || _resumePath is not null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && (now - _lastPositionSave).TotalSeconds < 5)
        {
            return;
        }

        _lastPositionSave = now;
        SettingsService.LastTrackPath = _current.Path;
        SettingsService.LastTrackPositionMs = Math.Max(0, _player.PositionMs);
    }

    /// <summary>Показывает последний трек на паузе с сохранённой позицией.</summary>
    private void RestorePlaybackState()
    {
        if (_current is not null)
        {
            return;
        }

        var path = SettingsService.LastTrackPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var item = _all.FirstOrDefault(entry =>
                       string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
                   ?? MediaItem.FromPath(path);

        _current = item;
        _queueIndex = Queue.IndexOf(item);
        _resumePath = item.Path;
        _resumePositionMs = Math.Max(0, SettingsService.LastTrackPositionMs);

        UpdateNowPlaying();
        SeekSlider.Maximum = Math.Max(1, item.DurationMs);
        SeekSlider.Value = Math.Min(_resumePositionMs, SeekSlider.Maximum);
        CurrentTimeText.Text = FormatTime(_resumePositionMs);
        TotalTimeText.Text = FormatTime(item.DurationMs);
        PlayPauseIcon.Symbol = Symbol.Play;
        if (_resumePositionMs > 0)
        {
            BarSubtitle.Text = LocalizationService.Format("Player_Resume", item.Subtitle, FormatTime(_resumePositionMs));
        }
    }

    private void UpdateQueueCount()
    {
        QueueCountText.Text = SongCount(Queue.Count);
        QueueEmptyPanel.Visibility = Queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (QueueClearButton is not null)
        {
            QueueClearButton.IsEnabled = Queue.Count > 0;
        }

        UpdateQueueRows();
    }

    /// <summary>Чередование фона строк, чтобы треки не сливались визуально.</summary>
    private void UpdateQueueRows()
    {
        var selectionActive = Queue.Any(item => item.IsSelected);
        for (var index = 0; index < Queue.Count; index++)
        {
            Queue[index].IsAlternateRow = index % 2 == 1;
            Queue[index].IsSelectionActive = selectionActive;
        }

        UpdateQueueRowBackgrounds();
    }

    private void UpdateQueueRowBackgrounds()
    {
        foreach (var item in Queue)
        {
            if (QueueList.ContainerFromItem(item) is ListViewItem container &&
                FindElementByName(container, "QueueRow") is Grid row)
            {
                row.Background = item.IsAlternateRow && !item.IsHovered
                    ? ThemedBrush(row, "QueueAlternateRowBrush")
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
        }
    }

    /// <summary>Подсвечивает в очереди трек, который играет сейчас.</summary>
    private void MarkPlayingItem(MediaItem? item)
    {
        // Queue entries are cloned when added, so the identity of a concrete
        // queue slot — not the file path — decides which duplicate is active.
        var queueEntryId = item?.QueueEntryId;
        foreach (var entry in Queue)
        {
            entry.IsPlaying = item is not null && entry.QueueEntryId == queueEntryId;
        }

        var playlistEntryMarked = false;
        foreach (var entry in PlaylistItems)
        {
            var isPlaying = item is not null && !playlistEntryMarked &&
                (ReferenceEquals(entry, item) ||
                 string.Equals(entry.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            entry.IsPlaying = isPlaying;
            playlistEntryMarked |= isPlaying;
        }

        UpdateQueueCheckboxVisibility();
        UpdatePlaylistSelectionVisuals();
    }

    private void UpdateTransport()
    {
        var position = _player.PositionMs;
        if (position == 0 &&
            _resumePath is not null &&
            _current is not null &&
            string.Equals(_resumePath, _current.Path, StringComparison.OrdinalIgnoreCase))
        {
            // Пока сохранённая позиция не применена к плееру, показываем её, а не 0.
            position = _resumePositionMs;
        }

        var length = _player.LengthMs > 0
            ? _player.LengthMs
            : _current?.DurationMs ?? 0;
        SeekSlider.Maximum = Math.Max(1, length);

        if (!_isSeeking)
        {
            SeekSlider.Value = Math.Clamp(position, 0, Math.Max(1, length));
        }

        CurrentTimeText.Text = FormatTime(position);
        TotalTimeText.Text = FormatTime(length);
        PlayPauseIcon.Symbol = _player.IsPlaying ? Symbol.Pause : Symbol.Play;
        SavePlaybackPosition();
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

    private void ClearStaleNowPlaying()
    {
        if (_player.IsPlaying || _current is null)
        {
            return;
        }

        _current = null;
        _queueIndex = -1;
        MarkPlayingItem(null);
        UpdateNowPlaying();
    }

    public Task<RemoteSnapshot> GetSnapshotAsync()
    {
        return OnUiAsync(() =>
        {
            var showCurrent = _current is not null;
            return new RemoteSnapshot(
                showCurrent ? _current!.DisplayTitle : LocalizationService.Get("Player_NothingPlaying"),
                showCurrent ? _current!.Subtitle : LocalizationService.Get("Player_ChooseSong"),
                _player.IsPlaying,
                _player.PositionMs,
                _player.LengthMs,
                _player.Volume,
                _queueIndex >= 0 && _queueIndex < Queue.Count ? _queueIndex : -1,
                Queue.Select(item => new RemoteQueueItem(
                    item.Path,
                    item.DisplayTitle,
                    item.Subtitle,
                    item.DurationText,
                    item.Folder,
                    ResolveRequester(item),
                    _queueRequesters.GetValueOrDefault(item) ?? string.Empty)).ToList(),
                _shuffle,
                _repeatMode.ToString().ToLowerInvariant());
        });
    }

    private string ResolveRequester(MediaItem item)
    {
        if (!_queueRequesters.TryGetValue(item, out var requesterId) ||
            string.IsNullOrWhiteSpace(requesterId))
        {
            return "Компьютер";
        }

        var alias = _remoteClientDirectory.GetAliasName(requesterId);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        if (_queueRequesterNames.TryGetValue(item, out var guestName) &&
            !string.IsNullOrWhiteSpace(guestName))
        {
            return guestName.Trim();
        }

        return _remoteClientDirectory.ResolveName(requesterId, "Компьютер");
    }

    public Task<IReadOnlyList<RemoteSearchItem>> SearchAsync(string query)
    {
        return OnUiAsync<IReadOnlyList<RemoteSearchItem>>(() =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<RemoteSearchItem>();
            }

            return _searchSession.Search(query, limit: 0).Items
                .Take(30)
                .Select(item => new RemoteSearchItem(
                    item.Path,
                    item.DisplayTitle,
                    item.Subtitle,
                    item.DurationText))
                .ToList();
        });
    }

    public Task<bool> EnqueueAsync(string path, string? requesterId, string? requesterName)
    {
        return OnUiAsync(() =>
        {
            if (Queue.Count >= 5000 || !_itemsByPath.TryGetValue(path, out var item))
            {
                return false;
            }

            AddToQueue(item, requesterId, requesterName);
            return true;
        });
    }

    public Task<bool> RemoveFromQueueAsync(int index) =>
        OnUiAsync(() => RemoveQueueItemAt(index));

    public Task<bool> MoveQueueItemAsync(int from, int to)
    {
        return OnUiAsync(() =>
        {
            if (from < 0 || from >= Queue.Count ||
                to < 0 || to >= Queue.Count ||
                from == to)
            {
                return false;
            }

            _queue.Move(from, to);
            if (_queueIndex == from)
            {
                _queueIndex = to;
            }
            else if (from < _queueIndex && to >= _queueIndex)
            {
                _queueIndex--;
            }
            else if (from > _queueIndex && to <= _queueIndex)
            {
                _queueIndex++;
            }

            UpdateQueueCount();
            UpdateSelectionBar();
            SaveQueue();
            return true;
        });
    }

    public Task<bool> PlayNowAsync(string trackId) =>
        OnUiAsync(() =>
        {
            var item = Queue.FirstOrDefault(candidate =>
                           string.Equals(candidate.Path, trackId, StringComparison.OrdinalIgnoreCase))
                       ?? _itemsByPath.GetValueOrDefault(trackId);
            if (item is null)
            {
                return false;
            }

            PlayPlaylistItem(item);
            return true;
        });

    public Task<bool> ClearQueueAsync() =>
        OnUiAsync(() =>
        {
            _queue.Clear();
            _queueRequesters.Clear();
            _queueRequesterNames.Clear();
            _queueIndex = -1;
            ClearStaleNowPlaying();
            UpdateQueueCount();
            SaveQueue();
            return true;
        });

    public Task<IReadOnlyList<RemotePlaylistItem>> GetPlaylistsAsync() =>
        OnUiAsync<IReadOnlyList<RemotePlaylistItem>>(() =>
            Playlists
                .Select(playlist => new RemotePlaylistItem(
                    playlist.Id,
                    playlist.Name,
                    playlist.Items.Count))
                .ToList());

    public Task<bool> AppendPlaylistAsync(string playlistId) =>
        OnUiAsync(() =>
        {
            var playlist = Playlists.FirstOrDefault(item =>
                string.Equals(item.Id, playlistId, StringComparison.Ordinal));
            if (playlist is null)
            {
                return false;
            }

            var tracks = ResolvePlaylistTracks(playlist);
            foreach (var item in tracks)
            {
                AddToQueue(item);
            }

            ClearStaleNowPlaying();
            return tracks.Count > 0;
        });

    public Task<bool> ControlAsync(string action, double value)
    {
        return OnUiAsync(() =>
        {
            switch (action)
            {
                case "playpause":
                    TogglePlayback();
                    break;
                case "next":
                    PlayNext();
                    break;
                case "prev":
                    PlayPrevious();
                    break;
                case "stop":
                    _player.Stop();
                    break;
                case "volume":
                    _player.SetVolume((int)Math.Clamp(value, 0, 100));
                    break;
                case "seek":
                    if (_player.LengthMs > 0)
                    {
                        _player.Seek((long)(_player.LengthMs * (Math.Clamp(value, 0, 1000) / 1000d)));
                    }

                    break;
                default:
                    return false;
            }

            UpdateTransport();
            return true;
        });
    }

    public Task<IReadOnlyList<RemoteGuestRequest>> GetRequestsAsync() =>
        OnUiAsync<IReadOnlyList<RemoteGuestRequest>>(() =>
        {
            lock (_guestRequestSync)
            {
                return _guestRequests
                    .OrderBy(request => request.CreatedUtcTicks)
                    .ToList();
            }
        });

    public Task<IReadOnlyList<RemoteArchivedRequest>> GetRequestArchiveAsync() =>
        OnUiAsync<IReadOnlyList<RemoteArchivedRequest>>(() =>
        {
            lock (_guestRequestSync)
            {
                return _guestRequestArchive
                    .OrderByDescending(request => request.ClosedUtcTicks)
                    .ToList();
            }
        });

    public Task<bool> ClearRequestArchiveAsync() =>
        OnUiAsync(() =>
        {
            lock (_guestRequestSync)
            {
                _guestRequestArchive.Clear();
            }

            return true;
        });

    public Task<bool> CreateNextRequestAsync(string path, string? requesterId, string? requesterName)
    {
        return OnUiAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(requesterId) ||
                !_itemsByPath.TryGetValue(path, out var item))
            {
                return false;
            }

            lock (_guestRequestSync)
            {
                if (_guestRequests.Count >= SettingsService.MaxPendingRequests)
                {
                    return false;
                }

                if (_guestRequests.Count(request =>
                        string.Equals(request.RequesterId, requesterId, StringComparison.Ordinal)) >=
                    SettingsService.RequestLimitPerGuest)
                {
                    return false;
                }

                if (_guestRequests.Any(request =>
                        string.Equals(request.RequesterId, requesterId, StringComparison.Ordinal) &&
                        string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                _guestRequests.Add(new RemoteGuestRequest(
                    Guid.NewGuid().ToString("N"),
                    item.Path,
                    item.DisplayTitle,
                    requesterName ?? ResolveRequester(item),
                    requesterId,
                    DateTime.UtcNow.Ticks));
            }

            UpdateGuestRequestIndicator();
            return true;
        });
    }

    public Task<bool> ResolveRequestAsync(string requestId, string action)
    {
        return OnUiAsync(() =>
        {
            RemoteGuestRequest? request;
            lock (_guestRequestSync)
            {
                request = _guestRequests.FirstOrDefault(entry =>
                    string.Equals(entry.Id, requestId, StringComparison.Ordinal));
                if (request is null)
                {
                    return false;
                }

                _guestRequests.Remove(request);
            }

            UpdateGuestRequestIndicator();
            if (!_itemsByPath.TryGetValue(request.Path, out var item) &&
                !File.Exists(request.Path))
            {
                BarSubtitle.Text = LocalizationService.Get("Queue_TrackMissing");
                return false;
            }

            item ??= MediaItem.FromPath(request.Path);
            var archiveStatus = action switch
            {
                "next" => "approved-next",
                "play" => "played",
                "reject" => "rejected",
                _ => string.Empty,
            };
            switch (action)
            {
                case "next":
                    EnqueueItemNext(item);
                    break;
                case "play":
                    PlayNow(item);
                    break;
                case "reject":
                    break;
                default:
                    return false;
            }

            lock (_guestRequestSync)
            {
                _guestRequestArchive.Add(new RemoteArchivedRequest(
                    request.Id,
                    request.Path,
                    request.Title,
                    request.Requester,
                    archiveStatus,
                    request.CreatedUtcTicks,
                    DateTime.UtcNow.Ticks));

                if (_guestRequestArchive.Count > 200)
                {
                    _guestRequestArchive.RemoveRange(0, _guestRequestArchive.Count - 200);
                }
            }

            return true;
        });
    }

    public Task<RemoteAdminSettings> GetAdminSettingsAsync() =>
        OnUiAsync(() => new RemoteAdminSettings(
            SettingsService.CountdownEnabled,
            SettingsService.CountdownSeconds,
            SettingsService.RequestLimitPerGuest,
            SettingsService.MaxPendingRequests,
            SettingsService.Theme,
            SettingsService.Language));

    public Task<bool> UpdateAdminSettingsAsync(RemoteAdminSettings settings) =>
        OnUiAsync(() =>
        {
            SettingsService.CountdownEnabled = settings.CountdownEnabled;
            SettingsService.CountdownSeconds = settings.CountdownSeconds;
            SettingsService.RequestLimitPerGuest = settings.MaxRequestsPerGuest;
            SettingsService.MaxPendingRequests = settings.MaxPendingRequests;
            SettingsService.Theme = settings.Theme;
            SettingsService.Language = settings.Language;
            ApplyTheme(SettingsService.Theme);
            ApplyLanguageFromAdmin(SettingsService.Language);
            if (CountdownToggle is not null)
            {
                _isInitializingSettings = true;
                CountdownToggle.IsOn = settings.CountdownEnabled;
                CountdownSecondsBox.Value = settings.CountdownSeconds;
                _isInitializingSettings = false;
            }

            UpdateGuestRequestIndicator();
            return true;
        });

    private void ApplyLanguageFromAdmin(string language)
    {
        try
        {
            LocalizationService.SetLanguage(language);
            LocalizeQueueControls();
            PopulateFolders();
            ApplyFilter();
            UpdateQueueCount();
            UpdateWifiDirectStateUi();
            AboutVersionText.Text = LocalizationService.Format("About_Version", AppInfo.DisplayVersion);
            SelectLanguageInComboBox(LocalizationService.CurrentLanguage);

            if (ThemeComboBox is not null)
            {
                _isInitializingSettings = true;
                ThemeComboBox.SelectedIndex = SettingsService.Theme switch
                {
                    "light" => 1,
                    "dark" => 2,
                    _ => 0,
                };
                _isInitializingSettings = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Не удалось применить язык администратора: {ex.Message}");
        }
    }

    private void UpdateGuestRequestIndicator()
    {
        if (GuestRequestsButton is null)
        {
            return;
        }

        var count = _guestRequests.Count;
        GuestRequestsButton.Visibility = count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        GuestRequestsButtonText.Text = LocalizationService.Format("GuestRequests_Button", count);
    }

    private async void ShowGuestRequests_Click(object sender, RoutedEventArgs e)
    {
        RemoteGuestRequest[] requests;
        lock (_guestRequestSync)
        {
            requests = _guestRequests.ToArray();
        }

        if (requests.Length == 0)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("GuestRequests_Title"),
            CloseButtonText = LocalizationService.Get("Common_Close"),
            XamlRoot = XamlRoot,
        };

        foreach (var request in requests)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Spacing = 2 };
            info.Children.Add(new TextBlock
            {
                Text = request.Title,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            info.Children.Add(new TextBlock
            {
                Text = LocalizationService.Format("GuestRequests_From", request.Requester),
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            });
            row.Children.Add(info);

            Button Action(string key, int column, string action)
            {
                var button = new Button
                {
                    Content = LocalizationService.Get(key),
                    Style = (Style)Application.Current.Resources["CompactButtonStyle"],
                    Tag = request,
                };
                Grid.SetColumn(button, column);
                button.Click += async (_, _) =>
                {
                    await ResolveRequestAsync(request.Id, action);
                    dialog.Hide();
                };
                return button;
            }

            row.Children.Add(Action("GuestRequests_Next", 1, "next"));
            row.Children.Add(Action("GuestRequests_Play", 2, "play"));
            row.Children.Add(Action("GuestRequests_Reject", 3, "reject"));
            panel.Children.Add(row);
        }

        dialog.Content = panel;
        await ShowDialogAsync(dialog);
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        // Диалог должен открываться в той же теме, что и окно: иначе при светлой
        // теме приложения «Свойства» и другие окна показываются тёмными.
        dialog.RequestedTheme = RootLayout.ActualTheme;
        await _dialogLock.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async Task<T> OnUiAsync<T>(Func<T> action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("UI thread is not available."));
        }

        return await completion.Task;
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

    // ── Каталог разделов настроек ───────────────────────────────────────────

    private readonly ObservableCollection<SettingsSection> _settingsSections = new();
    private readonly Dictionary<string, StackPanel> _settingsSectionPanels = new(StringComparer.Ordinal);
    private string? _openSettingsSectionId;

    /// <summary>Строит карточки каталога из SettingsCatalog с текущей локалью.</summary>
    private void BuildSettingsCatalog()
    {
        _settingsSectionPanels.Clear();
        _settingsSectionPanels["interface"] = SectionInterface;
        _settingsSectionPanels["libraries"] = SectionLibraries;
        _settingsSectionPanels["playback"] = SectionPlayback;
        _settingsSectionPanels["remote"] = SectionRemote;
        _settingsSectionPanels["diagnostics"] = SectionDiagnostics;
        _settingsSectionPanels["updates"] = SectionUpdates;

        _settingsSections.Clear();
        foreach (var definition in SettingsCatalog.Sections)
        {
            _settingsSections.Add(new SettingsSection
            {
                Id = definition.Id,
                Icon = definition.Icon,
                TitleKey = definition.TitleKey,
                DescriptionKey = definition.DescriptionKey,
                Title = LocalizationService.Get(definition.TitleKey),
                Description = LocalizationService.Get(definition.DescriptionKey),
            });
        }

        SettingsSectionItems.ItemsSource = _settingsSections;
        RefreshSettingsSectionTitle();
    }

    /// <summary>Главный экран «Параметров»: только каталог разделов.</summary>
    private void ShowSettingsCatalog()
    {
        SettingsCatalogView.Visibility = Visibility.Visible;
        SettingsSectionHost.Visibility = Visibility.Collapsed;
        SettingsPageTitle.Visibility = Visibility.Visible;
        foreach (var panel in _settingsSectionPanels.Values)
        {
            panel.Visibility = Visibility.Collapsed;
        }

        _openSettingsSectionId = null;
    }

    /// <summary>Открывает раздел; при переходе из поиска подсвечивает параметр.</summary>
    private void OpenSettingsSection(string sectionId)
    {
        var definition = SettingsCatalog.Find(sectionId);
        if (definition is null)
        {
            return;
        }

        foreach (var (id, panel) in _settingsSectionPanels)
        {
            panel.Visibility = string.Equals(id, sectionId, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        _openSettingsSectionId = sectionId;
        SettingsSectionTitle.Text = LocalizationService.Get(definition.TitleKey);
        SettingsPageTitle.Visibility = Visibility.Collapsed;
        SettingsCatalogView.Visibility = Visibility.Collapsed;
        SettingsSectionHost.Visibility = Visibility.Visible;
        SettingsSectionScroll.ScrollTo(0, 0);

        if (string.Equals(sectionId, "diagnostics", StringComparison.Ordinal))
        {
            RefreshDiagnosticsSummary();
        }
    }

    private void RefreshSettingsSectionTitle()
    {
        if (_openSettingsSectionId is null)
        {
            return;
        }

        if (SettingsCatalog.Find(_openSettingsSectionId) is { } definition)
        {
            SettingsSectionTitle.Text = LocalizationService.Get(definition.TitleKey);
        }
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) => ShowSettingsCatalog();

    /// <summary>
    /// Клик по пустому месту «Параметров» снимает фокус с полей ввода: числа
    /// не должны выделяться синим и открывать панель стрелок при клике по фону.
    /// </summary>
    private void SettingsBackground_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInteractiveSource(source))
        {
            return;
        }

        SettingsFocusSink?.Focus(FocusState.Programmatic);
    }

    private static bool IsInteractiveSource(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is TextBox or PasswordBox or NumberBox or ComboBox or ToggleSwitch or
                Button or CheckBox or RadioButton or Slider or HyperlinkButton or ListViewItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void SettingsSectionCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SettingsSection section)
        {
            OpenSettingsSection(section.Id);
        }
    }

    // ── Плашка и уведомление об обновлении ──────────────────────────────────

    private string _updateBadgeState = string.Empty;
    private bool _handlingUpdateAction;

    /// <summary>Плашка у «Параметры»: UPDATE пока доступно, NEW когда установщик скачан.</summary>
    private void UpdateBadgeVisibility()
    {
        if (SettingsUpdatePlaque is null)
        {
            return;
        }

        if (!SettingsService.ShouldShowUpdateBadge(AppInfo.DisplayVersion))
        {
            if (_updateBadgeState.Length > 0)
            {
                _updateBadgeState = string.Empty;
                LogService.Info("[update] badge hidden");
            }

            SettingsUpdatePlaque.Visibility = Visibility.Collapsed;
            if (SettingsUpdateCompactBadge is not null)
            {
                SettingsUpdateCompactBadge.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var version = SettingsService.PendingUpdateVersion;
        var ready = UpdateService.IsInstallerDownloaded(version);
        var state = ready ? "new" : "update";
        var paneOpen = NavView?.IsPaneOpen ?? true;

        SettingsUpdatePlaqueText.Text = ready
            ? UpdateToastLocalization.Get("Update_BadgeNew", "Новое", "New")
            : UpdateToastLocalization.Get("Update_BadgeUpdate", "Обновление", "Update");
        SettingsUpdatePlaque.Visibility = paneOpen ? Visibility.Visible : Visibility.Collapsed;

        // В свёрнутой панели текстовой плашки не видно — показываем значок на шестерёнке.
        if (SettingsUpdateCompactBadge is not null)
        {
            SettingsUpdateCompactBadge.Visibility = paneOpen
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        if (!string.Equals(_updateBadgeState, state, StringComparison.Ordinal))
        {
            _updateBadgeState = state;
            LogService.Info($"[update] badge state={state} version='{version}' paneOpen={paneOpen}");
        }
    }

    private void OnUpdateStateChanged()
    {
        // Сервис может измениться из фонового потока — плашка только в UI-потоке.
        DispatcherQueue.TryEnqueue(UpdateBadgeVisibility);
    }

    private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            UpdateBadgeVisibility();
        }
    }

    private void OnUpdateActionRequested(string action)
    {
        // App кладёт действие в PendingUpdateAction; забираем ровно один раз.
        var pending = App.TakePendingUpdateAction();
        _ = HandleUpdateActionAsync(pending ?? action);
    }

    private void SettingsUpdatePlaque_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        OpenUpdateSettings();
    }

    /// <summary>«Напомнить позже»: убирает плашку до следующего релиза.</summary>
    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        var version = _pendingUpdate?.Version ?? SettingsService.PendingUpdateVersion;
        SettingsService.DismissUpdate(version);
        UpdateBadgeVisibility();
        _pendingUpdate = null;
        _updateState = UpdateUiState.Dismissed;
        RefreshUpdateStatusText();
    }

    /// <summary>Единая обработка тоста: install / postpone / open_update_page.</summary>
    private async Task HandleUpdateActionAsync(string action)
    {
        if (_handlingUpdateAction)
        {
            return;
        }

        _handlingUpdateAction = true;
        try
        {
            LogService.Info($"[update] handling action='{action}'");
            switch (action)
            {
                case UpdateActivationArguments.Install:
                    App.Window?.Activate();
                    if (_pendingUpdate is null)
                    {
                        await CheckForUpdatesAsync(userInitiated: true);
                    }

                    if (_pendingUpdate is not null)
                    {
                        InstallUpdate_Click(this, new RoutedEventArgs());
                    }

                    break;

                case UpdateActivationArguments.Postpone:
                    UpdateLater_Click(this, new RoutedEventArgs());
                    break;

                default:
                    OpenUpdateSettings();
                    break;
            }
        }
        finally
        {
            _handlingUpdateAction = false;
        }
    }

    /// <summary>Открывает «Параметры › Обновления» из тоста или плашки.</summary>
    private void OpenUpdateSettings()
    {
        try
        {
            NavView.SelectedItem = SettingsNavItem;
            App.Window?.Activate();
            ShowSettings();
            OpenSettingsSection("updates");
            LogService.Info("[update] navigated to Settings > Updates");
        }
        catch (Exception ex)
        {
            LogService.Warn($"[update] navigation failed: {ex.Message}");
        }
    }

    /// <summary>Системное уведомление справа снизу; при недоступности остаётся плашка.</summary>
    private void ShowUpdateNotification(UpdateInfo info)
    {
        if (!SettingsService.ShouldShowUpdateBadge(AppInfo.DisplayVersion))
        {
            return;
        }

        var highlights = UpdateToastBuilder.ExtractHighlights(info.Changelog, max: 2);
        var size = UpdateToastBuilder.FormatSize(info.InstallerSize);
        var versionLine = $"{AppInfo.DisplayVersion} → {info.Version}";
        var body = highlights.Count > 0
            ? UpdateToastLocalization.Format(
                "Update_ToastBody",
                "Установлена {0} • {1}. Что нового: {2}",
                "Installed {0} • {1}. What's new: {2}",
                versionLine,
                size,
                string.Join(" • ", highlights))
            : UpdateToastLocalization.Format(
                "Update_ToastBodyNoNotes",
                "Установлена {0} • {1}",
                "Installed {0} • {1}",
                versionLine,
                size);

        var attribution = info.PublishedAt is { } published
            ? UpdateToastLocalization.Format(
                "Update_ToastAttribution",
                "Опубликовано {0}",
                "Published {0}",
                published.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
            : null;

        var content = new UpdateToastContent(
            UpdateToastLocalization.Get(
                "Update_ToastTitle",
                "Доступно обновление TempoKaraoke",
                "TempoKaraoke update available"),
            body,
            attribution,
            UpdateToastLocalization.Get("Update_ToastInstall", "Установить", "Install"),
            UpdateToastLocalization.Get("Update_ToastLater", "Позже", "Later"),
            UpdateToastLocalization.Get("Update_ToastChangelog", "Что нового", "What's new"),
            UpdateToastBuilder.OpenUpdatePageArgument,
            ResolveAppLogoUri(),
            DateTimeOffset.Now);

        var shown = NotificationService.TryShowUpdate(content);
        LogService.Info($"[update] notification shown={shown} version={info.Version}");
    }

    /// <summary>URI логотипа приложения для appLogoOverride.</summary>
    private static string? ResolveAppLogoUri()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon-192.png");
            if (File.Exists(path))
            {
                return new Uri(path).AbsoluteUri;
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"[update] app logo lookup failed: {ex.Message}");
        }

        return "ms-appx:///Assets/AppIcon-192.png";
    }

    // ── Диагностика ─────────────────────────────────────────────────────────

    /// <summary>Ленивая сводка: тайминги запуска, система/дисплеи, целостность.</summary>
    private void RefreshDiagnosticsSummary()
    {
        StartupTimingSummary.Text = StartupTrace.BuildSummaryText();
        StartupTimingsText.Text = StartupTrace.BuildTimelineText();
        SystemInfoText.Text = BuildSystemInfoText();
        ConfigIntegrityText.Text = BuildConfigIntegrityText();
    }

    /// <summary>Копирует полный лог запуска в буфер обмена.</summary>
    private void CopyStartupTiming_Click(object sender, RoutedEventArgs e)
    {
        var text = StartupTimingsText?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            BarSubtitle.Text = LocalizationService.Get("Diagnostics_TimingCopied");
        }
        catch (Exception ex)
        {
            LogService.Warn($"Copy startup timing failed: {ex.Message}");
        }
    }

    private static string BuildSystemInfoText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{LocalizationService.Get("Diagnostics_LabelVersion")}: {AppInfo.DisplayVersion}");
        builder.AppendLine($"{LocalizationService.Get("Diagnostics_LabelMode")}: {(AppInfo.IsPackaged ? "MSIX" : "portable")}");
        builder.AppendLine($"{LocalizationService.Get("Diagnostics_LabelOs")}: {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

        var displays = WindowPlacementService.GetDisplays();
        builder.AppendLine($"{LocalizationService.Get("Diagnostics_LabelDisplays")}: {displays.Count}");
        foreach (var display in displays)
        {
            builder.AppendLine($"  {(display.IsPrimary ? "*" : "-")} {display.DisplayId} · {display.WorkArea.Width}×{display.WorkArea.Height} @{display.Scale:0.##}");
        }

        if (App.Window is MainWindow window)
        {
            var appWindow = window.AppWindow;
            builder.Append($"{LocalizationService.Get("Diagnostics_LabelWindow")}: {appWindow.Position.X},{appWindow.Position.Y} {appWindow.Size.Width}×{appWindow.Size.Height}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildConfigIntegrityText()
    {
        var builder = new StringBuilder();
        var settings = AppStorage.PathFor("settings.json");
        builder.AppendLine($"settings.json: {DescribeFile(settings)}");

        var catalog = LocalizationPaths.FindCatalogDirectory();
        var manifest = catalog is null ? null : Path.Combine(catalog, "manifest.json");
        builder.AppendLine($"localization/manifest.json: {DescribeFile(manifest)}");

        if (catalog is not null)
        {
            var languages = Math.Max(Directory.GetFiles(catalog, "*.json").Length - 1, 0);
            builder.Append($"{LocalizationService.Get("Diagnostics_LabelLanguages")}: {languages}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return LocalizationService.Get("Diagnostics_FileMissing");
        }

        try
        {
            var info = new FileInfo(path);
            return LocalizationService.Format(
                "Diagnostics_FileState",
                LocalizationService.Format("Diagnostics_SizeKb", info.Length / 1024.0),
                info.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
        }
        catch
        {
            return LocalizationService.Get("Diagnostics_FileMissing");
        }
    }}

