using System;
using TempoKaraoke.Models;

namespace TempoKaraoke.Services;

/// <summary>
/// Чистая геометрия размещения окна: размер по умолчанию, центрирование,
/// прижатие к рабочей области и проверка валидности сохранённого прямоугольника.
/// Никаких вызовов Win32/WinRT — поэтому покрывается тестами.
/// </summary>
public static class WindowPlacementMath
{
    /// <summary>Минимальный разумный размер окна в DIP.</summary>
    public const int MinWidthDip = 1024;

    public const int MinHeightDip = 700;

    /// <summary>Максимальный стартовый размер в DIP: на 4K окно не должно быть гигантским.</summary>
    public const int MaxWidthDip = 1920;

    public const int MaxHeightDip = 1200;

    /// <summary>Доля рабочей области для стартового размера.</summary>
    public const double WorkAreaFraction = 0.75;

    /// <summary>Сколько DIP окна должно быть видно, чтобы считать позицию пригодной.</summary>
    public const int MinVisibleDip = 100;

    /// <summary>Какая доля площади окна должна попадать в рабочую область.</summary>
    public const double MinVisibleFraction = 0.2;

    /// <summary>Перевод DIP в физические пиксели для текущего масштаба.</summary>
    public static int ToPhysical(double dip, double scale)
    {
        var safeScale = scale > 0 ? scale : 1;
        return (int)Math.Round(dip * safeScale, MidpointRounding.AwayFromZero);
    }

    public static double ToDip(int physical, double scale)
    {
        var safeScale = scale > 0 ? scale : 1;
        return physical / safeScale;
    }

    /// <summary>
    /// Стартовый размер: 75% рабочей области, но не меньше минимума и не больше
    /// максимума (оба предела заданы в DIP и переводятся в пиксели текущего дисплея).
    /// </summary>
    public static PlacementRect ComputeDefault(PlacementRect workArea, double scale)
    {
        if (workArea.IsEmpty)
        {
            return new PlacementRect(0, 0, 0, 0);
        }

        var minWidth = Math.Min(ToPhysical(MinWidthDip, scale), workArea.Width);
        var minHeight = Math.Min(ToPhysical(MinHeightDip, scale), workArea.Height);
        var maxWidth = Math.Max(minWidth, Math.Min(ToPhysical(MaxWidthDip, scale), workArea.Width));
        var maxHeight = Math.Max(minHeight, Math.Min(ToPhysical(MaxHeightDip, scale), workArea.Height));

        var width = (int)Math.Round(workArea.Width * WorkAreaFraction, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(workArea.Height * WorkAreaFraction, MidpointRounding.AwayFromZero);

        width = Math.Clamp(width, minWidth, maxWidth);
        height = Math.Clamp(height, minHeight, maxHeight);

        return Center(new PlacementRect(0, 0, width, height), workArea);
    }

    /// <summary>Центрирование внутри рабочей области (не под taskbar).</summary>
    public static PlacementRect Center(PlacementRect rect, PlacementRect workArea)
    {
        return new PlacementRect(
            workArea.X + (workArea.Width - rect.Width) / 2,
            workArea.Y + (workArea.Height - rect.Height) / 2,
            rect.Width,
            rect.Height);
    }

    /// <summary>Прижимает окно внутрь рабочей области, сохраняя его размер (если он помещается).</summary>
    public static PlacementRect Clamp(PlacementRect rect, PlacementRect workArea, int margin = 0)
    {
        if (workArea.IsEmpty || rect.IsEmpty)
        {
            return rect;
        }

        var usableWidth = Math.Max(1, workArea.Width - (margin * 2));
        var usableHeight = Math.Max(1, workArea.Height - (margin * 2));

        var width = Math.Min(rect.Width, usableWidth);
        var height = Math.Min(rect.Height, usableHeight);

        var x = Math.Clamp(rect.X, workArea.X + margin, workArea.X + margin + (usableWidth - width));
        var y = Math.Clamp(rect.Y, workArea.Y + margin, workArea.Y + margin + (usableHeight - height));

        return new PlacementRect(x, y, width, height);
    }

    /// <summary>Доля площади окна, попадающая в рабочую область.</summary>
    public static double VisibleFraction(PlacementRect rect, PlacementRect workArea)
    {
        if (rect.IsEmpty || workArea.IsEmpty)
        {
            return 0;
        }

        var left = Math.Max(rect.X, workArea.X);
        var top = Math.Max(rect.Y, workArea.Y);
        var right = Math.Min(rect.Right, workArea.Right);
        var bottom = Math.Min(rect.Bottom, workArea.Bottom);

        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        var visible = (double)width * height;
        var total = (double)rect.Width * rect.Height;

        return total <= 0 ? 0 : visible / total;
    }

    /// <summary>Проверка, что сохранённое окно не уехало за экран целиком или почти целиком.</summary>
    public static bool IsVisibleEnough(
        PlacementRect rect,
        PlacementRect workArea,
        double scale,
        int minVisibleDip = MinVisibleDip,
        double minFraction = MinVisibleFraction)
    {
        if (rect.IsEmpty || workArea.IsEmpty)
        {
            return false;
        }

        var visibleWidth = Math.Min(rect.Right, workArea.Right) - Math.Max(rect.X, workArea.X);
        var visibleHeight = Math.Min(rect.Bottom, workArea.Bottom) - Math.Max(rect.Y, workArea.Y);

        if (visibleWidth < ToPhysical(minVisibleDip, scale) || visibleHeight < ToPhysical(minVisibleDip, scale))
        {
            return false;
        }

        return VisibleFraction(rect, workArea) >= minFraction;
    }

    /// <summary>
    /// Восстанавливает прямоугольник окна или возвращает безопасный центрированный
    /// размер. Возвращает причину, чтобы сервис записал её в лог.
    /// </summary>
    public static PlacementRect RestoreOrDefault(
        PlacementRect? saved,
        PlacementRect workArea,
        double scale,
        out bool clamped,
        out bool reset,
        out string reason)
    {
        clamped = false;
        reset = false;

        if (workArea.IsEmpty)
        {
            reason = "empty-work-area";
            reset = true;
            return new PlacementRect(0, 0, 0, 0);
        }

        var defaultRect = ComputeDefault(workArea, scale);

        if (saved is null || saved.Value.IsEmpty)
        {
            reason = "no-saved-placement";
            reset = true;
            return defaultRect;
        }

        var candidate = saved.Value;

        if (!IsVisibleEnough(candidate, workArea, scale))
        {
            reason = "saved-rect-offscreen";
            reset = true;
            return defaultRect;
        }

        // Слишком большой или слишком маленький для текущего монитора — поправляем размер.
        var minWidth = Math.Min(ToPhysical(MinWidthDip, scale), workArea.Width);
        var minHeight = Math.Min(ToPhysical(MinHeightDip, scale), workArea.Height);
        var maxWidth = Math.Max(minWidth, Math.Min(ToPhysical(MaxWidthDip, scale), workArea.Width));
        var maxHeight = Math.Max(minHeight, Math.Min(ToPhysical(MaxHeightDip, scale), workArea.Height));

        var width = Math.Clamp(candidate.Width, minWidth, maxWidth);
        var height = Math.Clamp(candidate.Height, minHeight, maxHeight);

        if (width != candidate.Width || height != candidate.Height)
        {
            clamped = true;
        }

        var limited = new PlacementRect(candidate.X, candidate.Y, width, height);

        var clampedRect = Clamp(limited, workArea);
        if (clampedRect != limited)
        {
            clamped = true;
        }

        reason = clamped ? "saved-rect-clamped" : "saved-rect";
        return clampedRect;
    }
}
