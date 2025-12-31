using System.Collections.Generic;
using PdfInspector.App.Models;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace PdfInspector.App.Services;

internal static class GeometryConverters
{
    public static BoundingBox ToBoundingBox(PdfRectangle rectangle) =>
        new(rectangle.Left, rectangle.Bottom, rectangle.Width, rectangle.Height);

    public static PdfPoint ToPoint((double x, double y) tuple) => new(tuple.x, tuple.y);

    public static IEnumerable<PdfSubpath.IPathCommand> AdjustLineEndpoints(
        IReadOnlyList<PdfSubpath.IPathCommand> commands,
        PdfPoint newStart,
        PdfPoint newEnd)
    {
        if (commands.Count < 2)
        {
            return commands;
        }

        var result = new List<PdfSubpath.IPathCommand>(commands.Count);
        var first = commands[0];
        var second = commands[1];

        if (first is PdfSubpath.Move && second is PdfSubpath.Line)
        {
            result.Add(new PdfSubpath.Move(newStart));
            result.Add(new PdfSubpath.Line(newStart, newEnd));
            for (var i = 2; i < commands.Count; i++)
            {
                result.Add(commands[i]);
            }

            return result;
        }

        return commands;
    }
}
