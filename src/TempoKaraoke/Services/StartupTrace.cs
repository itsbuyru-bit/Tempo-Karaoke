using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TempoKaraoke.Services;

/// <summary>
/// Тайминги запуска: каждая веха пишется в лог, а в конце — сводная таблица
/// «Startup timeline», по которой видно, какой этап тормозит старт.
/// </summary>
public static class StartupTrace
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<(string Name, long Milliseconds)> Marks = new();
    private static readonly object Gate = new();

    /// <summary>Запрошен сброс положения окна через --reset-window.</summary>
    public static bool ResetWindowRequested { get; set; }

    public static void Mark(string name)
    {
        lock (Gate)
        {
            var milliseconds = Clock.ElapsedMilliseconds;
            var previous = Marks.Count == 0 ? 0 : Marks[^1].Milliseconds;
            Marks.Add((name, milliseconds));
            LogService.Startup($"[startup] {name}: {milliseconds} ms (+{milliseconds - previous})");
        }
    }

    public static void DumpTimeline()
    {
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                return;
            }

            LogService.Startup(BuildTimelineText());
        }
    }

    /// <summary>Короткая сводка для шапки «Время запуска»: последний этап и время.</summary>
    public static string BuildSummaryText()
    {
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                return string.Empty;
            }

            var last = Marks[^1];
            return $"{last.Name}: {last.Milliseconds / 1000.0:0.0} s";
        }
    }

    /// <summary>Сводка таймингов для раздела «Диагностика» (без записи в лог).</summary>
    public static string BuildTimelineText()
    {
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Startup timeline:");
            long previous = 0;
            foreach (var (name, milliseconds) in Marks)
            {
                builder.AppendLine($"  {name}: +{milliseconds - previous} ms (t={milliseconds})");
                previous = milliseconds;
            }

            builder.Append($"  Total: {previous} ms");
            return builder.ToString();
        }
    }
}
