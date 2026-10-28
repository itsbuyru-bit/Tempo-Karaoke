using System;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;

namespace TempoKaraoke.Services;

/// <summary>
/// Владелец libvlc. Умеет подключаться к разным <see cref="VideoView"/>:
/// при откреплении окна видео плеер пересоздаётся с swapchain нового окна,
/// а позиция и состояние воспроизведения переносятся.
/// </summary>
public sealed class PlayerService : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _media;
    private VideoView? _view;
    private string? _currentPath;
    private long _lastPositionMs;
    private long _lastLengthMs;
    private long _pendingSeekMs;
    private bool _resumeAfterAttach = true;
    private int _volume = 80;
    private int _volumeBeforeMute = 80;

    public PlayerService(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public event EventHandler? StateChanged;

    public event EventHandler? EndReached;

    public event EventHandler<string>? Failed;

    public string? CurrentPath => _currentPath;

    public bool IsAttached => _mediaPlayer is not null;

    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    public long PositionMs => _mediaPlayer?.Time ?? _lastPositionMs;

    public long LengthMs => _mediaPlayer?.Length ?? _lastLengthMs;

    public int Volume => _volume;

    public void Attach(VideoView view, string[] swapChainOptions)
    {
        if (_mediaPlayer is not null)
        {
            return;
        }

        try
        {
            Core.Initialize();

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
            _mediaPlayer.Volume = _volume;

            view.MediaPlayer = _mediaPlayer;
            _view = view;

            if (!string.IsNullOrWhiteSpace(_currentPath))
            {
                var path = _currentPath;
                var resume = _resumeAfterAttach;
                _pendingSeekMs = _lastPositionMs;
                PlayCore(path, resume);
            }

            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
    }

    public void Detach()
    {
        if (_mediaPlayer is not null)
        {
            _lastPositionMs = _mediaPlayer.Time;
            _lastLengthMs = _mediaPlayer.Length;
            _resumeAfterAttach = _mediaPlayer.IsPlaying;
            _volume = _mediaPlayer.Volume;

            _mediaPlayer.Stop();
            Unsubscribe();
            _mediaPlayer.Dispose();
            _mediaPlayer = null;

            if (_view is not null)
            {
                _view.MediaPlayer = null;
                _view = null;
            }
        }

        _media?.Dispose();
        _media = null;
        _libVlc?.Dispose();
        _libVlc = null;
        RaiseStateChanged();
    }

    public void Play(string path)
    {
        _currentPath = path;
        _lastPositionMs = 0;
        _pendingSeekMs = 0;
        _resumeAfterAttach = true;
        PlayCore(path, true);
    }

    public void PlayAt(string path, long positionMs, bool resume)
    {
        _currentPath = path;
        _lastPositionMs = positionMs;
        _pendingSeekMs = positionMs;
        _resumeAfterAttach = resume;
        PlayCore(path, resume);
    }

    public void PlayPause()
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
            _resumeAfterAttach = true;
            _mediaPlayer.Play();
        }
    }

    public void Pause()
    {
        if (_mediaPlayer?.IsPlaying == true)
        {
            _mediaPlayer.Pause();
        }
    }

    public void Stop()
    {
        _mediaPlayer?.Stop();
        _lastPositionMs = 0;
        _resumeAfterAttach = false;
        RaiseStateChanged();
    }

    public void Seek(long milliseconds)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        var target = Math.Clamp(milliseconds, 0, Math.Max(0, _mediaPlayer.Length));
        _mediaPlayer.Time = target;
        _lastPositionMs = target;
        RaiseStateChanged();
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        if (_mediaPlayer is not null)
        {
            _mediaPlayer.Volume = _volume;
        }

        RaiseStateChanged();
    }

    public void ToggleMute()
    {
        if (_volume == 0)
        {
            SetVolume(_volumeBeforeMute);
        }
        else
        {
            _volumeBeforeMute = _volume;
            SetVolume(0);
        }
    }

    public void Dispose()
    {
        Detach();
    }

    private void PlayCore(string path, bool play)
    {
        if (_mediaPlayer is null || _libVlc is null)
        {
            return;
        }

        _mediaPlayer.Stop();
        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);
        _lastLengthMs = 0;

        if (!_mediaPlayer.Play(_media))
        {
            Failed?.Invoke(this, $"Не удалось открыть {path}");
            return;
        }

        if (!play)
        {
            _mediaPlayer.Pause();
        }

        RaiseStateChanged();
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        _lastPositionMs = e.Time;
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        _lastLengthMs = e.Length;
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        if (_pendingSeekMs > 0)
        {
            var seek = _pendingSeekMs;
            _pendingSeekMs = 0;
            _mediaPlayer!.Time = seek;
        }

        if (!_resumeAfterAttach)
        {
            _mediaPlayer!.Pause();
        }

        RaiseStateChanged();
    }

    private void OnPaused(object? sender, EventArgs e) => RaiseStateChanged();

    private void OnStopped(object? sender, EventArgs e) => RaiseStateChanged();

    private void OnEndReached(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() => EndReached?.Invoke(this, EventArgs.Empty));
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() => Failed?.Invoke(this, "Ошибка воспроизведения"));
    }

    private void RaiseStateChanged()
    {
        _dispatcher.TryEnqueue(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void Unsubscribe()
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer.LengthChanged -= OnLengthChanged;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.Paused -= OnPaused;
        _mediaPlayer.Stopped -= OnStopped;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;
    }
}
