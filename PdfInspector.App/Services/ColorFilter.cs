using System;
using UglyToad.PdfPig.Graphics.Colors;

namespace PdfInspector.App.Services;

public sealed class ColorFilter
{
    private readonly (double r, double g, double b) target;
    private readonly double tolerance;

    private ColorFilter((double r, double g, double b) target, double tolerance)
    {
        this.target = target;
        this.tolerance = tolerance;
    }

    public static bool TryParse(string value, out ColorFilter? filter, out string? error, double tolerance = 0.01)
    {
        filter = null;
        error = null;

        if (!ColorUtilities.TryParseHex(value, out var rgb, out error))
        {
            return false;
        }

        filter = new ColorFilter(rgb, tolerance);
        return true;
    }

    public bool Matches(IColor? color)
    {
        var rgb = ColorUtilities.ToRgb(color);
        if (rgb is null)
        {
            return false;
        }

        return WithinTolerance(rgb.Value.r, target.r)
            && WithinTolerance(rgb.Value.g, target.g)
            && WithinTolerance(rgb.Value.b, target.b);
    }

    public override string ToString() => $"#{ToByte(target.r):X2}{ToByte(target.g):X2}{ToByte(target.b):X2} (±{tolerance:0.###})";

    private bool WithinTolerance(double value, double expected) =>
        Math.Abs(value - expected) <= tolerance;

    private static byte ToByte(double channel) =>
        (byte)Math.Round(Math.Max(0, Math.Min(1, channel)) * 255);

    public bool MatchesHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        if (!ColorUtilities.TryParseHex(hex, out var rgb, out _))
        {
            return false;
        }

        return WithinTolerance(rgb.r, target.r)
            && WithinTolerance(rgb.g, target.g)
            && WithinTolerance(rgb.b, target.b);
    }
}
