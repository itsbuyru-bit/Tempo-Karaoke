using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Windows.ApplicationModel.Resources;
using TempoKaraoke.Locales;
using Windows.Globalization;
using Windows.System.UserProfile;

namespace TempoKaraoke.Services;

/// <summary>
/// Data-driven BCP 47 localization. The supported language list and the
/// translations come from localization/manifest.json and the flat JSON catalogs
/// next to it. The compiled .resw resources stay in place as the MRT fallback
/// for the initial XAML render; runtime strings prefer the JSON catalog.
/// </summary>
public sealed class LocalizationService : DependencyObject
{
    public const string DefaultLanguage = LocalizationCatalog.DefaultLanguage;

    private static readonly List<WeakReference<DependencyObject>> Targets = new();
    private static readonly ResourceManager ResourceManager = new();
    private static readonly Lazy<LocalizationCatalog> LazyCatalog = new(LoadCatalog);
    private static string _language = DefaultLanguage;
    private static bool _initialized;

    public static string CurrentLanguage => _language;

    /// <summary>The manifest is the single source of the language selector.</summary>
    public static IReadOnlyList<LocalizationLanguage> Languages => Catalog.Languages;

    public static string FallbackLanguage => Catalog.FallbackTag;

    public static IReadOnlyList<LocalizationIssue> Issues => Catalog.Issues;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(LocalizationService),
        new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty SecondaryKeyProperty = DependencyProperty.RegisterAttached(
        "SecondaryKey",
        typeof(string),
        typeof(LocalizationService),
        new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty ToolTipKeyProperty = DependencyProperty.RegisterAttached(
        "ToolTipKey",
        typeof(string),
        typeof(LocalizationService),
        new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty AutomationNameKeyProperty = DependencyProperty.RegisterAttached(
        "AutomationNameKey",
        typeof(string),
        typeof(LocalizationService),
        new PropertyMetadata(null, OnKeyChanged));

    public LocalizationService()
    {
    }

    static LocalizationService()
    {
        Initialize();
    }

    private static LocalizationCatalog Catalog => LazyCatalog.Value;

    /// <summary>
    /// Resolves the startup language: saved choice → Windows UI language →
    /// language-group match → en-US. An automatically chosen language is stored
    /// right away so the next launch keeps it.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            ApplyAll();
            return;
        }

        _initialized = true;
        var saved = SettingsService.LanguageConfigured ? SettingsService.Language : null;
        var resolution = LanguageResolver.Resolve(Catalog, saved, ReadSystemLanguages());
        ApplyLanguage(resolution.Tag, persist: false);

        if (resolution.ShouldPersist ||
            !string.Equals(SettingsService.Language, resolution.Tag, StringComparison.Ordinal))
        {
            SettingsService.Language = resolution.Tag;
        }

        foreach (var issue in Catalog.Issues)
        {
            LogService.Warn($"Localization: {issue}");
        }
    }

    /// <summary>Reads the Windows display-language list, tolerant of unpackaged runs.</summary>
    public static IReadOnlyList<string> ReadSystemLanguages()
    {
        try
        {
            return GlobalizationPreferences.Languages.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GlobalizationPreferences unavailable: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static string GetSecondaryKey(DependencyObject element) => (string)element.GetValue(SecondaryKeyProperty);

    public static void SetSecondaryKey(DependencyObject element, string value) => element.SetValue(SecondaryKeyProperty, value);

    public static string GetToolTipKey(DependencyObject element) => (string)element.GetValue(ToolTipKeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value) => element.SetValue(ToolTipKeyProperty, value);

    public static string GetAutomationNameKey(DependencyObject element) => (string)element.GetValue(AutomationNameKeyProperty);

    public static void SetAutomationNameKey(DependencyObject element, string value) => element.SetValue(AutomationNameKeyProperty, value);

    /// <summary>
    /// Maps any tag to an enabled manifest language. No language list lives in
    /// code: adding a language only changes manifest.json and its JSON file.
    /// </summary>
    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return _initialized ? Catalog.ResolveTag(_language) : DefaultLanguage;
        }

        return Catalog.ResolveTag(language);
    }

    /// <summary>Переключает интерфейс на язык Windows и больше его не запоминает.</summary>
    public static void UseSystemLanguage()
    {
        var resolution = LanguageResolver.Resolve(Catalog, null, ReadSystemLanguages());
        ApplyLanguage(resolution.Tag, persist: false);
        SettingsService.UseSystemLanguage();
    }

    public static void SetLanguage(string language, bool save = true)
    {
        var normalized = NormalizeLanguage(language);
        ApplyLanguage(normalized, save);
    }

    private static void ApplyLanguage(string normalized, bool persist)
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = normalized;
        }
        catch
        {
            // Unpackaged/dev runs may not support the override.
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            Debug.WriteLine($"Unknown culture {normalized}: {ex.Message}");
        }

        var changed = !string.Equals(_language, normalized, StringComparison.OrdinalIgnoreCase);
        _language = normalized;
        if (persist && changed)
        {
            SettingsService.Language = normalized;
        }

        ApplyAll();
    }

    /// <summary>Applies a language and refreshes every registered XAML target.</summary>
    public static void Apply(DependencyObject root, string language)
    {
        SetLanguage(language);
    }

    /// <summary>Returns a resource string for the current language.</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var lookup = Catalog.Lookup(_language, key);
        if (lookup.Found)
        {
            if (lookup.FromFallback && !string.Equals(_language, Catalog.FallbackTag, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Localization: {_language} misses '{key}', using {Catalog.FallbackTag}.");
            }

            return lookup.Value;
        }

        // Legacy MRT path: keeps x:Uid-only resources working if a key is not in
        // the JSON catalogs yet.
        try
        {
            var context = ResourceManager.CreateResourceContext();
            context.QualifierValues["Language"] = _language;
            var resourcePath = "Resources/" + key.Replace('.', '/');
            var candidate = ResourceManager.MainResourceMap.GetValue(resourcePath, context);
            if (candidate is not null && !string.IsNullOrEmpty(candidate.ValueAsString))
            {
                return candidate.ValueAsString;
            }
        }
        catch
        {
            // Fall through to the ResourceLoader path and then the key itself.
        }

        try
        {
            var value = new ResourceLoader().GetString(key);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch
        {
            // Missing key in both JSON and MRT.
        }

        Debug.WriteLine($"Localization: key '{key}' is missing in {_language} and {Catalog.FallbackTag}.");
        LogService.Warn($"Localization: missing key '{key}' for {_language} and {Catalog.FallbackTag}.");
        return key;
    }

    /// <summary>Selects the plural resource for a numeric count.</summary>
    public static string GetPlural(string keyBase, int count)
    {
        var lookup = Catalog.LookupPlural(_language, keyBase, count);
        if (lookup.Found)
        {
            return lookup.Value;
        }

        Debug.WriteLine($"Localization: plural '{keyBase}' is missing in {_language} and {Catalog.FallbackTag}.");
        return $"{keyBase}_{PluralCategory.For(_language, count)}";
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static string FormatPlural(string keyBase, int count, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetPlural(keyBase, count), args);
    }

    /// <summary>Resolves a key for a specific language without switching the UI.</summary>
    public static string Translate(string key, string language)
    {
        return Catalog.Get(Catalog.ResolveTag(language), key);
    }

    public static string SongCount(int count) => FormatPlural("Plural_SongCount", count, count);

    public static string ShownSummary(int groups, int files) =>
        Format("Library_ShowingSummary", groups, files);

    public static string AllFoldersLabel => Get("Library_AllFolders");

    public static bool IsAllFolders(string? value) =>
        string.Equals(value, AllFoldersLabel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Все папки", StringComparison.OrdinalIgnoreCase);

    private static LocalizationCatalog LoadCatalog()
    {
        var directory = LocalizationPaths.FindCatalogDirectory();
        var catalog = LocalizationCatalog.Load(directory);
        if (!catalog.IsUsable)
        {
            Debug.WriteLine(
                $"Localization catalog is unusable at '{directory}': " +
                string.Join("; ", catalog.Issues.Select(issue => issue.ToString())));
        }

        return catalog;
    }

    private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        Register(element);
        ApplyElement(element);
    }

    public static void RegisterXamlElement(DependencyObject element)
    {
        try
        {
            Register(element);
            ApplyElement(element);
        }
        catch (Exception ex)
        {
            // Ошибка локализации не должна ломать загрузку XAML: элемент просто
            // останется с текстом из разметки, а причина попадёт в лог.
            LogService.Crash(ex, "Localization.ApplyElement");
        }
    }

    private static void Register(DependencyObject element)
    {
        foreach (var reference in Targets)
        {
            if (reference.TryGetTarget(out var target) && ReferenceEquals(target, element))
            {
                return;
            }
        }

        Targets.Add(new WeakReference<DependencyObject>(element));
    }

    private static void ApplyAll()
    {
        for (var index = Targets.Count - 1; index >= 0; index--)
        {
            if (Targets[index].TryGetTarget(out var target))
            {
                try
                {
                    ApplyElement(target);
                }
                catch
                {
                    // A template part may be detached; keep updating the rest.
                }
            }
            else
            {
                Targets.RemoveAt(index);
            }
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        ApplyProperty(element, TempoKaraoke.Localization.GetKey(element), isSecondary: false);
        ApplyProperty(element, TempoKaraoke.Localization.GetSecondaryKey(element), isSecondary: true);

        var tooltipKey = TempoKaraoke.Localization.GetToolTipKey(element);
        if (!string.IsNullOrWhiteSpace(tooltipKey) && element is FrameworkElement tooltipTarget)
        {
            ToolTipService.SetToolTip(tooltipTarget, Get(tooltipKey));
        }

        var automationKey = TempoKaraoke.Localization.GetAutomationNameKey(element);
        if (!string.IsNullOrWhiteSpace(automationKey))
        {
            AutomationProperties.SetName(element, Get(automationKey));
        }
    }

    private static void ApplyProperty(DependencyObject element, string key, bool isSecondary)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var value = Get(key);
        var property = key[(key.LastIndexOf('.') + 1)..];

        switch (property)
        {
            case "Text":
                if (element is TextBlock textBlock)
                {
                    textBlock.Text = value;
                }
                else
                {
                    SetProperty(element, "Text", value);
                }
                break;
            case "Content":
                if (element is ContentControl content)
                {
                    content.Content = value;
                }
                break;
            case "Header":
                SetProperty(element, "Header", value);
                break;
            case "Description":
                SetProperty(element, "Description", value);
                break;
            case "PlaceholderText":
                if (element is TextBox textBox)
                {
                    textBox.PlaceholderText = value;
                }
                else if (element is AutoSuggestBox autoSuggest)
                {
                    autoSuggest.PlaceholderText = value;
                }
                break;
            case "OffContent":
                if (element is ToggleSwitch offToggle)
                {
                    offToggle.OffContent = value;
                }
                break;
            case "OnContent":
                if (element is ToggleSwitch onToggle)
                {
                    onToggle.OnContent = value;
                }
                break;
            default:
                if (isSecondary)
                {
                    SetProperty(element, "Description", value);
                }
                break;
        }
    }

    private static void SetProperty(object target, string name, string value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(target, value);
        }
        else if (property?.CanWrite == true && property.PropertyType == typeof(object))
        {
            property.SetValue(target, value);
        }
    }
}

/// <summary>Locates the localization folder in packaged, portable and dev runs.</summary>
public static class LocalizationPaths
{
    public static string FindCatalogDirectory()
    {
        var candidates = new List<string>();
        var direct = Path.Combine(AppContext.BaseDirectory, "localization");
        candidates.Add(direct);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            candidates.Add(Path.Combine(directory.FullName, "localization"));
            directory = directory.Parent;
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(Path.Combine(candidate, LocalizationManifestLoader.FileName)))
                {
                    return candidate;
                }
            }
            catch
            {
                // Keep looking.
            }
        }

        return direct;
    }
}
