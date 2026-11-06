using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using TempoKaraoke.Models;
using Windows.Graphics;

namespace TempoKaraoke.Services;

/// <summary>Снимок дисплея: рабочая область, границы и эффективный DPI.</summary>
public sealed record DisplaySnapshot(
    string DisplayId,
    PlacementRect Bounds,
    PlacementRect WorkArea,
    double Scale,
    bool IsPrimary,
    nint MonitorHandle = default)
{
    public string Describe() =>
        $"display={DisplayId} scale={Scale:0.##} work={WorkArea} bounds={Bounds} primary={IsPrimary}";
}

/// <summary>Результат размещения окна — для лога и для тестов/диагностики.</summary>
public sealed record PlacementResult(
    PlacementRect FinalRect,
    string DisplayId,
    double Scale,
    string Reason,
    bool Clamped,
    bool Reset,
    bool Maximized)
{
    public string Describe() =>
        $"rect={FinalRect} display={DisplayId} scale={Scale:0.##} reason={Reason} clamped={Clamped} reset={Reset} maximized={Maximized}";
}

/// <summary>
/// Размещение главного окна: стартовый размер по рабочей области текущего дисплея,
/// центрирование, проверка сохранённого положения и его прижатие к экрану.
/// Все расчёты — в физических пикселях; сохранение — в DIP относительно WorkArea.
/// Дисплеи перечисляются через Win32 (EnumDisplayMonitors): DisplayArea.FindAll
/// в unpackaged-сборке падает с «class not registered», а Win32 работает всегда.
/// </summary>
public static class WindowPlacementService
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    private const uint MonitorInfoPrimary = 1;

    public static IReadOnlyList<DisplaySnapshot> GetDisplays()
    {
        var displays = new List<DisplaySnapshot>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<MONITORINFOEX>(),
                    szDevice = string.Empty,
                };

                if (GetMonitorInfo(monitor, ref info))
                {
                    var bounds = new PlacementRect(
                        info.rcMonitor.Left,
                        info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top);

                    var work = new PlacementRect(
                        info.rcWork.Left,
                        info.rcWork.Top,
                        info.rcWork.Right - info.rcWork.Left,
                        info.rcWork.Bottom - info.rcWork.Top);

                    displays.Add(new DisplaySnapshot(
                        info.szDevice,
                        bounds,
                        work,
                        GetScaleForMonitor(monitor),
                        (info.dwFlags & MonitorInfoPrimary) != 0,
                        monitor));
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Monitor enumeration failed: {ex.Message}");
        }

        return displays;
    }

    /// <summary>Эффективный масштаб дисплея, на котором сейчас находится окно.</summary>
    public static double GetScale(IntPtr windowHandle)
    {
        try
        {
            var dpi = GetDpiForWindow(windowHandle);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }
        catch
        {
            // Ниже — фолбэк 100%.
        }

        return 1.0;
    }

    /// <summary>
    /// Применяет сохранённое положение (если оно валидно) или центрирует окно
    /// с размером 75% рабочей области текущего дисплея.
    /// </summary>
    public static PlacementResult Apply(
        AppWindow window,
        IntPtr windowHandle,
        WindowPlacementData saved,
        bool forceReset)
    {
        var displays = GetDisplays();
        foreach (var display in displays)
        {
            LogService.Startup($"[window] {display.Describe()}");
        }

        var savedSummary = saved.UserPositioned
            ? $"user placement display={saved.DisplayId} dip={saved.X},{saved.Y} {saved.Width}x{saved.Height} savedScale={saved.DpiScale:0.##} maximized={saved.Maximized} at={saved.SavedAt}"
            : "no user placement";
        LogService.Startup($"[window] saved: {savedSummary}");

        if (displays.Count == 0)
        {
            var area = DisplayArea.GetFromWindowId(window.Id, DisplayAreaFallback.Primary);
            var work = ToRect(area.WorkArea);
            var scale = GetScale(windowHandle);
            var fallback = WindowPlacementMath.ComputeDefault(work, scale);
            window.MoveAndResize(ToRectInt32(fallback));
            LogService.Startup($"[window] placement: displays unavailable, default {fallback} scale={scale:0.##}");
            return new PlacementResult(fallback, "unknown", scale, "displays-unavailable", false, true, false);
        }

        var target = forceReset || !saved.UserPositioned
            ? null
            : displays.FirstOrDefault(d => string.Equals(d.DisplayId, saved.DisplayId, StringComparison.Ordinal));

        PlacementRect finalRect;
        string reason;
        bool clamped = false;
        bool reset = false;

        if (target is null)
        {
            target = ResolveCurrentDisplay(windowHandle, displays);
            finalRect = WindowPlacementMath.ComputeDefault(target.WorkArea, target.Scale);
            reason = forceReset
                ? "reset-requested"
                : saved.UserPositioned
                    ? "saved-display-missing"
                    : "first-run";
            reset = true;
        }
        else
        {
            var savedRect = new PlacementRect(
                target.WorkArea.X + WindowPlacementMath.ToPhysical(saved.X, target.Scale),
                target.WorkArea.Y + WindowPlacementMath.ToPhysical(saved.Y, target.Scale),
                WindowPlacementMath.ToPhysical(saved.Width, target.Scale),
                WindowPlacementMath.ToPhysical(saved.Height, target.Scale));

            finalRect = WindowPlacementMath.RestoreOrDefault(
                savedRect,
                target.WorkArea,
                target.Scale,
                out clamped,
                out reset,
                out reason);
        }

        window.MoveAndResize(ToRectInt32(finalRect));

        var maximized = !forceReset && saved.UserPositioned && saved.Maximized;
        if (maximized && window.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        var result = new PlacementResult(finalRect, target.DisplayId, target.Scale, reason, clamped, reset, maximized);
        LogService.Startup($"[window] placement: {result.Describe()}");
        return result;
    }

    /// <summary>
    /// После смены конфигурации дисплеев прижимает окно к рабочей области
    /// текущего монитора (например, второй монитор отключили).
    /// </summary>
    public static PlacementResult EnsureVisible(AppWindow window, IntPtr windowHandle, PlacementRect normalRect)
    {
        var displays = GetDisplays();
        if (displays.Count == 0 || normalRect.IsEmpty)
        {
            return new PlacementResult(normalRect, "unknown", GetScale(windowHandle), "no-displays", false, false, false);
        }

        var target = ResolveCurrentDisplay(windowHandle, displays);
        var clampedRect = WindowPlacementMath.Clamp(normalRect, target.WorkArea);
        if (clampedRect != normalRect)
        {
            window.MoveAndResize(ToRectInt32(clampedRect));
        }

        var result = new PlacementResult(
            clampedRect,
            target.DisplayId,
            target.Scale,
            clampedRect == normalRect ? "visible" : "reclamped",
            clampedRect != normalRect,
            false,
            false);
        LogService.Startup($"[window] ensure visible: {result.Describe()}");
        return result;
    }

    /// <summary>Сохраняет текущее положение окна в DIP относительно рабочей области.</summary>
    public static WindowPlacementData Capture(
        AppWindow window,
        IntPtr windowHandle,
        PlacementRect normalRect,
        bool maximized)
    {
        var scale = GetScale(windowHandle);
        var displays = GetDisplays();
        var target = displays.Count == 0 ? null : ResolveCurrentDisplay(windowHandle, displays);
        var work = target?.WorkArea ?? new PlacementRect(0, 0, 0, 0);

        return new WindowPlacementData
        {
            UserPositioned = true,
            Maximized = maximized,
            X = (int)Math.Round(WindowPlacementMath.ToDip(normalRect.X - work.X, scale)),
            Y = (int)Math.Round(WindowPlacementMath.ToDip(normalRect.Y - work.Y, scale)),
            Width = (int)Math.Round(WindowPlacementMath.ToDip(normalRect.Width, scale)),
            Height = (int)Math.Round(WindowPlacementMath.ToDip(normalRect.Height, scale)),
            DisplayId = target?.DisplayId ?? string.Empty,
            DpiScale = scale,
            SavedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
    }

    private static DisplaySnapshot ResolveCurrentDisplay(IntPtr windowHandle, IReadOnlyList<DisplaySnapshot> displays)
    {
        try
        {
            var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            var match = displays.FirstOrDefault(d => d.MonitorHandle == monitor);
            if (match is not null)
            {
                return match;
            }
        }
        catch
        {
            // Ниже — primary.
        }

        return displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }

    private static double GetScaleForMonitor(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Ниже — 100%.
        }

        return 1.0;
    }

    private static PlacementRect ToRect(RectInt32 rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static RectInt32 ToRectInt32(PlacementRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
