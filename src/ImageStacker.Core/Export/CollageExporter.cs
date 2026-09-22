using ImageStacker.Core.Color;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Io;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Core.Export;

public static class CollageExporter
{
    public static NetVips.Image BuildCollageImage(
        IReadOnlyList<string> orderedPaths,
        string layoutName,
        bool borderless,
        object color,
        bool bleed = false,
        IReadOnlyList<SlotAssignment>? slots = null,
        SourceImageCache? cache = null)
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute(layoutName, borderless, bleed);
        if (orderedPaths.Count != geometry.NumImages)
        {
            throw new ArgumentException(
                $"Need exactly {geometry.NumImages} images for layout '{layoutName}', got {orderedPaths.Count}.");
        }

        if (slots is not null && slots.Count != geometry.NumImages)
        {
            throw new ArgumentException(
                $"slot assignments length must be {geometry.NumImages}, got {slots.Count}.");
        }

        var canvasColor = ColorParser.Parse(color);
        bool ownsCache = cache is null;
        cache ??= new SourceImageCache();
        using var cells = new DisposableList<NetVips.Image>();

        for (int i = 0; i < geometry.NumImages; i++)
        {
            CellRect size = geometry.CellSizes[i];
            double panX;
            double panY;
            bool flipH;
            bool grayscale;

            if (slots is not null)
            {
                panX = slots[i].PanX;
                panY = slots[i].PanY;
                flipH = slots[i].FlipH;
                grayscale = slots[i].Grayscale;
            }
            else
            {
                panX = CropDefaults.DefaultPanX(layoutName, i);
                panY = CropDefaults.DefaultPanY(layoutName);
                flipH = false;
                grayscale = false;
            }

            NetVips.Image? processed = ImagePipeline.ProcessImageForCell(
                cache,
                orderedPaths[i],
                size.Width,
                size.Height,
                panX,
                panY,
                flipH,
                grayscale);

            if (processed is null)
            {
                throw new InvalidOperationException(
                    $"Image unusable (read error): {orderedPaths[i]}");
            }

            cells.Add(processed);
        }

        NetVips.Image result = ComposeCanvas(
            cells, canvasColor, geometry.Positions, Constants.CanvasWidth, Constants.CanvasHeight);
        if (ownsCache)
        {
            cache.Dispose();
        }

        return result;
    }

    public static void ExportCollage(
        IReadOnlyList<string> orderedPaths,
        string layoutName,
        bool borderless,
        object color,
        string outputPath,
        bool bleed = false,
        IReadOnlyList<SlotAssignment>? slots = null,
        SourceImageCache? cache = null)
    {
        bool ownsCache = cache is null;
        cache ??= new SourceImageCache();
        try
        {
            using var collage = BuildCollageImage(
                orderedPaths, layoutName, borderless, color, bleed, slots, cache);
            JpegEncoder.SaveOptimized(collage, outputPath);
        }
        finally
        {
            if (ownsCache)
            {
                cache.Dispose();
            }
        }
    }

    private static int _outputFilenameSequence;

    public static string GenerateOutputFilename(string outputDir, string prefix, int jobIndex)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        int seq = Interlocked.Increment(ref _outputFilenameSequence);
        long ticks = Environment.TickCount64;
        string fileName = $"{prefix}_{jobIndex:000}_{timestamp}_{seq:000000}_{ticks}.jpg";
        return Path.Combine(outputDir, fileName);
    }
    public static string GenerateManualOutputFilename(string outputDir)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        int rand = Random.Shared.Next(100, 1000);
        string fileName = $"manual_{timestamp}_{rand}.jpg";
        return Path.Combine(outputDir, fileName);
    }

    internal static NetVips.Image ComposeCanvas(
        IReadOnlyList<NetVips.Image> cells,
        (byte R, byte G, byte B) canvasColor,
        IReadOnlyList<CellRect> positions,
        int canvasWidth,
        int canvasHeight)
    {
        using var background = NetVips.Image.Black(canvasWidth, canvasHeight)
            .NewFromImage(new double[] { canvasColor.R, canvasColor.G, canvasColor.B })
            .Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);

        NetVips.Image canvas = ImagePipeline.EnsureRgb(background);
        for (int i = 0; i < cells.Count; i++)
        {
            CellRect pos = positions[i];
            using var cell = ImagePipeline.EnsureRgb(cells[i]);
            NetVips.Image next = canvas.Insert(cell, pos.X, pos.Y);
            canvas.Dispose();
            canvas = next;
        }

        return canvas;
    }

    private sealed class DisposableList<T> : List<T>, IDisposable where T : IDisposable
    {
        public void Dispose()
        {
            foreach (T item in this)
            {
                item.Dispose();
            }

            Clear();
        }
    }
}
