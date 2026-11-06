using System;
using System.IO;
using System.Threading;
using LibVLCSharp.Shared;

namespace TempoKaraoke.Services;

public readonly record struct MediaMetadata(long DurationMs, int Width, int Height);

/// <summary>
/// Читает длительность и разрешение видеофайла через libvlc,
/// не открывая окно вывода. Один экземпляр можно использовать из нескольких потоков.
/// </summary>
public sealed class MediaMetadataService : IDisposable
{
    private readonly LibVLC _libVlc;
    private bool _disposed;

    public MediaMetadataService()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC("--quiet");
    }

    public MediaMetadata Read(string path, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return default;
        }

        try
        {
            using var media = new Media(_libVlc, path, FromType.FromPath);
            var status = media
                .Parse(MediaParseOptions.ParseLocal, 1500, cancellationToken)
                .GetAwaiter()
                .GetResult();
            if (status is not (MediaParsedStatus.Done or MediaParsedStatus.Timeout))
            {
                return default;
            }

            var duration = Math.Max(0, media.Duration);
            var width = 0;
            var height = 0;
            var tracks = media.Tracks;
            if (tracks is not null)
            {
                foreach (var track in tracks)
                {
                    if (track.TrackType != TrackType.Video)
                    {
                        continue;
                    }

                    width = (int)track.Data.Video.Width;
                    height = (int)track.Data.Video.Height;
                    break;
                }
            }

            return new MediaMetadata(duration, width, height);
        }
        catch
        {
            // Broken or unsupported file: metadata stays unknown.
            return default;
        }
    }

    public long GetDurationMs(string path) => Read(path).DurationMs;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libVlc.Dispose();
    }
}
