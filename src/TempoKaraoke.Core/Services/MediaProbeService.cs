using System;
using System.IO;

namespace TempoKaraoke.Services;

/// <summary>
/// Быстрое чтение разрешения кадра прямо из заголовка контейнера: tkhd в MP4,
/// PixelWidth/PixelHeight в EBML/MKV. Это в сотни раз дешевле, чем поднимать
/// LibVLC ради двух чисел, — 6900 файлов проходятся за секунды.
/// LibVLC остаётся фолбэком: если метод вернул null, вызывающая сторона решает сама.
/// </summary>
public static class MediaProbeService
{
    private const int HeaderBytes = 2_000_000;

    public static (int Width, int Height)? TryReadDimensions(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" or ".mov" => ReadMp4(path),
                ".mkv" or ".webm" => ReadMkv(path),
                _ => null,
            };
        }
        catch (Exception)
        {
            // Битый или недокачанный файл — не повод падать всей проверке.
            return null;
        }
    }

    private static (int, int)? ReadMp4(string path)
    {
        using var stream = File.OpenRead(path);
        var total = stream.Length;

        var moov = FindBox(stream, 0, total, "moov");
        if (moov is null)
        {
            return null;
        }

        (int Width, int Height)? fallback = null;

        foreach (var trak in FindBoxes(stream, moov.Value.Start, moov.Value.End, "trak"))
        {
            var isVideo = false;
            (int, int)? dimensions = null;

            foreach (var child in FindBoxes(stream, trak.Start, trak.End, null))
            {
                if (child.Type == "tkhd" && child.End - child.Start >= 8)
                {
                    stream.Seek(child.End - 8, SeekOrigin.Begin);
                    var tail = new byte[8];
                    stream.ReadExactly(tail);
                    var width = (int)(ReadBigEndianUInt32(tail, 0) >> 16);
                    var height = (int)(ReadBigEndianUInt32(tail, 4) >> 16);
                    if (width > 0 && height > 0)
                    {
                        dimensions = (width, height);
                    }
                }
                else if (child.Type == "mdia")
                {
                    foreach (var inner in FindBoxes(stream, child.Start, child.End, "hdlr"))
                    {
                        if (inner.End - inner.Start >= 12)
                        {
                            stream.Seek(inner.Start + 8, SeekOrigin.Begin);
                            var handler = new byte[4];
                            stream.ReadExactly(handler);
                            isVideo = handler[0] == (byte)'v' && handler[1] == (byte)'i';
                        }
                    }
                }
            }

            if (dimensions is not null)
            {
                if (isVideo)
                {
                    return dimensions;
                }

                fallback ??= dimensions;
            }
        }

        return fallback;
    }

    private static (int, int)? ReadMkv(string path)
    {
        using var stream = File.OpenRead(path);
        var length = (int)Math.Min(stream.Length, HeaderBytes);
        var buffer = new byte[length];
        stream.ReadExactly(buffer);

        int? width = null;
        int? height = null;
        ParseEbml(buffer, 0, length, 0, ref width, ref height);

        return width is > 0 && height is > 0 ? (width.Value, height.Value) : null;
    }

    private static readonly ulong[] MasterIds =
    {
        0x18538067, // Segment
        0x1654AE6B, // Tracks
        0xAE,       // TrackEntry
        0xE0,       // Video
    };

    private static void ParseEbml(byte[] buffer, int start, int end, int depth, ref int? width, ref int? height)
    {
        var position = start;
        var guard = 0;

        while (position < end && guard++ < 5000)
        {
            var id = ReadVintRaw(buffer, position, out var idLength);
            if (id is null)
            {
                return;
            }

            var size = ReadVintSize(buffer, position + idLength, out var sizeLength);
            if (size is null)
            {
                return;
            }

            var body = position + idLength + sizeLength;
            if (body > end)
            {
                return;
            }

            var isUnknownSize = size.Value == long.MaxValue;
            var bodyEnd = isUnknownSize ? end : body + (int)size.Value;

            if (id.Value == 0xB0 && !isUnknownSize && size.Value is > 0 and <= 4 && bodyEnd <= end)
            {
                width = (int)ReadBigEndianUInt32(buffer, body, (int)size.Value);
            }
            else if (id.Value == 0xBA && !isUnknownSize && size.Value is > 0 and <= 4 && bodyEnd <= end)
            {
                height = (int)ReadBigEndianUInt32(buffer, body, (int)size.Value);
            }

            var isMaster = Array.IndexOf(MasterIds, id.Value) >= 0;
            if (isMaster && depth < 6)
            {
                // Размер сегмента часто больше доступного буфера — разбираем то,
                // что дочитали, вместо того чтобы сдаться.
                ParseEbml(buffer, body, Math.Min(bodyEnd, end), depth + 1, ref width, ref height);
            }

            if (isUnknownSize)
            {
                return;
            }

            position = bodyEnd;
        }
    }

    private static ulong? ReadVintRaw(byte[] buffer, int position, out int length)
    {
        length = 0;
        if (position >= buffer.Length)
        {
            return null;
        }

        var first = buffer[position];
        if (first == 0)
        {
            return null;
        }

        var mask = 0x80;
        while ((first & mask) == 0)
        {
            mask >>= 1;
            length++;
            if (length > 8)
            {
                return null;
            }
        }

        length++;
        ulong value = 0;
        for (var index = 0; index < length; index++)
        {
            if (position + index >= buffer.Length)
            {
                return null;
            }

            value = (value << 8) | buffer[position + index];
        }

        return value;
    }

    private static long? ReadVintSize(byte[] buffer, int position, out int length)
    {
        length = 0;
        if (position >= buffer.Length)
        {
            return null;
        }

        var first = buffer[position];
        if (first == 0)
        {
            return null;
        }

        var mask = 0x80;
        while ((first & mask) == 0)
        {
            mask >>= 1;
            length++;
            if (length > 8)
            {
                return null;
            }
        }

        length++;
        ulong value = (ulong)(first & (0xFF >> length));
        for (var index = 1; index < length; index++)
        {
            if (position + index >= buffer.Length)
            {
                return null;
            }

            value = (value << 8) | buffer[position + index];
        }

        // Все единицы — «размер неизвестен».
        var allOnes = length == 8 && value == 0x01FFFFFFFFFFFFFF;
        return allOnes ? long.MaxValue : (long)value;
    }

    private static uint ReadBigEndianUInt32(byte[] buffer, int offset)
    {
        return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
    }

    private static uint ReadBigEndianUInt32(byte[] buffer, int offset, int length)
    {
        uint value = 0;
        for (var index = 0; index < length; index++)
        {
            value = (value << 8) | buffer[offset + index];
        }

        return value;
    }

    private readonly record struct Box(string Type, long Start, long End);

    private static Box? FindBox(FileStream stream, long start, long end, string type)
    {
        foreach (var box in FindBoxes(stream, start, end, type))
        {
            return box;
        }

        return null;
    }

    private static System.Collections.Generic.IEnumerable<Box> FindBoxes(
        FileStream stream,
        long start,
        long end,
        string? type)
    {
        var position = start;
        var header = new byte[16];

        while (position + 8 <= end)
        {
            stream.Seek(position, SeekOrigin.Begin);
            var read = stream.Read(header, 0, 16);
            if (read < 8)
            {
                yield break;
            }

            long size = ReadBigEndianUInt32(header, 0);
            var boxType = string.Create(4, header, (span, source) =>
            {
                for (var index = 0; index < 4; index++)
                {
                    span[index] = (char)source[4 + index];
                }
            });

            long body = position + 8;
            if (size == 1)
            {
                if (read < 16)
                {
                    yield break;
                }

                size = ((long)ReadBigEndianUInt32(header, 8) << 32) | ReadBigEndianUInt32(header, 12);
                body = position + 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < 8 || position + size > end)
            {
                yield break;
            }

            if (type is null || string.Equals(boxType, type, StringComparison.Ordinal))
            {
                yield return new Box(boxType, body, position + size);
            }

            position += size;
        }
    }
}
