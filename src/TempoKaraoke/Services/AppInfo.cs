using System;
using System.Reflection;
using Windows.ApplicationModel;

namespace TempoKaraoke.Services;

/// <summary>Version information shown on the About page.</summary>
public static class AppInfo
{
    public static string SemanticVersion { get; } = ReadSemanticVersion();

    public static string PackageVersion { get; } = ReadPackageVersion();

    /// <summary>
    /// true, когда приложение запущено из MSIX. В портативной сборке identity нет,
    /// поэтому версия читается из assembly, а самообновление выполняется установщиком.
    /// </summary>
    public static bool IsPackaged { get; } = DetectPackaged();

    /// <summary>Версия для сравнения с релизами GitHub.</summary>
    public static Version CurrentVersion { get; } = ReadVersion();

    private static bool DetectPackaged()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Version ReadVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return new Version(version.Major, version.Minor, version.Build, version.Revision);
        }
        catch
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        }
    }

    private static string ReadSemanticVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null
                ? "1.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private static string ReadPackageVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return SemanticVersion;
        }
    }
}
