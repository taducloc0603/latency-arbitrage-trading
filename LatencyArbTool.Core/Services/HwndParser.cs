using System.Globalization;

namespace LatencyArbTool.Core.Services;

public static class HwndParser
{
    public static bool TryParse(string? text, out ulong hwnd, out string error)
    {
        hwnd = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "HWND is empty";
            return false;
        }

        var value = text.Trim();
        var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var number = isHex ? value[2..] : value;
        var style = isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer;

        if (!ulong.TryParse(number, style, CultureInfo.InvariantCulture, out hwnd))
        {
            error = "HWND is not a valid decimal or hex value";
            return false;
        }

        if (hwnd == 0)
        {
            error = "HWND must be non-zero";
            return false;
        }

        return true;
    }
}

