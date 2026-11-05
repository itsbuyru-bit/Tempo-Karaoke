using System;
using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace TempoKaraoke.Controls;

/// <summary>TextBlock-like control that highlights the current search query and its basic word forms.</summary>
public sealed partial class HighlightedText : UserControl
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText),
        typeof(string),
        typeof(HighlightedText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query),
        typeof(string),
        typeof(HighlightedText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public HighlightedText()
    {
        InitializeComponent();
    }

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HighlightedText)d).Rebuild();

    private void Rebuild()
    {
        Inner.Inlines.Clear();
        var text = SourceText ?? string.Empty;
        var query = Query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        if (query.Length == 0)
        {
            Inner.Inlines.Add(new Run { Text = text });
            return;
        }

        var ranges = new List<(int Start, int End)>();
        AddRanges(text, query, ranges, wordStartOnly: false);
        var root = SearchRoot(query);
        if (root.Length >= 3 && !string.Equals(root, query, StringComparison.OrdinalIgnoreCase))
        {
            AddRanges(text, root, ranges, wordStartOnly: true);
        }

        if (ranges.Count == 0)
        {
            Inner.Inlines.Add(new Run { Text = text });
            return;
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End)
            {
                merged.Add(range);
                continue;
            }

            var last = merged[^1];
            merged[^1] = (last.Start, Math.Max(last.End, range.End));
        }

        var accent = Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var value)
            ? value as Brush
            : null;

        var offset = 0;
        foreach (var range in merged)
        {
            if (range.Start > offset)
            {
                Inner.Inlines.Add(new Run { Text = text[offset..range.Start] });
            }

            Inner.Inlines.Add(new Run
            {
                Text = text[range.Start..range.End],
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
            });
            offset = range.End;
        }

        if (offset < text.Length)
        {
            Inner.Inlines.Add(new Run { Text = text[offset..] });
        }
    }

    private static void AddRanges(
        string text,
        string needle,
        List<(int Start, int End)> ranges,
        bool wordStartOnly)
    {
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return;
            }

            if (!wordStartOnly || index == 0 || !char.IsLetter(text[index - 1]))
            {
                ranges.Add((index, index + needle.Length));
            }

            start = index + 1;
        }
    }

    private static string SearchRoot(string query)
    {
        if (query.Length <= 3)
        {
            return query;
        }

        // Very small, language-agnostic prefix fallback: enough to match
        // "Конь" with "коня" without pretending to be a full stemmer.
        foreach (var suffix in new[]
        {
            "ами", "ями", "ого", "его", "ому", "ему", "ыми", "ими",
            "ов", "ев", "ей", "ам", "ям", "ах", "ях", "ом", "ем", "ой",
            "ый", "ая", "ое", "ие", "ые", "а", "я", "ы", "и", "у", "ю",
            "е", "о", "ь",
        })
        {
            if (query.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                query.Length - suffix.Length >= 3)
            {
                return query[..^suffix.Length];
            }
        }

        return query;
    }
}
