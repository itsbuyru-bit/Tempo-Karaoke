using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace TempoKaraoke.Services;

/// <summary>Нормализация MAC-адресов: AA:BB:CC:DD:EE:FF.</summary>
public static class RemoteClientMac
{
    public static bool TryNormalize(string? value, out string macAddress)
    {
        macAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (hex.Length != 12)
        {
            return false;
        }

        macAddress = string.Join(
            ":",
            Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
        return true;
    }
}

/// <summary>Получает MAC-адрес соседнего устройства из ARP-таблицы Windows.</summary>
public static class MacAddressHelper
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destinationIp, uint sourceIp, byte[] macAddress, ref uint length);

    public static string? TryGet(IPAddress? address)
    {
        if (address is null ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(address))
        {
            return null;
        }

        try
        {
            var destination = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            var mac = new byte[6];
            var length = (uint)mac.Length;
            if (SendARP(destination, 0, mac, ref length) != 0 || length == 0)
            {
                return null;
            }

            if (mac.All(value => value == 0))
            {
                return null;
            }

            return string.Join(":", mac.Select(value => value.ToString("X2")));
        }
        catch
        {
            return null;
        }
    }
}
