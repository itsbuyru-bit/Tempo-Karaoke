using System;
using System.Collections.Generic;
using System.Globalization;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TempoKaraoke.Services;

namespace TempoKaraoke;

/// <summary>
/// Диалог эквалайзера LibVLC: включение, пресеты, 10 полос и связка соседних
/// ползунков. Состояние пишется в settings.json и восстанавливается при старте.
/// </summary>
public sealed partial class EqualizerDialog : ContentDialog
{
    private const int CustomPreset = -1;

    private static readonly (string Key, string Name, int Fallback)[] PresetSpecs =
    {
        ("Equalizer_PresetFlat", "Flat", 0),
        ("Equalizer_PresetClub", "Club", 2),
        ("Equalizer_PresetDance", "Dance", 3),
        ("Equalizer_PresetFullBass", "Full bass", 4),
        ("Equalizer_PresetLive", "Live", 9),
        ("Equalizer_PresetParty", "Party", 10),
        ("Equalizer_PresetPop", "Pop", 11),
        ("Equalizer_PresetRock", "Rock", 13),
        ("Equalizer_PresetSoft", "Soft", 15),
        ("Equalizer_PresetTechno", "Techno", 17),
    };

    private readonly PlayerService _player;
    private readonly Slider[] _sliders;
    private readonly float[] _previousValues;
    private readonly List<(string Key, int Index)> _presets = new();
    private Equalizer _equalizer = new();
    private bool _updating;
    private int _preset = CustomPreset;

    public EqualizerDialog(PlayerService player)
    {
        _player = player;
        InitializeComponent();

        var bandCount = (int)_equalizer.BandCount;
        _sliders = new Slider[bandCount];
        _previousValues = new float[bandCount];

        TitleText.Text = LocalizationService.Get("Ui_Equalizer.Text");
        CloseButtonText = LocalizationService.Get("Equalizer_Close");
        PresetLabel.Text = LocalizationService.Get("Equalizer_Preset");
        EnabledToggle.OnContent = LocalizationService.Get("Equalizer_On");
        EnabledToggle.OffContent = LocalizationService.Get("Equalizer_Off");
        LinkSlidersCheckBox.Content = LocalizationService.Get("Equalizer_LinkSliders");

        var decibel = LocalizationService.Get("Equalizer_dB");
        ScalePlus12.Text = $"+12 {decibel}";
        ScalePlus6.Text = $"+6 {decibel}";
        ScaleZero.Text = $"0 {decibel}";
        ScaleMinus6.Text = $"-6 {decibel}";
        ScaleMinus12.Text = $"-12 {decibel}";

        BuildSliders();
        BuildPresets();
        RestoreState();
    }

    private void BuildSliders()
    {
        for (var band = 0; band < _sliders.Length; band++)
        {
            SlidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                StepFrequency = 0.5,
                Height = 160,
                Tag = band,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            slider.ValueChanged += Slider_ValueChanged;
            _sliders[band] = slider;

            var frequency = _equalizer.BandFrequency((uint)band);
            var label = new TextBlock
            {
                Text = FormatFrequency(frequency),
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            };

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(slider);
            panel.Children.Add(label);

            Grid.SetColumn(panel, band);
            SlidersGrid.Children.Add(panel);
        }
    }

    private static string FormatFrequency(float hertz)
    {
        if (hertz >= 1000)
        {
            var value = hertz / 1000.0;
            var text = value.ToString(value % 1 == 0 ? "0" : "0.#", CultureInfo.CurrentCulture);
            return $"{text} {LocalizationService.Get("Equalizer_kHz")}";
        }

        return $"{hertz:0} {LocalizationService.Get("Equalizer_Hz")}";
    }

    private void BuildPresets()
    {
        foreach (var (key, name, fallback) in PresetSpecs)
        {
            _presets.Add((key, ResolvePresetIndex(name, fallback)));
        }

        var items = new List<ComboBoxItem>();
        foreach (var (key, index) in _presets)
        {
            items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get(key),
                Tag = index,
            });
        }

        items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get("Equalizer_PresetCustom"),
            Tag = CustomPreset,
        });

        _updating = true;
        PresetComboBox.ItemsSource = items;
        _updating = false;
    }

    private static int ResolvePresetIndex(string name, int fallback)
    {
        try
        {
            using var probe = new Equalizer();
            for (uint index = 0; index < probe.PresetCount; index++)
            {
                if (string.Equals(probe.PresetName(index), name, StringComparison.OrdinalIgnoreCase))
                {
                    return (int)index;
                }
            }
        }
        catch
        {
            // Без libvlc остаётся фиксированный индекс пресета.
        }

        return fallback;
    }

    private void RestoreState()
    {
        var savedPreset = SettingsService.EqualizerPreset;
        var savedBands = SettingsService.EqualizerBands;

        if (savedPreset >= 0)
        {
            _preset = savedPreset;
            _equalizer = new Equalizer((uint)savedPreset);
        }
        else
        {
            _preset = CustomPreset;
            _equalizer = new Equalizer();
            for (var band = 0; band < _sliders.Length && band < savedBands.Count; band++)
            {
                _equalizer.SetAmp(savedBands[band], (uint)band);
            }
        }

        _updating = true;
        EnabledToggle.IsOn = SettingsService.EqualizerEnabled;
        LinkSlidersCheckBox.IsChecked = SettingsService.EqualizerLinkSliders;
        SetPresetSelection(_preset);
        LoadBandsIntoSliders();
        _updating = false;

        UpdateEnabledState();
        ApplyEqualizer();
    }

    private void SetPresetSelection(int preset)
    {
        if (PresetComboBox.ItemsSource is not IEnumerable<ComboBoxItem> items)
        {
            return;
        }

        var index = 0;
        foreach (var item in items)
        {
            if (item.Tag is int tag && tag == preset)
            {
                PresetComboBox.SelectedIndex = index;
                return;
            }

            index++;
        }
    }

    private void LoadBandsIntoSliders()
    {
        for (var band = 0; band < _sliders.Length; band++)
        {
            var value = Math.Clamp(_equalizer.Amp((uint)band), -12f, 12f);
            _sliders[band].Value = value;
            _previousValues[band] = (float)_sliders[band].Value;
        }
    }

    private void UpdateEnabledState()
    {
        var on = EnabledToggle.IsOn;
        PresetComboBox.IsEnabled = on;
        LinkSlidersCheckBox.IsEnabled = on;
        foreach (var slider in _sliders)
        {
            slider.IsEnabled = on;
        }
    }

    private void ApplyEqualizer()
    {
        _player.SetEqualizer(EnabledToggle.IsOn ? _equalizer : null);
    }

    private void EnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        UpdateEnabledState();
        ApplyEqualizer();
        SaveState();
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || PresetComboBox.SelectedItem is not ComboBoxItem { Tag: int preset })
        {
            return;
        }

        _preset = preset;
        if (preset != CustomPreset)
        {
            _equalizer = new Equalizer((uint)preset);
            _updating = true;
            LoadBandsIntoSliders();
            _updating = false;
        }

        ApplyEqualizer();
        SaveState();
    }

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating || sender is not Slider { Tag: int band } slider)
        {
            return;
        }

        var delta = (float)(slider.Value - _previousValues[band]);
        _previousValues[band] = (float)slider.Value;

        if (LinkSlidersCheckBox.IsChecked == true && Math.Abs(delta) > 0.001f)
        {
            _updating = true;
            MoveNeighbor(band - 1, delta * 0.5f);
            MoveNeighbor(band + 1, delta * 0.5f);
            _updating = false;
        }

        for (var index = 0; index < _sliders.Length; index++)
        {
            _equalizer.SetAmp((float)_sliders[index].Value, (uint)index);
        }

        if (_preset != CustomPreset)
        {
            _preset = CustomPreset;
            _updating = true;
            SetPresetSelection(CustomPreset);
            _updating = false;
        }

        ApplyEqualizer();
        SaveState();
    }

    private void MoveNeighbor(int band, float delta)
    {
        if (band < 0 || band >= _sliders.Length)
        {
            return;
        }

        var next = Math.Clamp((float)_sliders[band].Value + delta, -12f, 12f);
        _sliders[band].Value = next;
        _previousValues[band] = (float)_sliders[band].Value;
    }

    private void SaveState()
    {
        SettingsService.EqualizerEnabled = EnabledToggle.IsOn;
        SettingsService.EqualizerPreset = _preset;
        SettingsService.EqualizerLinkSliders = LinkSlidersCheckBox.IsChecked == true;

        var bands = new List<float>(_sliders.Length);
        foreach (var slider in _sliders)
        {
            bands.Add((float)Math.Round(slider.Value, 1));
        }

        SettingsService.EqualizerBands = bands;
    }
}
