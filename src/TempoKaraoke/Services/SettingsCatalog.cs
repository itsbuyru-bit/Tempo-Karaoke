using System.Collections.Generic;

namespace TempoKaraoke.Services;

/// <summary>
/// Декларативный каталог разделов «Параметров». Чтобы добавить седьмой раздел,
/// достаточно дописать запись сюда и панель Section* в MainPage.xaml.
/// </summary>
public static class SettingsCatalog
{
    public sealed record SectionDefinition(string Id, string Icon, string TitleKey, string DescriptionKey);

    public sealed record ParameterDefinition(string SectionId, string TitleKey, string ControlName);

    /// <summary>Порядок карточек на главном экране настроек.</summary>
    public static IReadOnlyList<SectionDefinition> Sections { get; } =
    [
        new("interface", "\uE790", "Settings_Section_Interface.Header", "Settings_Section_Interface.Description"),
        new("libraries", "\uE8B7", "Settings_Section_Libraries.Header", "Settings_Section_Libraries.Description"),
        new("playback", "\uE768", "Settings_Section_Playback.Header", "Settings_Section_Playback.Description"),
        new("remote", "\uE8B3", "Settings_Section_Remote.Header", "Settings_Section_Remote.Description"),
        new("security", "\uE72E", "Settings_Section_Security.Header", "Settings_Section_Security.Description"),
        new("diagnostics", "\uE9D9", "Settings_Section_Diagnostics.Header", "Settings_Section_Diagnostics.Description"),
    ];

    /// <summary>
    /// Параметры для поиска и перехода с подсветкой. ControlName ищется через
    /// FindName: если контрол переименовали, переход просто откроет раздел.
    /// </summary>
    public static IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("interface", "Settings_Section_Interface_Language.Header", "LanguageComboBox"),
        new("interface", "Ui_ChooseAThemeOrUseTheSystemTheme.Header", "ThemeComboBox"),
        new("interface", "Settings_WindowPlacement.Header", "ResetWindowPlacementButton"),
        new("libraries", "Settings_Section_Libraries_Refresh.Header", "RefreshLibrariesButton"),
        new("playback", "Ui_CountdownOnVideoBeforeTheNextSongInTheQu.Header", "CountdownSecondsBox"),
        new("playback", "Ui_CountdownBeforeNextTrack.OnContent", "CountdownToggle"),
        new("remote", "Ui_WifiSafeMode.Header", "WifiSafeModeToggle"),
        new("security", "Ui_AccessPins.Header", "PersistentPinToggle"),
        new("diagnostics", "Ui_Logging.OnContent", "LoggingToggle"),
    ];

    public static SectionDefinition? Find(string id)
    {
        foreach (var section in Sections)
        {
            if (section.Id == id)
            {
                return section;
            }
        }

        return null;
    }
}
