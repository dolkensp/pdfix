using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PdfInspector.App.Models;
using UglyToadPdf = UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace PdfInspector.App.Services;

public sealed class PdfEditor
{
    public void RewriteWithEdits(
        string inputPath,
        string outputPath,
        int? targetPage,
        int? targetPathId,
        string? newStrokeColorHex,
        (double x, double y)? newStart,
        (double x, double y)? newEnd)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required for writing.", nameof(outputPath));
        }

        using var source = UglyToadPdf.PdfDocument.Open(inputPath);
        using var target = new PdfDocument();

        for (var pageNumber = 1; pageNumber <= source.NumberOfPages; pageNumber++)
        {
            var page = source.GetPage(pageNumber);
            var pdfPage = target.AddPage();
            pdfPage.Width = page.Width;
            pdfPage.Height = page.Height;

            using var gfx = XGraphics.FromPdfPage(pdfPage);

            var paths = page.ExperimentalAccess.Paths?.ToList() ?? new List<PdfPath>();
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var subpaths = new List<IReadOnlyList<PdfSubpath.IPathCommand>>();
                foreach (var subpath in path)
                {
                    subpaths.Add(subpath.Commands);
                }
                var strokeHex = ColorUtilities.ToHex(path.StrokeColor);
                var fillHex = ColorUtilities.ToHex(path.FillColor);

                if (targetPage == pageNumber && targetPathId == i)
                {
                    if (newStrokeColorHex != null)
                    {
                        strokeHex = NormalizeHex(newStrokeColorHex);
                    }

                    if (newStart.HasValue && newEnd.HasValue)
                    {
                        subpaths = AdjustCommands(subpaths, newStart.Value, newEnd.Value);
                    }
                }

                DrawPath(gfx, subpaths, path, strokeHex, fillHex);
            }
        }

        target.Save(outputPath);
    }

    private static List<IReadOnlyList<PdfSubpath.IPathCommand>> AdjustCommands(
        List<IReadOnlyList<PdfSubpath.IPathCommand>> subpaths,
        (double x, double y) newStart,
        (double x, double y) newEnd)
    {
        var updatedSubpaths = new List<IReadOnlyList<PdfSubpath.IPathCommand>>(subpaths.Count);
        var applied = false;
        foreach (var subpathCommands in subpaths)
        {
            if (!applied)
            {
                var adjusted = GeometryConverters.AdjustLineEndpoints(subpathCommands, GeometryConverters.ToPoint(newStart), GeometryConverters.ToPoint(newEnd));
                updatedSubpaths.Add(adjusted.ToList());
                applied = true;
                continue;
            }

            updatedSubpaths.Add(subpathCommands);
        }
        return updatedSubpaths;
    }

    private static void DrawPath(
        XGraphics gfx,
        IReadOnlyList<IReadOnlyList<PdfSubpath.IPathCommand>> subpaths,
        PdfPath sourcePath,
        string? strokeHex,
        string? fillHex)
    {
        var xPath = new XGraphicsPath();
        foreach (var subpath in subpaths)
        {
            var started = false;
            XPoint start = default;
            foreach (var command in subpath)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        start = ToXPoint(move.Location);
                        started = true;
                        break;
                    case PdfSubpath.Line line:
                        if (!started)
                        {
                            start = ToXPoint(line.From);
                            started = true;
                        }
                        xPath.AddLine(ToXPoint(line.From), ToXPoint(line.To));
                        break;
                    case PdfSubpath.BezierCurve curve:
                        xPath.AddBezier(
                            ToXPoint(curve.StartPoint),
                            ToXPoint(curve.FirstControlPoint),
                            ToXPoint(curve.SecondControlPoint),
                            ToXPoint(curve.EndPoint));
                        break;
                    case PdfSubpath.Close:
                        xPath.CloseFigure();
                        break;
                }
            }
        }

        var strokeColor = ParseColor(strokeHex);
        var fillColor = ParseColor(fillHex);
        var lineWidth = (double)(sourcePath.IsStroked ? sourcePath.LineWidth : 0.5m);

        if (fillColor is XColor fill && sourcePath.IsFilled)
        {
            gfx.DrawPath(new XPen(fill, 0), new XSolidBrush(fill), xPath);
        }

        if (strokeColor is XColor stroke && sourcePath.IsStroked)
        {
            var pen = new XPen(stroke, lineWidth);
            gfx.DrawPath(pen, xPath);
        }
    }

    private static XColor? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        if (hex.Length is not (6 or 3))
        {
            return null;
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var r = (byte)((value >> 16) & 0xFF);
        var g = (byte)((value >> 8) & 0xFF);
        var b = (byte)(value & 0xFF);
        return XColor.FromArgb(r, g, b);
    }

    private static string NormalizeHex(string hex) =>
        hex.StartsWith("#", StringComparison.Ordinal) ? hex : $"#{hex}";

    private static XPoint ToXPoint(PdfPoint point) => new(point.X, point.Y);
}
