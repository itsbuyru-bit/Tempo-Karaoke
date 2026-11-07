using System;
using System.Reflection;

namespace TempoKaraoke.Services;

/// <summary>Version information shown on the About page.</summary>
public static class AppInfo
{
    public static string SemanticVersion { get; } = ReadSemanticVersion();

    /// <summary>Версия для сравнения с релизами GitHub.</summary>
    public static Version CurrentVersion { get; } = ReadVersion();

    /// <summary>
    /// Версия для интерфейса: 4 сегмента, если есть ревизия (1.8.22.1),
    /// иначе три (1.8.22). Четвёртый сегмент не отбрасываем.
    /// </summary>
    public static string DisplayVersion => CurrentVersion.Revision > 0
        ? CurrentVersion.ToString(4)
        : CurrentVersion.ToString(3);

    private static Version ReadVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private static string ReadSemanticVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
