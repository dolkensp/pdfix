using System;
using System.Globalization;
using UglyToad.PdfPig.Graphics.Colors;

namespace PdfInspector.App.Services;

public static class ColorUtilities
{
    public static string? ToHex(IColor? color)
    {
        if (color == null)
        {
            return null;
        }

        try
        {
            var rgb = color.ToRGBValues();
            var r = ClampToByte(rgb.r);
            var g = ClampToByte(rgb.g);
            var b = ClampToByte(rgb.b);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch
        {
            return null;
        }
    }

    public static (double r, double g, double b)? ToRgb(IColor? color)
    {
        if (color == null)
        {
            return null;
        }

        try
        {
            var values = color.ToRGBValues();
            return (values.r, values.g, values.b);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParseHex(string value, out (double r, double g, double b) rgb, out string? error)
    {
        rgb = default;
        error = null;

        var hex = value.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        if (hex.Length is not (6 or 3))
        {
            error = "Hex colors must be in the form #RRGGBB or #RGB.";
            return false;
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
        {
            error = "Could not parse the provided hex color.";
            return false;
        }

        var rByte = (byte)((number >> 16) & 0xFF);
        var gByte = (byte)((number >> 8) & 0xFF);
        var bByte = (byte)(number & 0xFF);

        rgb = (rByte / 255d, gByte / 255d, bByte / 255d);
        return true;
    }

    private static byte ClampToByte(double channel)
    {
        var clamped = Math.Max(0, Math.Min(1, channel));
        return (byte)Math.Round(clamped * 255);
    }
}
