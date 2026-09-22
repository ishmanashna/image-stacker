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
        SourceImageCache? cache = null,
        bool noise = false,
        bool orton = false)
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute(layoutName, borderless, bleed, orderedPaths);
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
            cells, canvasColor, geometry.Positions, geometry.CanvasWidth, geometry.CanvasHeight);
        if (noise || orton)
        {
            using var composed = result;
            result = PostComposeEffects.Apply(composed, noise, orton);
        }

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
        SourceImageCache? cache = null,
        bool noise = false,
        bool orton = false)
    {
        bool ownsCache = cache is null;
        cache ??= new SourceImageCache();
        try
        {
            using var collage = BuildCollageImage(
                orderedPaths, layoutName, borderless, color, bleed, slots, cache, noise, orton);
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
        int rampWidth = ComputeBleedRampWidth(canvasWidth, canvasHeight);
        for (int i = 0; i < cells.Count; i++)
        {
            CellRect pos = positions[i];
            using var cell = ImagePipeline.EnsureRgb(cells[i]);
            NetVips.Image next;
            if (TryGetBlendEdges(i, positions, cells, out bool rampTop, out bool rampLeft))
            {
                using var alpha = BuildBleedAlphaMask(cell.Width, cell.Height, rampWidth, rampTop, rampLeft);
                using var cellRgba = cell.Bandjoin(alpha);
                next = canvas.Composite2(cellRgba, Enums.BlendMode.Over, pos.X, pos.Y);
            }
            else
            {
                next = canvas.Insert(cell, pos.X, pos.Y);
            }

            canvas.Dispose();
            canvas = next;
        }

        return canvas;
    }

    /// <summary>16 px ramp at full 3840×4800 reference; scales with preview canvas size.</summary>
    private static int ComputeBleedRampWidth(int canvasWidth, int canvasHeight)
    {
        const int fullRampPx = 16;
        const int referenceMinSide = 3840;
        int minSide = Math.Min(canvasWidth, canvasHeight);
        return Math.Max(1, (int)Math.Round(fullRampPx * minSide / (double)referenceMinSide));
    }

    private static bool TryGetBlendEdges(
        int index,
        IReadOnlyList<CellRect> positions,
        IReadOnlyList<NetVips.Image> cells,
        out bool rampTop,
        out bool rampLeft)
    {
        rampTop = false;
        rampLeft = false;
        if (index <= 0)
        {
            return false;
        }

        CellRect pos = positions[index];
        int px = pos.X;
        int py = pos.Y;
        int pw = cells[index].Width;
        int ph = cells[index].Height;
        bool anyOverlap = false;

        for (int j = 0; j < index; j++)
        {
            CellRect pj = positions[j];
            int jw = cells[j].Width;
            int jh = cells[j].Height;
            int ix0 = Math.Max(px, pj.X);
            int iy0 = Math.Max(py, pj.Y);
            int ix1 = Math.Min(px + pw, pj.X + jw);
            int iy1 = Math.Min(py + ph, pj.Y + jh);
            if (ix0 >= ix1 || iy0 >= iy1)
            {
                continue;
            }

            anyOverlap = true;
            if (iy0 == py || iy1 == py + ph)
            {
                rampTop = true;
            }

            if (ix0 == px || ix1 == px + pw)
            {
                rampLeft = true;
            }
        }

        if (anyOverlap && !rampTop && !rampLeft)
        {
            rampTop = true;
            rampLeft = true;
        }

        return anyOverlap;
    }

    private static NetVips.Image BuildBleedAlphaMask(
        int width,
        int height,
        int rampWidth,
        bool rampTop,
        bool rampLeft)
    {
        rampWidth = Math.Max(1, rampWidth);
        using var xyz = NetVips.Image.Xyz(width, height);
        NetVips.Image? mask = null;

        double invRamp = 1.0 / rampWidth;
        if (rampTop)
        {
            using var y = xyz.ExtractBand(1).Cast(Enums.BandFormat.Double);
            using var t = y.Linear(new[] { invRamp }, new[] { 0.0 }).Clamp(0, 1);
            mask = t.Linear(new[] { 255.0 }, new[] { 0.0 }).Cast(Enums.BandFormat.Uchar);
        }

        if (rampLeft)
        {
            using var x = xyz.ExtractBand(0).Cast(Enums.BandFormat.Double);
            using var t = x.Linear(new[] { invRamp }, new[] { 0.0 }).Clamp(0, 1);
            using var left = t.Linear(new[] { 255.0 }, new[] { 0.0 }).Cast(Enums.BandFormat.Uchar);
            if (mask is null)
            {
                mask = left.Copy();
            }
            else
            {
                using var top = mask;
                mask = top.Minpair(left);
            }
        }

        if (mask is null)
        {
            return NetVips.Image.Black(width, height)
                .NewFromImage(new double[] { 255 })
                .Cast(Enums.BandFormat.Uchar);
        }

        NetVips.Image copied = mask.Copy();
        mask.Dispose();
        return copied;
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
