using System.Globalization;

namespace TunnelDeck.Core.Validation;

public static class PortNumber
{
    public const int Minimum = 1;
    public const int Maximum = 65535;

    public static bool IsValid(int value) => value is >= Minimum and <= Maximum;

    public static bool TryParse(string? text, out int port)
    {
        port = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int candidate) || !IsValid(candidate))
        {
            return false;
        }
        port = candidate;
        return true;
    }

    public static bool TryFromNumber(double value, out int port)
    {
        port = 0;
        if (!double.IsFinite(value) || value < Minimum || value > Maximum || Math.Truncate(value) != value) { return false; }
        port = (int)value;
        return true;
    }
}
