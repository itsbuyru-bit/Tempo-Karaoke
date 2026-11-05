using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Globalization;

namespace TempoKaraoke.Services;

/// <summary>
/// BCP 47 localization. Strings live in Strings/en-US and Strings/ru-RU .resw files.
/// The attached properties keep created elements in sync when the language changes,
/// while x:Uid provides the standard MRT binding for the initial visual tree.
/// </summary>
public sealed class LocalizationService : DependencyObject
{
    public const string DefaultLanguage = "en-US";
    public const string RussianLanguage = "ru-RU";

    private static readonly List<WeakReference<DependencyObject>> Targets = new();
    private static readonly ResourceManager ResourceManager = new();
    private static string _language = DefaultLanguage;

    public static string CurrentLanguage => _language;

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
        SetLanguage(SettingsService.Language, save: false);
    }

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static string GetSecondaryKey(DependencyObject element) => (string)element.GetValue(SecondaryKeyProperty);

    public static void SetSecondaryKey(DependencyObject element, string value) => element.SetValue(SecondaryKeyProperty, value);

    public static string GetToolTipKey(DependencyObject element) => (string)element.GetValue(ToolTipKeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value) => element.SetValue(ToolTipKeyProperty, value);

    public static string GetAutomationNameKey(DependencyObject element) => (string)element.GetValue(AutomationNameKeyProperty);

    public static void SetAutomationNameKey(DependencyObject element, string value) => element.SetValue(AutomationNameKeyProperty, value);

    public static string NormalizeLanguage(string? language)
    {
        return language?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true
            ? RussianLanguage
            : DefaultLanguage;
    }

    public static void SetLanguage(string language, bool save = true)
    {
        var normalized = NormalizeLanguage(language);
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = normalized;
        }
        catch
        {
            // Unpackaged/dev runs may not support the override.
        }

        var culture = new CultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (string.Equals(_language, normalized, StringComparison.OrdinalIgnoreCase))
        {
            ApplyAll();
            return;
        }

        _language = normalized;
        if (save)
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
            return new ResourceLoader().GetString(key);
        }
        catch
        {
            return key;
        }
    }

    /// <summary>Selects the plural resource for a numeric count.</summary>
    public static string GetPlural(string keyBase, int count)
    {
        var suffix = PluralSuffix(_language, count);
        var value = Get($"{keyBase}_{suffix}");
        return string.Equals(value, $"{keyBase}_{suffix}", StringComparison.Ordinal)
            ? Get($"{keyBase}_Other")
            : value;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static string FormatPlural(string keyBase, int count, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, GetPlural(keyBase, count), args);
    }

    public static string Translate(string key, string language)
    {
        if (!string.Equals(NormalizeLanguage(language), _language, StringComparison.OrdinalIgnoreCase))
        {
            SetLanguage(language, save: false);
        }

        return Get(key);
    }

    public static string SongCount(int count) => FormatPlural("Plural_SongCount", count, count);

    public static string ShownSummary(int groups, int files) =>
        Format("Library_ShowingSummary", groups, files);

    public static string AllFoldersLabel => Get("Library_AllFolders");

    public static bool IsAllFolders(string? value) =>
        string.Equals(value, AllFoldersLabel, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Все папки", StringComparison.OrdinalIgnoreCase);

    private static string PluralSuffix(string language, int count)
    {
        if (language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            var mod10 = count % 10;
            var mod100 = count % 100;
            if (mod10 == 1 && mod100 != 11)
            {
                return "One";
            }

            if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            {
                return "Few";
            }

            return "Many";
        }

        return count == 1 ? "One" : "Other";
    }

    private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        Register(element);
        ApplyElement(element);
    }

    public static void RegisterXamlElement(DependencyObject element)
    {
        Register(element);
        ApplyElement(element);
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
