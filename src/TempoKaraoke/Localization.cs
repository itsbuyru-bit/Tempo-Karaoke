using Microsoft.UI.Xaml;

namespace TempoKaraoke;

/// <summary>XAML-facing attached localization keys.</summary>
public static class Localization
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(Localization),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SecondaryKeyProperty = DependencyProperty.RegisterAttached(
        "SecondaryKey",
        typeof(string),
        typeof(Localization),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ToolTipKeyProperty = DependencyProperty.RegisterAttached(
        "ToolTipKey",
        typeof(string),
        typeof(Localization),
        new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty AutomationNameKeyProperty = DependencyProperty.RegisterAttached(
        "AutomationNameKey",
        typeof(string),
        typeof(Localization),
        new PropertyMetadata(null, OnChanged));

    public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

    public static string GetSecondaryKey(DependencyObject element) => (string)element.GetValue(SecondaryKeyProperty);

    public static void SetSecondaryKey(DependencyObject element, string value) => element.SetValue(SecondaryKeyProperty, value);

    public static string GetToolTipKey(DependencyObject element) => (string)element.GetValue(ToolTipKeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value) => element.SetValue(ToolTipKeyProperty, value);

    public static string GetAutomationNameKey(DependencyObject element) => (string)element.GetValue(AutomationNameKeyProperty);

    public static void SetAutomationNameKey(DependencyObject element, string value) => element.SetValue(AutomationNameKeyProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        Services.LocalizationService.RegisterXamlElement(element);
    }
}
