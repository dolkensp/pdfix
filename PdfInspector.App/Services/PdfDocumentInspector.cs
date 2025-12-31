using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PdfInspector.App.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace PdfInspector.App.Services;

public sealed class PdfDocumentInspector
{
    public DocumentComponents Inspect(string pdfPath, IReadOnlyCollection<int>? requestedPages, BoundingBoxFilter? vectorBounds, ColorFilter? strokeColorFilter)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF file not found.", pdfPath);
        }

        using var document = PdfDocument.Open(pdfPath);
        var pages = BuildPageList(document, requestedPages, vectorBounds, strokeColorFilter);

        return new DocumentComponents(
            FileName: Path.GetFileName(pdfPath),
            FileSizeBytes: new FileInfo(pdfPath).Length,
            PageCount: document.NumberOfPages,
            Metadata: BuildMetadata(document),
            Pages: pages);
    }

    private static IReadOnlyList<PageComponents> BuildPageList(PdfDocument document, IReadOnlyCollection<int>? requestedPages, BoundingBoxFilter? vectorBounds, ColorFilter? strokeColorFilter)
    {
        var allowedPages = requestedPages ?? Enumerable.Range(1, document.NumberOfPages).ToArray();
        var invalidPage = allowedPages.FirstOrDefault(p => p < 1 || p > document.NumberOfPages);
        if (invalidPage != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPages), $"Requested page {invalidPage} does not exist (document has {document.NumberOfPages}).");
        }

        var result = new List<PageComponents>();
        foreach (var number in allowedPages.OrderBy(p => p))
        {
            var page = document.GetPage(number);
            result.Add(BuildPageComponents(page, vectorBounds, strokeColorFilter));
        }

        return result;
    }

    private static PageComponents BuildPageComponents(Page page, BoundingBoxFilter? vectorBounds, ColorFilter? strokeColorFilter)
    {
        var words = ExtractWords(page);
        var images = ExtractImages(page);
        var paths = ExtractPaths(page, vectorBounds, strokeColorFilter);
        var operationCounts = SummarizeOperations(page);

        return new PageComponents(
            PageNumber: page.Number,
            Width: page.Width,
            Height: page.Height,
            Rotation: page.Rotation.Value,
            Size: page.Size.ToString(),
            TextSample: BuildTextPreview(words),
            Words: words,
            Images: images,
            VectorPaths: paths,
            OperationCounts: operationCounts);
    }

    private static PdfMetadata BuildMetadata(PdfDocument document)
    {
        var info = document.Information;
        return new PdfMetadata(
            Title: info.Title,
            Author: info.Author,
            Subject: info.Subject,
            Keywords: info.Keywords,
            Creator: info.Creator,
            Producer: info.Producer,
            PdfVersion: (decimal?)document.Version,
            CreationDate: info.GetCreatedDateTimeOffset()?.ToString("o", CultureInfo.InvariantCulture),
            ModificationDate: info.GetModifiedDateTimeOffset()?.ToString("o", CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<TextComponent> ExtractWords(Page page)
    {
        var result = new List<TextComponent>();
        foreach (var word in page.GetWords())
        {
            if (string.IsNullOrWhiteSpace(word.Text))
            {
                continue;
            }

            var bounds = ToBoundingBox(word.BoundingBox);
            var fontSize = word.Letters.Count > 0 ? word.Letters.Average(l => l.FontSize) : 0;
            var fontName = word.Letters.Count > 0 ? word.Letters[0].FontName : null;

            result.Add(new TextComponent(word.Text, bounds, fontName, fontSize, word.TextOrientation.ToString()));
        }

        return result;
    }

    private static IReadOnlyList<ImageComponent> ExtractImages(Page page)
    {
        var result = new List<ImageComponent>();
        foreach (var image in page.GetImages())
        {
            var bounds = ToBoundingBox(image.Bounds);
            result.Add(new ImageComponent(
                Bounds: bounds,
                Width: image.WidthInSamples,
                Height: image.HeightInSamples,
                BitsPerComponent: image.BitsPerComponent,
                RenderingIntent: image.RenderingIntent.ToString(),
                IsMask: image.IsImageMask,
                ColorSpace: image.ColorSpaceDetails?.Type.ToString()));
        }

        return result;
    }

    private static IReadOnlyList<VectorPathComponent> ExtractPaths(Page page, BoundingBoxFilter? boundsFilter, ColorFilter? strokeColorFilter)
    {
        var result = new List<VectorPathComponent>();
        var paths = page.ExperimentalAccess.Paths ?? Array.Empty<PdfPath>();

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            var bounds = path.GetBoundingRectangle();

            if (boundsFilter != null)
            {
                if (bounds is not { } rectangle || !boundsFilter.Intersects(rectangle))
                {
                    continue;
                }
            }

            if (strokeColorFilter != null && (!path.IsStroked || !strokeColorFilter.Matches(path.StrokeColor)))
            {
                continue;
            }

            var strokeHex = ColorUtilities.ToHex(path.StrokeColor);
            var fillHex = ColorUtilities.ToHex(path.FillColor);

            result.Add(new VectorPathComponent(
                Index: i,
                Bounds: bounds is { } rect ? ToBoundingBox(rect) : null,
                IsClipping: path.IsClipping,
                IsFilled: path.IsFilled,
                IsStroked: path.IsStroked,
                FillColor: path.FillColor?.ToString(),
                FillColorHex: fillHex,
                StrokeColor: path.StrokeColor?.ToString(),
                StrokeColorHex: strokeHex,
                LineWidth: path.IsStroked ? path.LineWidth : null,
                DashPattern: path.LineDashPattern?.ToString(),
                LineCap: path.LineCapStyle.ToString(),
                LineJoin: path.LineJoinStyle.ToString(),
                Subpaths: ExtractSubpaths(path)));
        }

        return result;
    }

    private static IReadOnlyList<SubpathComponent> ExtractSubpaths(PdfPath path)
    {
        var result = new List<SubpathComponent>();
        foreach (var subpath in path)
        {
            var commands = subpath.Commands.Select(DescribeCommand).ToList();
            var bounds = subpath.GetBoundingRectangle();

            result.Add(new SubpathComponent(
                IsClosed: subpath.IsClosed(),
                IsRectangle: subpath.IsDrawnAsRectangle,
                Bounds: bounds is { } rectangle ? ToBoundingBox(rectangle) : null,
                Commands: commands));
        }

        return result;
    }

    private static PathCommandComponent DescribeCommand(PdfSubpath.IPathCommand command)
    {
        return command switch
        {
            PdfSubpath.Move move => new PathCommandComponent("Move", $"Move to {FormatPoint(move.Location)}"),
            PdfSubpath.Line line => new PathCommandComponent("Line", $"Line {FormatPoint(line.From)} -> {FormatPoint(line.To)}"),
            PdfSubpath.BezierCurve curve => new PathCommandComponent(
                "Bezier",
                $"Curve {FormatPoint(curve.StartPoint)} -> {FormatPoint(curve.EndPoint)} (c1 {FormatPoint(curve.FirstControlPoint)}, c2 {FormatPoint(curve.SecondControlPoint)})"),
            PdfSubpath.Close => new PathCommandComponent("Close", "Close current path"),
            _ => new PathCommandComponent(command.GetType().Name, command.ToString() ?? command.GetType().Name)
        };
    }

    private static IReadOnlyDictionary<string, int> SummarizeOperations(Page page)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in page.Operations)
        {
            var key = operation.Operator;
            counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
        }

        return counts;
    }

    private static string BuildTextPreview(IReadOnlyList<TextComponent> words)
    {
        var preview = string.Join(" ", words.Select(w => w.Text));
        const int maxLength = 240;
        if (preview.Length <= maxLength)
        {
            return preview;
        }

        return preview[..maxLength] + "...";
    }

    private static BoundingBox ToBoundingBox(PdfRectangle rectangle) =>
        new(rectangle.Left, rectangle.Bottom, rectangle.Width, rectangle.Height);

    private static string FormatPoint(PdfPoint point) => $"({point.X:0.###},{point.Y:0.###})";
}
