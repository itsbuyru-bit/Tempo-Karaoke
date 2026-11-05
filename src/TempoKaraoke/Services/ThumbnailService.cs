using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace TempoKaraoke.Services;

/// <summary>
/// Достаёт превью видео средствами Windows Shell и складывает его в кэш.
/// Если проводник не умеет построить кадр, возвращает null — карточка
/// останется с заглушкой.
/// </summary>
public static class ThumbnailService
{
    public static async Task<string?> TryCreateAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(videoPath))
            {
                return null;
            }

            var file = await StorageFile.GetFileFromPathAsync(videoPath);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.VideosView,
                320,
                ThumbnailOptions.UseCurrentScale);

            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            var folder = GetThumbnailFolder();
            var name = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(videoPath))) + ".bmp";
            var targetPath = Path.Combine(folder, name);

            using var source = thumbnail.AsStreamForRead();
            using var target = File.Create(targetPath);
            await source.CopyToAsync(target, cancellationToken);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static string GetThumbnailFolder()
    {
        var folder = AppStorage.PathFor("Thumbnails");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
