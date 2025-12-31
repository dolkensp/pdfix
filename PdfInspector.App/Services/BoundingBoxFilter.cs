using System.Globalization;
using System.Linq;
using UglyToad.PdfPig.Core;

namespace PdfInspector.App.Services;

public sealed class BoundingBoxFilter
{
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    private BoundingBoxFilter(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public static bool TryParse(string value, out BoundingBoxFilter? filter, out string? error)
    {
        filter = null;
        error = null;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            error = "Expected four comma-separated numbers for --vector-bbox (minX,minY,maxX,maxY).";
            return false;
        }

        if (!parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            error = "Bounding box values must be numbers.";
            return false;
        }

        var numbers = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        if (numbers[2] <= numbers[0] || numbers[3] <= numbers[1])
        {
            error = "Bounding box max values must be greater than min values.";
            return false;
        }

        filter = new BoundingBoxFilter(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public bool Intersects(PdfRectangle rectangle)
    {
        return !(rectangle.Left > MaxX || rectangle.Right < MinX || rectangle.Bottom > MaxY || rectangle.Top < MinY);
    }

    public override string ToString() =>
        $"[{MinX:0.###},{MinY:0.###}]–[{MaxX:0.###},{MaxY:0.###}]";
}
