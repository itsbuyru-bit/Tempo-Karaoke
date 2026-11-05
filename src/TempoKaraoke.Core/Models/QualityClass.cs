namespace TempoKaraoke.Models;

/// <summary>
/// Класс качества по разрешению кадра. Используется для двух вещей: выбрать
/// победителя среди технических дублей и решить, нужен ли тег в имени файла.
/// </summary>
public enum QualityClass
{
    Unknown = 0,
    Low = 1,
    Sd = 2,
    Hd720 = 3,
    Hd1080 = 4,
    Uhd4K = 5,
}

public static class QualityClassExtensions
{
    /// <summary>Разрешение кадра в класс качества. 1080p и выше считаются нормой.</summary>
    public static QualityClass FromDimensions(int width, int height)
    {
        var shortSide = width > 0 && height > 0 ? System.Math.Min(width, height) : 0;
        return shortSide switch
        {
            0 => QualityClass.Unknown,
            >= 2000 => QualityClass.Uhd4K,
            >= 1050 => QualityClass.Hd1080,
            >= 700 => QualityClass.Hd720,
            >= 470 => QualityClass.Sd,
            _ => QualityClass.Low,
        };
    }

    /// <summary>
    /// Тег для имени файла. Норма (1080p и 4K) не помечается: иначе суффикс
    /// появится у большинства файлов и перестанет что-либо сообщать.
    /// </summary>
    public static string? FileTag(this QualityClass quality) => quality switch
    {
        QualityClass.Hd720 => "[720p]",
        QualityClass.Sd => "[480p]",
        QualityClass.Low => "[SD]",
        _ => null,
    };

    /// <summary>Короткая подпись для бейджа в интерфейсе.</summary>
    public static string BadgeLabel(this QualityClass quality) => quality switch
    {
        QualityClass.Uhd4K => "4K",
        QualityClass.Hd1080 => "1080p",
        QualityClass.Hd720 => "720p",
        QualityClass.Sd => "480p",
        QualityClass.Low => "SD",
        _ => "—",
    };
}
