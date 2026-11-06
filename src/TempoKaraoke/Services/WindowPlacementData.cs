namespace TempoKaraoke.Services;

/// <summary>
/// Сохранённое положение окна. X/Y/Width/Height — в DIP (логических пикселях)
/// относительно рабочей области дисплея, поэтому перенос настроек на монитор
/// с другим DPI/разрешением не превращается в окно за экраном.
/// </summary>
public sealed class WindowPlacementData
{
    /// <summary>True только после реального действия пользователя (перемещение/размер).</summary>
    public bool UserPositioned { get; set; }

    public bool Maximized { get; set; }

    /// <summary>Смещение от левого края рабочей области дисплея, DIP.</summary>
    public int X { get; set; }

    /// <summary>Смещение от верхнего края рабочей области дисплея, DIP.</summary>
    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Идентификатор дисплея на момент сохранения (DisplayArea.DisplayId).</summary>
    public string DisplayId { get; set; } = string.Empty;

    /// <summary>Масштаб Windows на момент сохранения (1.0, 1.25, 1.5, 2.0).</summary>
    public double DpiScale { get; set; } = 1.0;

    public string SavedAt { get; set; } = string.Empty;

    public WindowPlacementData Clone() => new()
    {
        UserPositioned = UserPositioned,
        Maximized = Maximized,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        DisplayId = DisplayId,
        DpiScale = DpiScale,
        SavedAt = SavedAt,
    };
}
