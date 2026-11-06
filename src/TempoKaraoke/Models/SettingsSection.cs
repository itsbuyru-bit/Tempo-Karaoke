namespace TempoKaraoke.Models;

/// <summary>Карточка раздела на главном экране настроек.</summary>
public sealed class SettingsSection
{
    public string Id { get; init; } = string.Empty;

    public string Icon { get; init; } = string.Empty;

    public string TitleKey { get; init; } = string.Empty;

    public string DescriptionKey { get; init; } = string.Empty;

    /// <summary>Локализованный заголовок на момент построения каталога.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Локализованное описание на момент построения каталога.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>Прямая ссылка на конкретный параметр из поиска.</summary>
public sealed record SettingsSearchHit(string SectionId, string Title, string SectionTitle, string? ControlName);
