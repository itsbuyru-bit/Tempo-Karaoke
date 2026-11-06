using System;

namespace TempoKaraoke.Models;

/// <summary>
/// Прямоугольник окна или рабочей области. Единицы задаёт вызывающий код:
/// сервис размещения хранит DIP, Win32/AppWindow работают в физических пикселях.
/// </summary>
public readonly record struct PlacementRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PlacementRect FromSize(int width, int height) => new(0, 0, width, height);

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
