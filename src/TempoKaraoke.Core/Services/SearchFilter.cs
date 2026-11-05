using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

public sealed record SearchFilter(
    string? Folder = null,
    string? Format = null,
    long MinDurationMs = 0,
    long MaxDurationMs = long.MaxValue)
{
    public bool Matches(MediaItem item)
    {
        if (!string.IsNullOrWhiteSpace(Folder) &&
            !string.Equals(item.Folder, Folder, System.StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Format) &&
            !string.Equals(
                System.IO.Path.GetExtension(item.Path).TrimStart('.'),
                Format.TrimStart('.'),
                System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (MinDurationMs > 0 || MaxDurationMs != long.MaxValue)
        {
            if (item.DurationMs <= 0)
            {
                return false;
            }

            return item.DurationMs >= MinDurationMs && item.DurationMs <= MaxDurationMs;
        }

        return true;
    }
}
