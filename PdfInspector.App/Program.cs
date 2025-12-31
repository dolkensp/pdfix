using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PdfInspector.App.Models;
using PdfInspector.App.Services;

namespace PdfInspector.App;

public static class Program
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int Main(string[] args)
    {
        var parseResult = CommandLineOptions.Parse(args);
        if (!parseResult.IsSuccess)
        {
            PrintUsage(parseResult.Error);
            return 1;
        }

        var options = parseResult.Options!;
        var inspector = new PdfDocumentInspector();

        try
        {
            var report = inspector.Inspect(options.PdfPath, options.Pages, options.VectorBounds, options.StrokeColorFilter);
            RenderSummary(report, options);

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                SaveReport(report, options.OutputPath!);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void RenderSummary(DocumentComponents report, CommandLineOptions options)
    {
        Console.WriteLine($"File: {report.FileName}");
        Console.WriteLine($"Pages: {report.PageCount}");
        if (options.Pages != null)
        {
            Console.WriteLine($"Requested pages: {string.Join(", ", options.Pages.OrderBy(p => p))}");
        }

        if (options.VectorBounds != null)
        {
            Console.WriteLine($"Vector bounding filter: {options.VectorBounds}");
        }

        if (options.StrokeColorFilter != null)
        {
            Console.WriteLine($"Stroke color filter: {options.StrokeColorFilter}");
        }

        Console.WriteLine();
        foreach (var page in report.Pages)
        {
            Console.WriteLine($"Page {page.PageNumber} ({page.Width:0.##} x {page.Height:0.##}, {page.Size}, rotation {page.Rotation}°)");
            Console.WriteLine($"  Text fragments: {page.Words.Count}, Images: {page.Images.Count}, Vector paths: {page.VectorPaths.Count}");

            var topOperations = page.OperationCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => $"{kvp.Key}:{kvp.Value}");

            Console.WriteLine($"  Dominant operations: {string.Join(", ", topOperations)}");

            if (!string.IsNullOrWhiteSpace(page.TextSample))
            {
                Console.WriteLine($"  Text preview: {page.TextSample}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Tip: use --vector-bbox to isolate paths in a specific area and --output to capture full JSON.");
    }

    private static void SaveReport(DocumentComponents report, string path)
    {
        var json = JsonSerializer.Serialize(report, SerializerOptions);
        File.WriteAllText(path, json, Encoding.UTF8);
        Console.WriteLine($"Saved component report to {path}");
    }

    private static void PrintUsage(string? error = null)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.WriteLine("PDF Inspector");
        Console.WriteLine("Usage: dotnet run --project PdfInspector.App -- <pdf path> [--pages 1,3-4] [--vector-bbox minX,minY,maxX,maxY] [--output components.json]");
    }

    private sealed record ParseResult(bool IsSuccess, CommandLineOptions? Options, string? Error);

    private sealed record CommandLineOptions(
        string PdfPath,
        IReadOnlyCollection<int>? Pages,
        string? OutputPath,
        BoundingBoxFilter? VectorBounds,
        ColorFilter? StrokeColorFilter)
    {
        public static ParseResult Parse(string[] args)
        {
            string? pdfPath = null;
            string? outputPath = null;
            BoundingBoxFilter? vectorBounds = null;
            ColorFilter? strokeColorFilter = null;
            IReadOnlyCollection<int>? pages = null;

            for (var i = 0; i < args.Length; i++)
            {
                var value = args[i];
                switch (value)
                {
                    case "-h":
                    case "--help":
                        return new ParseResult(false, null, null);
                    case "--pages":
                        if (i + 1 >= args.Length)
                        {
                            return new ParseResult(false, null, "--pages requires a value like 1,3-4.");
                        }

                        pages = ParsePages(args[++i], out var pageError);
                        if (pages == null)
                        {
                            return new ParseResult(false, null, pageError);
                        }

                        break;
                    case "--output":
                        if (i + 1 >= args.Length)
                        {
                            return new ParseResult(false, null, "--output requires a file path.");
                        }

                        outputPath = args[++i];
                        break;
                    case "--vector-bbox":
                        if (i + 1 >= args.Length)
                        {
                            return new ParseResult(false, null, "--vector-bbox requires four comma-separated values.");
                        }

                        if (!BoundingBoxFilter.TryParse(args[++i], out vectorBounds, out var bboxError))
                        {
                            return new ParseResult(false, null, bboxError);
                        }

                        break;
                    case "--line-color":
                        if (i + 1 >= args.Length)
                        {
                            return new ParseResult(false, null, "--line-color requires a hex color like #ffcccc.");
                        }

                        if (!ColorFilter.TryParse(args[++i], out strokeColorFilter, out var colorError))
                        {
                            return new ParseResult(false, null, colorError);
                        }

                        break;
                    default:
                        if (value.StartsWith("--", StringComparison.Ordinal))
                        {
                            return new ParseResult(false, null, $"Unknown option {value}.");
                        }

                        pdfPath ??= value;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                return new ParseResult(false, null, "Please provide a PDF file path.");
            }

            var options = new CommandLineOptions(pdfPath, pages, outputPath, vectorBounds, strokeColorFilter);
            return new ParseResult(true, options, null);
        }

        private static IReadOnlyCollection<int>? ParsePages(string value, out string? error)
        {
            var selections = new HashSet<int>();
            error = null;

            var segments = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                error = "Page selection cannot be empty.";
                return null;
            }

            foreach (var segment in segments)
            {
                if (segment.Contains('-', StringComparison.Ordinal))
                {
                    var bounds = segment.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    if (bounds.Length != 2 ||
                        !int.TryParse(bounds[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
                        !int.TryParse(bounds[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end))
                    {
                        error = "Page ranges must look like 2-5.";
                        return null;
                    }

                    if (start <= 0 || end < start)
                    {
                        error = "Page ranges must be positive and increasing.";
                        return null;
                    }

                    for (var i = start; i <= end; i++)
                    {
                        selections.Add(i);
                    }
                }
                else
                {
                    if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) || page <= 0)
                    {
                        error = $"Invalid page number: {segment}";
                        return null;
                    }

                    selections.Add(page);
                }
            }

            return selections;
        }
    }
}
