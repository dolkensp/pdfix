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
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;

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
        using var target = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

        for (var pageNumber = 1; pageNumber <= source.NumberOfPages; pageNumber++)
        {
            var pigPage = source.GetPage(pageNumber);
            var sharpPage = target.Pages[pageNumber - 1];

            if (targetPage != pageNumber)
            {
                continue;
            }

            var paths = pigPage.ExperimentalAccess.Paths?.ToList() ?? new List<PdfPath>();
            if (targetPathId is null || targetPathId < 0 || targetPathId >= paths.Count)
            {
                continue;
            }

            var path = paths[targetPathId.Value];
            var endpoints = GetFirstLineEndpoints(path);
            if (endpoints is null)
            {
                continue;
            }

            var strokeHex = newStrokeColorHex ?? ColorUtilities.ToHex(path.StrokeColor);
            strokeHex = NormalizeHex(strokeHex ?? "#000000");
            var (start, end) = endpoints.Value;

            var finalStart = newStart.HasValue ? GeometryConverters.ToPoint(newStart.Value) : start;
            var finalEnd = newEnd.HasValue ? GeometryConverters.ToPoint(newEnd.Value) : end;

            RemovePath(sharpPage, targetPathId.Value);

            using var gfx = XGraphics.FromPdfPage(sharpPage, XGraphicsPdfPageOptions.Append);
            DrawOverlayLine(gfx, sharpPage.Width, sharpPage.Height, pigPage.Rotation.Value, finalStart, finalEnd, strokeHex, path);
        }

        target.Save(outputPath);
    }

    public void RandomizeLineColors(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required for writing.", nameof(outputPath));
        }

        using var source = UglyToadPdf.PdfDocument.Open(inputPath);
        using var target = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        var random = new Random(1234);

        for (var pageNumber = 1; pageNumber <= source.NumberOfPages; pageNumber++)
        {
            var pigPage = source.GetPage(pageNumber);
            var sharpPage = target.Pages[pageNumber - 1];
            var paths = pigPage.ExperimentalAccess.Paths?.ToList() ?? new List<PdfPath>();

            using var gfx = XGraphics.FromPdfPage(sharpPage, XGraphicsPdfPageOptions.Append);

            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var strokeHex = RandomHex(random);
                var endpoints = GetFirstLineEndpoints(path);
                if (endpoints is null)
                {
                    continue;
                }

                var (start, end) = endpoints.Value;
                DrawOverlayLine(gfx, sharpPage.Width, sharpPage.Height, pigPage.Rotation.Value, start, end, strokeHex, path);
                Console.WriteLine($"Page {pageNumber}, Path {i}, Stroke {strokeHex}");
            }
        }

        target.Save(outputPath);
    }

    private static (PdfPoint start, PdfPoint end)? GetFirstLineEndpoints(PdfPath path)
    {
        foreach (var subpath in path)
        {
            PdfPoint? start = null;
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        start = move.Location;
                        break;
                    case PdfSubpath.Line line when start != null:
                        return ((start.Value, line.To));
                    case PdfSubpath.Line line:
                        return ((line.From, line.To));
                }
            }
        }

        return null;
    }

    private static void RemovePath(PdfPage page, int pathIndexToRemove)
    {
        var content = ContentReader.ReadContent(page);
        var filtered = new CSequence();
        var pathBuffer = new List<CObject>();
        var stateBuffer = new List<CObject>();
        var trackingPath = false;
        var currentPathIndex = -1;

        void FlushStateBuffer()
        {
            foreach (var item in stateBuffer)
            {
                filtered.Add(item);
            }

            stateBuffer.Clear();
        }

        foreach (var element in content)
        {
            if (element is COperator op)
            {
                var name = op.OpCode.OpCodeName;

                if (!trackingPath && IsStateOperator(name))
                {
                    stateBuffer.Add(op);
                    continue;
                }

                if (!trackingPath && IsPathConstructionOperator(name))
                {
                    trackingPath = true;
                    foreach (var item in stateBuffer)
                    {
                        pathBuffer.Add(item);
                    }

                    stateBuffer.Clear();
                    pathBuffer.Add(op);
                    continue;
                }

                if (trackingPath)
                {
                    pathBuffer.Add(op);

                    if (IsPathTerminatingOperator(name))
                    {
                        currentPathIndex++;
                        if (currentPathIndex != pathIndexToRemove)
                        {
                            foreach (var item in pathBuffer)
                            {
                                filtered.Add(item);
                            }
                        }

                        pathBuffer.Clear();
                        trackingPath = false;
                    }

                    continue;
                }

                FlushStateBuffer();
                filtered.Add(op);
                continue;
            }

            if (trackingPath)
            {
                pathBuffer.Add(element);
            }
            else
            {
                FlushStateBuffer();
                filtered.Add(element);
            }
        }

        if (pathBuffer.Count > 0)
        {
            foreach (var item in pathBuffer)
            {
                filtered.Add(item);
            }
        }

        FlushStateBuffer();
        page.Contents.ReplaceContent(filtered);
    }

    private static void DrawOverlayLine(XGraphics gfx, double pageWidth, double pageHeight, int rotation, PdfPoint start, PdfPoint end, string? strokeHex, PdfPath sourcePath)
    {
        var strokeColor = ParseColor(strokeHex);
        if (strokeColor is null)
        {
            return;
        }

        var pen = new XPen(strokeColor.Value, (double)(sourcePath.IsStroked ? sourcePath.LineWidth : 0.5m));
        var from = TransformPoint(start, pageWidth, pageHeight, rotation);
        var to = TransformPoint(end, pageWidth, pageHeight, rotation);
        gfx.DrawLine(pen, from, to);
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

    private static string RandomHex(Random random)
    {
        var r = random.Next(0, 256);
        var g = random.Next(0, 256);
        var b = random.Next(0, 256);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static XPoint TransformPoint(PdfPoint point, double pageWidth, double pageHeight, int rotationDegrees) =>
        rotationDegrees switch
        {
            90 => new XPoint(point.Y, point.X),
            180 => new XPoint(pageWidth - point.X, point.Y),
            270 => new XPoint(pageHeight - point.Y, pageWidth - point.X),
            _ => new XPoint(point.X, pageHeight - point.Y)
        };

    private static bool IsPathConstructionOperator(OpCodeName op) =>
        op is OpCodeName.m or OpCodeName.l or OpCodeName.c or OpCodeName.v or OpCodeName.y or OpCodeName.h or OpCodeName.re;

    private static bool IsPathTerminatingOperator(OpCodeName op) =>
        op is OpCodeName.S or OpCodeName.s or OpCodeName.f or OpCodeName.F or OpCodeName.fx or OpCodeName.B or OpCodeName.Bx or OpCodeName.b or OpCodeName.bx or OpCodeName.n or OpCodeName.W or OpCodeName.Wx;

    private static bool IsStateOperator(OpCodeName op) =>
        op is OpCodeName.q or OpCodeName.Q
            or OpCodeName.cm
            or OpCodeName.w
            or OpCodeName.J
            or OpCodeName.j
            or OpCodeName.M
            or OpCodeName.d
            or OpCodeName.ri
            or OpCodeName.i
            or OpCodeName.gs
            or OpCodeName.CS
            or OpCodeName.cs
            or OpCodeName.SC
            or OpCodeName.SCN
            or OpCodeName.sc
            or OpCodeName.scn
            or OpCodeName.G
            or OpCodeName.g
            or OpCodeName.RG
            or OpCodeName.rg
            or OpCodeName.K
            or OpCodeName.k
            or OpCodeName.CA
            or OpCodeName.ca;
}
