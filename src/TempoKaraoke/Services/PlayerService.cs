using System;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
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
    private float _rate = 1.0f;
    private Equalizer? _equalizer;

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

    /// <summary>Текущая скорость воспроизведения (1.0 = обычная).</summary>
    public float Rate => _rate;

    /// <summary>Меняет скорость воспроизведения LibVLC.</summary>
    public void SetRate(float rate)
    {
        _rate = rate;
        _mediaPlayer?.SetRate(rate);
        RaiseStateChanged();
    }

    /// <summary>
    /// Применяет эквалайзер LibVLC. LibVLCSharp 3.10 падает на SetEqualizer(null),
    /// поэтому «выключено» реализуется ровным эквалайзером (все полосы 0 дБ) —
    /// он не меняет звук. Экземпляр хранится здесь, чтобы настройки пережили
    /// пересоздание плеера при откреплении окна.
    /// </summary>
    public void SetEqualizer(Equalizer? equalizer)
    {
        var effective = equalizer ?? new Equalizer();
        var previous = _equalizer;
        _equalizer = effective;
        _mediaPlayer?.SetEqualizer(effective);
        if (!ReferenceEquals(previous, effective))
        {
            previous?.Dispose();
        }
    }
    public int AudioTrack => _mediaPlayer?.AudioTrack ?? -1;

    public IReadOnlyList<TrackDescription> AudioTracks =>
        _mediaPlayer?.AudioTrackDescription ?? Array.Empty<TrackDescription>();

    public void SetAudioTrack(int id)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.SetAudioTrack(id);
        RaiseStateChanged();
    }

    public void Attach(VideoView view, string[] swapChainOptions)
    {
        if (_mediaPlayer is not null)
        {
            return;
        }

        try
        {
            LibVLCSharp.Shared.Core.Initialize();

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
            _mediaPlayer.SetRate(_rate);
            if (_equalizer is not null)
            {
                _mediaPlayer.SetEqualizer(_equalizer);
            }

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

    /// <summary>
    /// Отключает видео от текущего VideoView, не блокируя UI-поток ожиданием VLC.
    /// </summary>
    public async Task DetachAsync()
    {
        var player = _mediaPlayer;
        if (player is not null)
        {
            _lastPositionMs = player.Time;
            _lastLengthMs = player.Length;
            _resumeAfterAttach = player.IsPlaying;
            _volume = player.Volume;

            _mediaPlayer = null;
            Unsubscribe(player);
            if (_view is not null)
            {
                _view.MediaPlayer = null;
                _view = null;
            }

            // Stop() может ждать поток событий VLC — даём ему ограниченное время.
            try
            {
                var stop = Task.Run(() =>
                {
                    try
                    {
                        player.Stop();
                    }
                    catch
                    {
                        // Плеер мог быть уже закрыт.
                    }
                });
                await Task.WhenAny(stop, Task.Delay(1500)).ConfigureAwait(false);
            }
            catch
            {
                // Не удалось дождаться остановки — всё равно освобождаем ресурсы.
            }

            player.Dispose();
        }

        _media?.Dispose();
        _media = null;
        _libVlc?.Dispose();
        _libVlc = null;
        RaiseStateChanged();
    }

    public void Detach() => DetachAsync().GetAwaiter().GetResult();

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
        var player = _mediaPlayer;
        _lastPositionMs = 0;
        _resumeAfterAttach = false;

        if (player is not null)
        {
            // Stop() ждёт поток событий VLC — не блокируем им UI.
            _ = Task.Run(() =>
            {
                try
                {
                    player.Stop();
                }
                catch
                {
                    // Плеер мог быть уже закрыт.
                }
            });
        }

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

    private static bool IsRemote(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps ||
                uri.Scheme == "rtsp");
    }

    private void PlayCore(string path, bool play)
    {
        if (_mediaPlayer is null || _libVlc is null)
        {
            return;
        }

        // ВАЖНО: не вызываем Stop() здесь. Stop() на UI-потоке ждёт поток событий VLC,
        // а тот в OnPlaying выставляет Time — получается взаимная блокировка (зависание).
        _media?.Dispose();
        _media = IsRemote(path)
            ? new Media(_libVlc, path, FromType.FromLocation)
            : new Media(_libVlc, path, FromType.FromPath);
        _lastLengthMs = 0;

        if (!_mediaPlayer.Play(_media))
        {
            Failed?.Invoke(this, $"Не удалось открыть {path}");
            return;
        }

        if (!play)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    _mediaPlayer?.Pause();
                }
                catch
                {
                    // Плеер мог быть уже закрыт.
                }
            });
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
        var seek = _pendingSeekMs;
        _pendingSeekMs = 0;
        var resume = _resumeAfterAttach;

        // Колбэк VLC нельзя использовать для вызовов в плеер: они могут ждать
        // остановки/смены медиа и вешать UI-поток. Откладываем на пул потоков.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120);
                var player = _mediaPlayer;
                if (player is null)
                {
                    return;
                }

                if (seek > 0)
                {
                    player.Time = seek;
                }

                if (!resume)
                {
                    player.Pause();
                }
            }
            catch
            {
                // Плеер мог быть уже освобождён при выходе.
            }
        });

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

    private void Unsubscribe(MediaPlayer player)
    {
        player.TimeChanged -= OnTimeChanged;
        player.LengthChanged -= OnLengthChanged;
        player.Playing -= OnPlaying;
        player.Paused -= OnPaused;
        player.Stopped -= OnStopped;
        player.EndReached -= OnEndReached;
        player.EncounteredError -= OnEncounteredError;
    }
}
