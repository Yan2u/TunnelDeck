using System.Globalization;
using System.Net;

namespace TunnelDeck.Core.Validation;

public static class HostAddress
{
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host != host.Trim() || host.Any(char.IsWhiteSpace) || host.Any(char.IsControl)) { return false; }
        if (IPAddress.TryParse(host, out _)) { return true; }
        try
        {
            string ascii = new IdnMapping().GetAscii(host);
            string domain = ascii.EndsWith('.') ? ascii[..^1] : ascii;
            return domain.Length is > 0 and <= 253 && domain.Split('.').All(label => label.Length is > 0 and <= 63) &&
                Uri.CheckHostName(domain) == UriHostNameType.Dns;
        }
        catch (ArgumentException) { return false; }
    }

    public static bool IsValidBindAddress(string? host) =>
        !string.IsNullOrWhiteSpace(host) && host == host.Trim() && IPAddress.TryParse(host, out _);

    public static string Normalize(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        string value = host.Trim().TrimEnd('.');
        return IPAddress.TryParse(value, out IPAddress? address)
            ? address.ToString()
            : new IdnMapping().GetAscii(value).TrimEnd('.').ToLowerInvariant();
    }
}
