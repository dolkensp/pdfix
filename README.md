# PDF Inspector

This repository contains a .NET 8 console app that inspects a PDF and lists the key components of each page (text, images, vector paths, and graphics operations). It is geared toward locating and isolating vector content for later modification.

## Quick start

```bash
# From the repo root
PATH="$HOME/.dotnet:$PATH" dotnet run --project PdfInspector.App -- sample.pdf \
  --output components.json \
  --vector-bbox 0,0,400,400 \
  --pages 1-2
```

Options:

- `--pages 1,3-4` – restricts inspection to specific pages.
- `--vector-bbox minX,minY,maxX,maxY` – only include vector paths whose bounds intersect the provided rectangle (PDF coordinate space).
- `--output <file>` – write the full JSON report to a file.

## Output structure

The JSON report includes:

- Document metadata (title, author, creator, producer, PDF version, creation/modification dates).
- Per-page dimensions, rotation, a text preview, and operation counts (top graphics operators on the page).
- Text entries (text content, bounding box, font name/size, orientation).
- Images (bounds, pixel dimensions, bits per component, rendering intent, color space, mask flag).
- Vector paths with stroke/fill details and subpath commands (moves, lines, Bezier curves) plus bounding boxes.

Use the `--vector-bbox` filter to zero in on vectors drawn inside a specific area (e.g., around a logo or illustration) before editing the PDF with another tool.
