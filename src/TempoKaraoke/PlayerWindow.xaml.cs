using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TempoKaraoke.Services;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;

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
    private readonly DispatcherTimer _transportTimer;
    private readonly DispatcherTimer _hideTimer;
    private string _path = string.Empty;
    private long _positionMs;
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
        Action returnAction)
    {
        InitializeComponent();

        _path = path;
        _positionMs = positionMs;
        _resume = resume;
        _next = next;
        _previous = previous;
        ReturnRequested += (_, _) => returnAction();

        Title = "Tempo Karaoke — видео";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1280, 720));

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

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HideControls();

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
        (long)(_mediaPlayer.PlaybackSession?.Position.TotalMilliseconds ?? 0);

    public long LengthMs =>
        (long)(_mediaPlayer.PlaybackSession?.NaturalDuration.TotalMilliseconds ?? 0);

    public bool IsPlaying =>
        _mediaPlayer.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;

    public void Maximize()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    public void Play(string path)
    {
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

        var uri = IsRemote(path)
            ? new Uri(path)
            : new Uri($"file:///{path.Replace('\\', '/')}");
        _mediaPlayer.Source = MediaSource.CreateFromUri(uri);
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
        DispatcherQueue.TryEnqueue(() => _next());
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
        _transportTimer.Stop();
        _hideTimer.Stop();

        _mediaPlayer.MediaOpened -= OnMediaOpened;
        _mediaPlayer.MediaEnded -= OnMediaEnded;
        _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        _mediaPlayer.Pause();
        if (!_returning)
        {
            ReturnRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CloseForReturn()
    {
        _returning = true;
        Close();
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
        ControlsBar.Visibility = Visibility.Collapsed;
        _hideTimer.Stop();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
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
