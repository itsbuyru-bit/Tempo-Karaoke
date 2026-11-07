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
        new("diagnostics", "\uE9D9", "Settings_Section_Diagnostics.Header", "Settings_Section_Diagnostics.Description"),
        new("updates", "\uE895", "Settings_Section_Updates.Header", "Settings_Section_Updates.Description"),
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
