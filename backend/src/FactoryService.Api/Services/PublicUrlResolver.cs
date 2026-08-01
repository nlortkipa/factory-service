using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FactoryService.Api.Services;

/// <summary>Resolves the development-only {LAN_IP} token in the public frontend URL.</summary>
public static class PublicUrlResolver
{
    public static string Resolve(string configuredUrl)
    {
        const string token = "{LAN_IP}";
        if (!configuredUrl.Contains(token, StringComparison.Ordinal)) return configuredUrl.TrimEnd('/');

        IPAddress? address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up
                && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(IsPrivateLanAddress);

        if (address is null)
            throw new InvalidOperationException("No active private IPv4 address was found for the QR public URL.");

        return configuredUrl.Replace(token, address.ToString(), StringComparison.Ordinal).TrimEnd('/');
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}
