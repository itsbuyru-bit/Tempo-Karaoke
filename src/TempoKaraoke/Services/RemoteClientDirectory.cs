using System;
using System.Collections.Generic;
using System.Linq;

namespace TempoKaraoke.Services;

/// <summary>Сохранённое пользователем имя для устройства по MAC-адресу.</summary>
public sealed record RemoteClientAlias(string MacAddress, string Name);

/// <summary>История устройств, когда-либо подключавшихся к пульту.</summary>
public sealed record RemoteClientHistory(
    string MacAddress,
    string Name,
    string IpAddress,
    DateTime LastSeenUtc,
    string? NetworkName = null);

/// <summary>Снимок состояния одного подключённого или известного устройства.</summary>
public sealed record RemoteClientInfo(
    string ClientId,
    string DisplayName,
    string NetworkName,
    string? MacAddress,
    string IpAddress,
    bool IsHost,
    bool IsConnected,
    DateTime ConnectedUtc,
    DateTime LastSeenUtc)
{
    public bool IsActive => IsConnected || LastSeenUtc > DateTime.UtcNow.AddSeconds(-30);

    public string RoleText => IsHost ? "Ведущий" : "Гость";

    public string ConnectionText => IsConnected
        ? "Подключён"
        : IsActive ? "Активен" : "Не в сети";
}

/// <summary>
/// Знает, кто подключался к пульту: сессию, IP, MAC и пользовательское имя.
/// Имена по MAC сохраняются в remote-clients.json и переживают перезапуск.
/// </summary>
public sealed class RemoteClientDirectory
{
    private const string FileName = "remote-clients.json";
    private const string HistoryFileName = "remote-history.json";

    private readonly object _sync = new();
    private readonly Dictionary<string, ClientRecord> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HistoryRecord> _history = new(StringComparer.OrdinalIgnoreCase);

    public RemoteClientDirectory()
    {
        foreach (var item in LoadHistory())
        {
            if (RemoteClientMac.TryNormalize(item.MacAddress, out var mac))
            {
                _history[mac] = new HistoryRecord
                {
                    MacAddress = mac,
                    Name = item.Name,
                    NetworkName = item.NetworkName ?? item.Name,
                    IpAddress = item.IpAddress,
                    LastSeenUtc = item.LastSeenUtc,
                };
            }
        }

        foreach (var alias in LoadAliases())
        {
            if (RemoteClientMac.TryNormalize(alias.MacAddress, out var mac) &&
                !string.IsNullOrWhiteSpace(alias.Name))
            {
                _aliases[mac] = alias.Name.Trim();
                if (!_history.TryGetValue(mac, out var history))
                {
                    _history[mac] = new HistoryRecord
                    {
                        MacAddress = mac,
                        Name = alias.Name.Trim(),
                        NetworkName = alias.Name.Trim(),
                    };
                }
                else
                {
                    history.Name = alias.Name.Trim();
                }
            }
        }
    }

    public RemoteClientInfo Register(
        string clientId,
        string ipAddress,
        string? macAddress,
        string fallbackName,
        bool isHost)
    {
        lock (_sync)
        {
            var mac = RemoteClientMac.TryNormalize(macAddress, out var normalized) ? normalized : null;
            if (!_clients.TryGetValue(clientId, out var record))
            {
                record = new ClientRecord
                {
                    ClientId = clientId,
                    ConnectedUtc = DateTime.UtcNow,
                };
                _clients[clientId] = record;
            }

            record.IpAddress = ipAddress;
            record.LastSeenUtc = DateTime.UtcNow;
            record.IsHost |= isHost;
            record.FallbackName = fallbackName;
            if (mac is not null)
            {
                record.MacAddress = mac;
            }

            var alias = record.MacAddress is not null && _aliases.TryGetValue(record.MacAddress, out var known)
                ? known
                : null;
            record.DisplayName = alias ?? record.FallbackName;

            if (record.MacAddress is not null)
            {
                EnsureHistory(
                    record.MacAddress,
                    record.DisplayName,
                    ipAddress,
                    DateTime.UtcNow,
                    record.FallbackName);
            }

            return ToInfo(record);
        }
    }

    public void Touch(string clientId)
    {
        lock (_sync)
        {
            if (_clients.TryGetValue(clientId, out var record))
            {
                record.LastSeenUtc = DateTime.UtcNow;
            }
        }
    }

    public bool Contains(string clientId)
    {
        lock (_sync)
        {
            return _clients.ContainsKey(clientId);
        }
    }

    public void SetConnected(string clientId, bool connected)
    {
        lock (_sync)
        {
            if (_clients.TryGetValue(clientId, out var record))
            {
                record.IsConnected = connected;
                record.LastSeenUtc = DateTime.UtcNow;
                if (connected)
                {
                    record.ConnectedUtc = DateTime.UtcNow;
                }
            }
        }
    }

    public void DisconnectAll()
    {
        lock (_sync)
        {
            foreach (var record in _clients.Values)
            {
                record.IsConnected = false;
            }
        }
    }

    public void SetHost(string clientId, bool isHost)
    {
        lock (_sync)
        {
            if (_clients.TryGetValue(clientId, out var record))
            {
                record.IsHost = isHost;
            }
        }
    }

    public string? GetAliasName(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        lock (_sync)
        {
            if (!_clients.TryGetValue(clientId, out var record))
            {
                return null;
            }

            if (record.IsHost)
            {
                return "Ведущий";
            }

            return record.MacAddress is not null &&
                   _aliases.TryGetValue(record.MacAddress, out var alias) &&
                   !string.IsNullOrWhiteSpace(alias)
                ? alias
                : null;
        }
    }

    public bool SetClientName(string clientId, string name)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var displayName = name.Trim();
        lock (_sync)
        {
            if (!_clients.TryGetValue(clientId, out var record))
            {
                return false;
            }

            record.DisplayName = displayName;
            if (record.MacAddress is not null)
            {
                _aliases[record.MacAddress] = displayName;
                SaveAliases();
                EnsureHistory(
                    record.MacAddress,
                    displayName,
                    record.IpAddress,
                    DateTime.UtcNow,
                    record.FallbackName);
            }

            return true;
        }
    }

    public string? GetFallbackName(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        lock (_sync)
        {
            return _clients.TryGetValue(clientId, out var record)
                ? record.FallbackName
                : null;
        }
    }

    public string ResolveName(string? clientId, string fallback = "Компьютер")
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return fallback;
        }

        lock (_sync)
        {
            if (!_clients.TryGetValue(clientId, out var record))
            {
                return fallback;
            }

            if (record.IsHost)
            {
                return "Ведущий";
            }

            return string.IsNullOrWhiteSpace(record.DisplayName) ? fallback : record.DisplayName;
        }
    }

    public string? BadgeText()
    {
        lock (_sync)
        {
            var active = _clients.Values
                .Where(record => record.IsConnected || record.LastSeenUtc > DateTime.UtcNow.AddSeconds(-30))
                .ToList();
            if (active.Count == 0)
            {
                return null;
            }

            if (active.Any(record => record.IsHost))
            {
                return "Ведущий";
            }

            return active.Count == 1
                ? active[0].DisplayName
                : $"Гости: {active.Count}";
        }
    }

    public IReadOnlyList<RemoteClientInfo> Snapshot()
    {
        lock (_sync)
        {
            var result = _clients.Values
                .Select(ToInfo)
                .ToList();

            foreach (var history in _history.Values)
            {
                if (result.Any(client =>
                        string.Equals(client.MacAddress, history.MacAddress, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var name = _aliases.TryGetValue(history.MacAddress, out var alias)
                    ? alias
                    : history.Name;
                result.Add(new RemoteClientInfo(
                    string.Empty,
                    string.IsNullOrWhiteSpace(name) ? "Устройство" : name,
                    string.IsNullOrWhiteSpace(history.NetworkName) ? history.Name : history.NetworkName,
                    history.MacAddress,
                    history.IpAddress,
                    IsHost: false,
                    IsConnected: false,
                    ConnectedUtc: default,
                    LastSeenUtc: history.LastSeenUtc));
            }

            return result
                .OrderByDescending(client => client.IsHost)
                .ThenByDescending(client => client.IsActive)
                .ThenByDescending(client => client.LastSeenUtc)
                .ThenBy(client => client.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public bool SetAlias(string macAddress, string? name)
    {
        if (!RemoteClientMac.TryNormalize(macAddress, out var mac))
        {
            return false;
        }

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _aliases.Remove(mac);
            }
            else
            {
                _aliases[mac] = name.Trim();
            }

            foreach (var record in _clients.Values.Where(record =>
                         string.Equals(record.MacAddress, mac, StringComparison.OrdinalIgnoreCase)))
            {
                record.DisplayName = _aliases.TryGetValue(mac, out var alias)
                    ? alias
                    : record.FallbackName;
            }

            var fallback = _clients.Values
                .FirstOrDefault(record => string.Equals(record.MacAddress, mac, StringComparison.OrdinalIgnoreCase))
                ?.FallbackName;
            EnsureHistory(
                mac,
                string.IsNullOrWhiteSpace(name) ? fallback ?? string.Empty : name.Trim(),
                string.Empty,
                DateTime.MinValue,
                fallback ?? string.Empty);

            SaveAliases();
            return true;
        }
    }

    public bool RemoveAlias(string macAddress)
    {
        if (!RemoteClientMac.TryNormalize(macAddress, out var mac))
        {
            return false;
        }

        lock (_sync)
        {
            var removed = _aliases.Remove(mac);
            removed |= _history.Remove(mac);
            if (removed)
            {
                foreach (var record in _clients.Values.Where(record =>
                             string.Equals(record.MacAddress, mac, StringComparison.OrdinalIgnoreCase)))
                {
                    record.DisplayName = record.FallbackName;
                }

                SaveAliases();
                SaveHistory();
            }

            return removed;
        }
    }

    public static string? NormalizeMac(string? macAddress) =>
        RemoteClientMac.TryNormalize(macAddress, out var mac) ? mac : null;

    private List<RemoteClientAlias> LoadAliases() =>
        JsonFile.Read(AppStorage.PathFor(FileName), AppJsonContext.Default.ListRemoteClientAlias)
        ?? new List<RemoteClientAlias>();

    private List<RemoteClientHistory> LoadHistory() =>
        JsonFile.Read(AppStorage.PathFor(HistoryFileName), AppJsonContext.Default.ListRemoteClientHistory)
        ?? new List<RemoteClientHistory>();

    private void EnsureHistory(
        string mac,
        string name,
        string ipAddress,
        DateTime lastSeenUtc,
        string networkName)
    {
        var changed = false;
        if (!_history.TryGetValue(mac, out var item))
        {
            item = new HistoryRecord { MacAddress = mac };
            _history[mac] = item;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(name) &&
            !string.Equals(item.Name, name, StringComparison.CurrentCulture))
        {
            item.Name = name.Trim();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(networkName) &&
            !string.Equals(item.NetworkName, networkName, StringComparison.CurrentCulture))
        {
            item.NetworkName = networkName.Trim();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(ipAddress) &&
            !string.Equals(item.IpAddress, ipAddress, StringComparison.Ordinal))
        {
            item.IpAddress = ipAddress;
            changed = true;
        }

        if (lastSeenUtc != DateTime.MinValue && lastSeenUtc > item.LastSeenUtc)
        {
            item.LastSeenUtc = lastSeenUtc;
        }

        while (_history.Count > 200)
        {
            var oldest = _history.Values
                .OrderBy(record => record.LastSeenUtc)
                .ThenBy(record => record.MacAddress, StringComparer.Ordinal)
                .First();
            _history.Remove(oldest.MacAddress);
        }

        if (changed)
        {
            SaveHistory();
        }
    }

    private void SaveAliases()
    {
        try
        {
            var aliases = _aliases
                .Select(pair => new RemoteClientAlias(pair.Key, pair.Value))
                .OrderBy(alias => alias.MacAddress, StringComparer.Ordinal)
                .ToList();
            JsonFile.Write(AppStorage.PathFor(FileName), aliases, AppJsonContext.Default.ListRemoteClientAlias);
        }
        catch
        {
            // Имена устройств — вспомогательные данные.
        }
    }

    private void SaveHistory()
    {
        try
        {
            var history = _history.Values
                .Select(record => new RemoteClientHistory(
                    record.MacAddress,
                    _aliases.TryGetValue(record.MacAddress, out var alias) ? alias : record.Name,
                    record.IpAddress,
                    record.LastSeenUtc,
                    record.NetworkName))
                .OrderByDescending(record => record.LastSeenUtc)
                .ThenBy(record => record.MacAddress, StringComparer.Ordinal)
                .ToList();
            JsonFile.Write(AppStorage.PathFor(HistoryFileName), history, AppJsonContext.Default.ListRemoteClientHistory);
        }
        catch
        {
            // История устройств — вспомогательные данные.
        }
    }

    private static RemoteClientInfo ToInfo(ClientRecord record) =>
        new(
            record.ClientId,
            record.DisplayName,
            record.FallbackName,
            record.MacAddress,
            record.IpAddress,
            record.IsHost,
            record.IsConnected,
            record.ConnectedUtc,
            record.LastSeenUtc);

    private sealed class HistoryRecord
    {
        public string MacAddress { get; init; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string NetworkName { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public DateTime LastSeenUtc { get; set; }
    }

    private sealed class ClientRecord
    {
        public string ClientId { get; init; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string FallbackName { get; set; } = string.Empty;

        public string? MacAddress { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public bool IsHost { get; set; }

        public bool IsConnected { get; set; }

        public DateTime ConnectedUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }
    }
}
