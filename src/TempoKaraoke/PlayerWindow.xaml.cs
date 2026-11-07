using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TempoKaraoke.Services;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;

namespace TempoKaraoke;

/// <summary>
/// Откреплённое окно видео. Для второго окна используется MediaPlayerElement:
/// libvlc WinUI не поддерживает два одновременных video output в одном процессе,
/// а Media Foundation надёжно играет MP4/MKV с H.264.
/// </summary>
public sealed partial class PlayerWindow : Window
{
    private readonly MediaPlayer _mediaPlayer;
    private readonly Action _next;
    private readonly Action _previous;
    private readonly Func<bool>? _hasNext;
    private readonly DispatcherTimer _transportTimer;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _countdownTimer;
    private int _countdownRemaining;
    private string _path = string.Empty;
    private long _positionMs;
    private long _closedPositionMs;
    private long _closedLengthMs;
    private int _closedVolume;
    private bool _closedMuted;
    private bool _closedPlaying;
    private bool _resume;
    private bool _isSeeking;
    private bool _suppressVolume;
    private bool _closed;
    private bool _returning;

    public PlayerWindow(
        string path,
        long positionMs,
        bool resume,
        int volume,
        Action next,
        Action previous,
        Action returnAction,
        Func<bool>? hasNext = null)
    {
        InitializeComponent();
        LocalizationService.Apply(RootGrid, SettingsService.Language);

        _path = path;
        _positionMs = positionMs;
        _resume = resume;
        _next = next;
        _previous = previous;
        _hasNext = hasNext;
        ReturnRequested += (_, _) => returnAction();

        Title = LocalizationService.Get("Window_VideoTitle");
        ApplyChrome();
        ResizeToDip(1280, 720);

        _mediaPlayer = new MediaPlayer
        {
            Volume = Math.Clamp(volume, 0, 100) / 100.0,
        };
        MediaElement.SetMediaPlayer(_mediaPlayer);
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaEnded += OnMediaEnded;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => UpdateTransport();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hideTimer.Tick += (_, _) => HideControls();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();

        Closed += OnClosed;

        _suppressVolume = true;
        VolumeSlider.Value = _mediaPlayer.Volume * 100;
        _suppressVolume = false;

        SetSource(path, positionMs, resume);
        _transportTimer.Start();
        _hideTimer.Start();
        UpdateTransport();
    }

    public event EventHandler? ReturnRequested;

    public long PositionMs =>
        _closed ? _closedPositionMs : (long)(_mediaPlayer.PlaybackSession?.Position.TotalMilliseconds ?? 0);

    public long LengthMs =>
        _closed ? _closedLengthMs : (long)(_mediaPlayer.PlaybackSession?.NaturalDuration.TotalMilliseconds ?? 0);

    public bool IsPlaying =>
        _closed
            ? _closedPlaying
            : _mediaPlayer.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;

    public int Volume =>
        _closed ? _closedVolume : (int)Math.Round(Math.Clamp(_mediaPlayer.Volume, 0, 1) * 100);

    public bool IsMuted => _closed ? _closedMuted : _mediaPlayer.IsMuted;

    public void Maximize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void ApplyChrome()
    {
        var isDark = SettingsService.Theme switch
        {
            "light" => false,
            "dark" => true,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };

        RootGrid.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var bar = AppWindow.TitleBar;
        var background = isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        var foreground = isDark ? Colors.White : Colors.Black;

        bar.BackgroundColor = background;
        bar.ForegroundColor = foreground;
        bar.InactiveBackgroundColor = background;
        bar.InactiveForegroundColor = foreground;
        bar.ButtonBackgroundColor = background;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonInactiveForegroundColor = foreground;
        bar.ButtonHoverBackgroundColor = isDark
            ? Color.FromArgb(255, 58, 58, 58)
            : Color.FromArgb(255, 229, 229, 229);
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = isDark
            ? Color.FromArgb(255, 74, 74, 74)
            : Color.FromArgb(255, 214, 214, 214);
        bar.ButtonPressedForegroundColor = foreground;
    }

    private void ResizeToDip(int widthDip, int heightDip)
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var width = Math.Min((int)Math.Round(widthDip * scale), Math.Max(320, work.Width - 40));
        var height = Math.Min((int)Math.Round(heightDip * scale), Math.Max(240, work.Height - 40));
        AppWindow.Resize(new SizeInt32(width, height));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public void Play(string path)
    {
        StopCountdown();
        _positionMs = 0;
        _resume = true;
        SetSource(path, 0, true);
    }

    private static bool IsRemote(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == "rtsp");
    }

    private void SetSource(string path, long positionMs, bool resume)
    {
        _path = path;
        _positionMs = positionMs;
        _resume = resume;

        _mediaPlayer.Source = MediaSource.CreateFromUri(CreateUri(path));
    }

    private static Uri CreateUri(string path)
    {
        if (IsRemote(path))
        {
            return new Uri(path);
        }

        // new Uri работает и с UNC, а ручная склейка «file:///» ломалась на # и %.
        var fullPath = Path.GetFullPath(path)
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal);
        return new Uri(fullPath);
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
            {
                return;
            }

            if (_positionMs > 0)
            {
                sender.PlaybackSession.Position = TimeSpan.FromMilliseconds(_positionMs);
            }

            if (_resume)
            {
                sender.Play();
            }
            else
            {
                sender.Pause();
            }

            UpdateTransport();
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        if (_closed)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
            {
                return;
            }

            if (SettingsService.CountdownEnabled && (_hasNext?.Invoke() ?? true))
            {
                StartCountdown();
            }
            else
            {
                _next();
            }
        });
    }

    private void StartCountdown()
    {
        _countdownRemaining = Math.Max(1, SettingsService.CountdownSeconds);
        CountdownValueText.Text = _countdownRemaining.ToString();
        CountdownOverlay.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _countdownTimer.Start();
    }

    private void OnCountdownTick()
    {
        _countdownRemaining--;
        CountdownValueText.Text = Math.Max(0, _countdownRemaining).ToString();
        if (_countdownRemaining > 0)
        {
            return;
        }

        StopCountdown();
        _next();
    }

    private void StopCountdown()
    {
        _countdownTimer.Stop();
        if (CountdownOverlay is not null)
        {
            CountdownOverlay.Visibility = Visibility.Collapsed;
        }

        _hideTimer.Start();
    }

    private void CountdownPlayNow_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _next();
    }

    private void CountdownCancel_Click(object sender, RoutedEventArgs e)
    {
        StopCountdown();
        _mediaPlayer.Pause();
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        DispatcherQueue.TryEnqueue(UpdateTransport);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _countdownTimer.Stop();
        _closedPositionMs = (long)(_mediaPlayer.PlaybackSession?.Position.TotalMilliseconds ?? 0);
        _closedLengthMs = (long)(_mediaPlayer.PlaybackSession?.NaturalDuration.TotalMilliseconds ?? 0);
        _closedVolume = (int)Math.Round(Math.Clamp(_mediaPlayer.Volume, 0, 1) * 100);
        _closedMuted = _mediaPlayer.IsMuted;
        _closedPlaying = _mediaPlayer.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        _transportTimer.Stop();
        _hideTimer.Stop();

        _mediaPlayer.MediaOpened -= OnMediaOpened;
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        var session = _mediaPlayer.PlaybackSession;
        if (session is not null)
        {
            session.PlaybackStateChanged -= OnPlaybackStateChanged;
        }

        try
        {
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
            MediaElement.SetMediaPlayer(null);
            _mediaPlayer.Dispose();
        }
        catch
        {
            // Окно могло закрыться во время смены источника.
        }

        if (!_returning)
        {
            ReturnRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CloseForReturn()
    {
        if (_closed)
        {
            return;
        }

        _returning = true;
        try
        {
            Close();
        }
        catch
        {
            // Окно уже закрыто пользователем.
        }
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void ControlsBar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hideTimer.Stop();
    }

    private void ControlsBar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hideTimer.Start();
    }

    private void ShowControls()
    {
        ControlsBar.Visibility = Visibility.Visible;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControls()
    {
        if (!IsPlaying)
        {
            _hideTimer.Stop();
            return;
        }

        ControlsBar.Visibility = Visibility.Collapsed;
        _hideTimer.Stop();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }

        UpdateTransport();
    }

    /// <summary>Seek from the phone remote or the host UI.</summary>
    public void Seek(long milliseconds) => SeekTo(milliseconds);

    /// <summary>Set volume from the phone remote.</summary>
    public void SetVolume(int volume)
    {
        _mediaPlayer.Volume = Math.Clamp(volume, 0, 100) / 100.0;
        UpdateTransport();
    }

    /// <summary>Stop from the phone remote: pause and rewind to the start.</summary>
    public void Stop()
    {
        _mediaPlayer.Pause();
        if (_mediaPlayer.PlaybackSession is not null)
        {
            _mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
        }

        UpdateTransport();
    }

    /// <summary>Останавливает откреплённое воспроизведение перед миграцией.</summary>
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer.Pause();
        _mediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
        UpdateTransport();
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        _previous();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        _next();
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
        SeekTo((long)SeekSlider.Value);
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = false;
        SeekTo((long)SeekSlider.Value);
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isSeeking)
        {
            TimeText.Text = $"{FormatTime((long)e.NewValue)} / {FormatTime(LengthMs)}";
        }
    }

    private void SeekTo(long milliseconds)
    {
        if (_mediaPlayer.PlaybackSession is null)
        {
            return;
        }

        var target = Math.Clamp(milliseconds, 0, Math.Max(0, LengthMs));
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(target);
        UpdateTransport();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressVolume)
        {
            return;
        }

        _mediaPlayer.Volume = e.NewValue / 100.0;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer.IsMuted = !_mediaPlayer.IsMuted;
        UpdateTransport();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.ToggleFullScreen(this);
    }

    private void FullScreen_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WindowHelper.ToggleFullScreen(this);
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        WindowHelper.ExitFullScreen(this);
    }

    private void Return_Click(object sender, RoutedEventArgs e)
    {
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTransport()
    {
        var length = Math.Max(1, LengthMs);
        SeekSlider.Maximum = length;

        if (!_isSeeking)
        {
            SeekSlider.Value = Math.Clamp(PositionMs, 0, length);
        }

        TimeText.Text = $"{FormatTime(PositionMs)} / {FormatTime(LengthMs)}";
        PlayPauseIcon.Symbol = IsPlaying ? Symbol.Pause : Symbol.Play;
        MuteIcon.Symbol = _mediaPlayer.IsMuted ? Symbol.Mute : Symbol.Volume;

        if (!IsPlaying)
        {
            ControlsBar.Visibility = Visibility.Visible;
        }

        var volume = _mediaPlayer.Volume * 100;
        if (!_suppressVolume && Math.Abs(VolumeSlider.Value - volume) > 0.5)
        {
            _suppressVolume = true;
            VolumeSlider.Value = volume;
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
