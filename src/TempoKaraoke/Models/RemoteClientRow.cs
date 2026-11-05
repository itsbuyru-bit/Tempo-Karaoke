using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using TempoKaraoke.Services;
using Windows.UI;

namespace TempoKaraoke.Models;

/// <summary>Строка списка подключённых устройств в параметрах.</summary>
public sealed class RemoteClientRow : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _roleText = string.Empty;
    private string _connectionText = string.Empty;
    private string _ipAddress = string.Empty;
    private string _macAddress = string.Empty;
    private string _networkName = string.Empty;
    private bool _isOnline;
    private bool _isEditing;
    private int _orderedSongs;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string MacAddress
    {
        get => _macAddress;
        set
        {
            if (_macAddress == value)
            {
                return;
            }

            _macAddress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MacAddress)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMac)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLine)));
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public string NetworkName
    {
        get => _networkName;
        set
        {
            if (_networkName == value)
            {
                return;
            }

            _networkName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetworkName)));
        }
    }

    public string RoleText
    {
        get => _roleText;
        set
        {
            if (_roleText == value)
            {
                return;
            }

            _roleText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleText)));
        }
    }

    public string ConnectionText
    {
        get => _connectionText;
        set
        {
            if (_connectionText == value)
            {
                return;
            }

            _connectionText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionText)));
        }
    }

    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (_ipAddress == value)
            {
                return;
            }

            _ipAddress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IpAddress)));
        }
    }

    public bool HasMac => !string.IsNullOrWhiteSpace(MacAddress);

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value)
            {
                return;
            }

            _isOnline = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOnline)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLine)));
        }
    }

    public Brush StatusBrush => new SolidColorBrush(
        IsOnline
            ? Color.FromArgb(255, 34, 197, 94)
            : Color.FromArgb(255, 140, 140, 140));

    public string StatusLine =>
        LocalizationService.Format(
            IsOnline ? "Remote_StatusOnline" : "Remote_StatusOffline",
            HasMac ? MacAddress : LocalizationService.Get("Remote_UnknownMac"));

    public bool IsEditing
    {
        get => _isEditing;
        set => _isEditing = value;
    }

    public int OrderedSongs
    {
        get => _orderedSongs;
        set
        {
            if (_orderedSongs == value)
            {
                return;
            }

            _orderedSongs = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderedSongs)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderedSongsText)));
        }
    }

    public string OrderedSongsText =>
        LocalizationService.Format("Remote_OrderedSongs", OrderedSongs);

    public void RefreshLanguage()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLine)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OrderedSongsText)));
    }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name)
            ? string.IsNullOrWhiteSpace(MacAddress) ? LocalizationService.Get("Remote_Device") : MacAddress
            : Name;
}
