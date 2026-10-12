using System.Net;
using System.Net.Sockets;

namespace MeuSecretario.Domain.Security;

/// <summary>Classificação e normalização de IP (RN-009-01).</summary>
public static class IpClassifier
{
    /// <summary>Loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, <c>fc00::/7</c> e <c>fe80::/10</c>.</summary>
    public static bool IsTrusted(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return ip.AddressFamily == AddressFamily.InterNetworkV6
            && ((bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80));
    }

    /// <summary>IPv4 mapeado em IPv6 vira IPv4; o resto é <c>ToString()</c> (forma canônica).</summary>
    public static string Normalize(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    /// <summary>
    /// Entrada de operador (desvio 078): <c>trim</c> + <see cref="IPAddress.TryParse(string, out IPAddress)"/>, e IPv4
    /// só com quatro grupos (o <c>TryParse</c> aceitaria <c>1</c> como <c>0.0.0.1</c>).
    /// </summary>
    public static bool TryParseInput(string? text, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !IPAddress.TryParse(trimmed, out var ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork && trimmed.Count(c => c == '.') != 3)
        {
            return false;
        }

        normalized = Normalize(ip);
        return true;
    }
}
