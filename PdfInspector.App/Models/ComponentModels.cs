using System.Collections.Generic;

namespace PdfInspector.App.Models;

public record BoundingBox(double Left, double Bottom, double Width, double Height)
{
    public double Right => Left + Width;
    public double Top => Bottom + Height;

    public override string ToString() => $"({Left:0.###}, {Bottom:0.###}, {Width:0.###}, {Height:0.###})";
}

public record PdfMetadata(
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    decimal? PdfVersion,
    string? CreationDate,
    string? ModificationDate);

public record TextComponent(string Text, BoundingBox Bounds, string? FontName, double FontSize, string Orientation);

public record ImageComponent(
    BoundingBox Bounds,
    int Width,
    int Height,
    int BitsPerComponent,
    string RenderingIntent,
    bool IsMask,
    string? ColorSpace);

public record PathCommandComponent(string Kind, string Summary);

public record SubpathComponent(bool IsClosed, bool IsRectangle, BoundingBox? Bounds, IReadOnlyList<PathCommandComponent> Commands);

public record VectorPathComponent(
    int Index,
    BoundingBox? Bounds,
    bool IsClipping,
    bool IsFilled,
    bool IsStroked,
    string? FillColor,
    string? StrokeColor,
    decimal? LineWidth,
    string? DashPattern,
    string? LineCap,
    string? LineJoin,
    IReadOnlyList<SubpathComponent> Subpaths);

public record PageComponents(
    int PageNumber,
    double Width,
    double Height,
    int Rotation,
    string Size,
    string TextSample,
    IReadOnlyList<TextComponent> Words,
    IReadOnlyList<ImageComponent> Images,
    IReadOnlyList<VectorPathComponent> VectorPaths,
    IReadOnlyDictionary<string, int> OperationCounts);

public record DocumentComponents(
    string FileName,
    long FileSizeBytes,
    int PageCount,
    PdfMetadata Metadata,
    IReadOnlyList<PageComponents> Pages);
