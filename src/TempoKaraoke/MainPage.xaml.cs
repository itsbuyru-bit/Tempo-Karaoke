using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace TempoKaraoke;

/// <summary>
/// Технический спайк: проверяет, что WinUI 3 + LibVLCSharp.WinUI играют реальные
/// файлы из медиатеки. Это ещё не продуктовый UI.
/// </summary>
public sealed partial class MainPage : Page
{
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private long _lengthMs;
    private bool _initialized;

    public MainPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        VideoView.Initialized += OnVideoViewInitialized;
    }

    private void OnVideoViewInitialized(object? sender, InitializedEventArgs e)
    {
        InitializePlayer(e.SwapChainOptions);
    }

    private void InitializePlayer(string[] swapChainOptions)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Core.Initialize();

            // SwapChainOptions обязательны: без них libvlc откроет своё окно
            // вместо вывода в VideoView.
            var options = swapChainOptions
                .Concat(new[] { "--no-video-title-show", "--quiet" })
                .ToArray();

            _libVlc = new LibVLC(options);
            _mediaPlayer = new MediaPlayer(_libVlc);

            _mediaPlayer.TimeChanged += OnTimeChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.EncounteredError += OnEncounteredError;

            VideoView.MediaPlayer = _mediaPlayer;
            _initialized = true;
            StatusText.Text = $"libvlc {_libVlc.Version} — готов";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"libvlc не загрузился: {ex.Message}";
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        VideoView.Initialized -= OnVideoViewInitialized;
        VideoView.MediaPlayer = null;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.TimeChanged -= OnTimeChanged;
            _mediaPlayer.LengthChanged -= OnLengthChanged;
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.EncounteredError -= OnEncounteredError;
        }

        _currentMedia?.Dispose();
        _mediaPlayer?.Dispose();
        _libVlc?.Dispose();
        _mediaPlayer = null;
        _libVlc = null;
        _currentMedia = null;
        _initialized = false;
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
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

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            Play(file.Path);
        }
    }

    private async void Sample_Click(object sender, RoutedEventArgs e)
    {
        var root = Environment.GetEnvironmentVariable("KARAOKE_SAMPLE") ?? @"G:\Karaoke";

        StatusText.Text = "Ищу файл…";

        string? path;
        try
        {
            path = await Task.Run(() => FindSample(root));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка сканирования: {ex.Message}";
            return;
        }

        if (path is null)
        {
            StatusText.Text = $"Не нашёл видео в {root}";
            return;
        }

        Play(path);
    }

    private static string? FindSample(string root)
    {
        if (File.Exists(root))
        {
            return root;
        }

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p =>
                p.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private void Play(string path)
    {
        if (_mediaPlayer is null || _libVlc is null)
        {
            StatusText.Text = "libvlc ещё не готов";
            return;
        }

        _mediaPlayer.Stop();
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, path, FromType.FromPath);

        if (!_mediaPlayer.Play(_currentMedia))
        {
            StatusText.Text = $"Не удалось запустить: {Path.GetFileName(path)}";
            return;
        }

        _lengthMs = 0;
        StatusText.Text = Path.GetFileName(path);
        PlayPauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        PlayPauseButton.Content = "Пауза";
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.Stop();
        TimeText.Text = "00:00 / 00:00";
        PlayPauseButton.Content = "Играть";
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var time = FormatTime(e.Time);
        var total = FormatTime(_lengthMs);
        App.DispatcherQueue.TryEnqueue(() => TimeText.Text = $"{time} / {total}");
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        _lengthMs = e.Length;
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() => PlayPauseButton.Content = "Пауза");
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() => PlayPauseButton.Content = "Играть");
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() => PlayPauseButton.Content = "Играть");
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            PlayPauseButton.Content = "Играть";
            StatusText.Text = "Конец файла";
        });
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() => StatusText.Text = "Ошибка воспроизведения");
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
