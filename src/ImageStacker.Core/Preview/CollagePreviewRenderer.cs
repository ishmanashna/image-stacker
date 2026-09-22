using ImageStacker.Core.Color;
using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Core.Preview;

public static class CollagePreviewRenderer
{
    public static NetVips.Image Render(
        IReadOnlyList<string> orderedPaths,
        string layoutName,
        bool borderless,
        object color,
        bool bleed = false,
        IReadOnlyList<SlotAssignment>? slots = null,
        int previewLongEdge = 1000,
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

        double scale = previewLongEdge / (double)Math.Max(geometry.CanvasWidth, geometry.CanvasHeight);
        int canvasW = Math.Max(1, (int)Math.Round(geometry.CanvasWidth * scale));
        int canvasH = Math.Max(1, (int)Math.Round(geometry.CanvasHeight * scale));
        ScaleGeometrySeamless(geometry, scale, canvasW, canvasH, out var scaledPositions, out var scaledCellSizes);

        var canvasColor = ColorParser.Parse(color);
        using var cells = new DisposableList<NetVips.Image>();

        for (int i = 0; i < geometry.NumImages; i++)
        {
            (int tw, int th) = scaledCellSizes[i];
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

            NetVips.Image? processed = ImagePipeline.ProcessImageForPreviewCell(
                orderedPaths[i],
                tw,
                th,
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

        NetVips.Image result = CollageExporter.ComposeCanvas(
            cells, canvasColor, scaledPositions, canvasW, canvasH);
        if (noise || orton)
        {
            using var composed = result;
            result = PostComposeEffects.Apply(composed, noise, orton);
        }

        return result;
    }

    public static NetVips.Image RenderManual(
        string layoutName,
        bool borderless,
        object color,
        bool bleed,
        IReadOnlyList<SlotAssignment?> slots,
        int previewLongEdge = 1000,
        int? livePanSlot = null,
        double? livePanX = null,
        double? livePanY = null,
        bool noise = false,
        bool orton = false)
    {
        IReadOnlyList<string>? canvasPaths = ResolveCanvasPathsForManual(layoutName, slots);
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute(layoutName, borderless, bleed, canvasPaths);
        if (slots.Count != geometry.NumImages)
        {
            throw new ArgumentException(
                $"Need exactly {geometry.NumImages} slot entries for layout '{layoutName}', got {slots.Count}.");
        }

        double scale = previewLongEdge / (double)Math.Max(geometry.CanvasWidth, geometry.CanvasHeight);
        int canvasW = Math.Max(1, (int)Math.Round(geometry.CanvasWidth * scale));
        int canvasH = Math.Max(1, (int)Math.Round(geometry.CanvasHeight * scale));
        ScaleGeometrySeamless(geometry, scale, canvasW, canvasH, out var scaledPositions, out var scaledCellSizes);

        var canvasColor = ColorParser.Parse(color);
        using var cells = new DisposableList<NetVips.Image>();

        for (int i = 0; i < geometry.NumImages; i++)
        {
            (int tw, int th) = scaledCellSizes[i];
            SlotAssignment? assignment = slots[i];
            if (assignment is null || string.IsNullOrWhiteSpace(assignment.Path))
            {
                cells.Add(CreateEmptySlotCell(tw, th));
                continue;
            }

            double panX = assignment.PanX;
            double panY = assignment.PanY;
            if (livePanSlot == i && livePanX is not null && livePanY is not null)
            {
                panX = livePanX.Value;
                panY = livePanY.Value;
            }

            NetVips.Image? processed = ImagePipeline.ProcessImageForPreviewCell(
                assignment.Path,
                tw,
                th,
                panX,
                panY,
                assignment.FlipH,
                assignment.Grayscale);

            if (processed is null)
            {
                cells.Add(CreateEmptySlotCell(tw, th));
                continue;
            }

            cells.Add(processed);
        }

        NetVips.Image result = CollageExporter.ComposeCanvas(
            cells, canvasColor, scaledPositions, canvasW, canvasH);
        if (noise || orton)
        {
            using var composed = result;
            result = PostComposeEffects.Apply(composed, noise, orton);
        }

        return result;
    }

    private static IReadOnlyList<string>? ResolveCanvasPathsForManual(
        string layoutName,
        IReadOnlyList<SlotAssignment?> slots)
    {
        if (!string.Equals(layoutName, "stack-1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (SlotAssignment? slot in slots)
        {
            if (!string.IsNullOrWhiteSpace(slot?.Path))
            {
                return new[] { slot.Path };
            }
        }

        return null;
    }

    private static NetVips.Image CreateEmptySlotCell(int width, int height)
    {
        return NetVips.Image.Black(width, height)
            .NewFromImage(new double[] { 48, 48, 48 })
            .Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
    }

    /// <summary>
    /// Scale full-res cell rects so adjacent tiles share the same edge pixel after rounding.
    /// Rounding X/Y and W/H independently leaves 1px hairlines (e.g. stack-3 at preview scale).
    /// </summary>
    private static void ScaleGeometrySeamless(
        LayoutGeometry geometry,
        double scale,
        int canvasW,
        int canvasH,
        out List<CellRect> scaledPositions,
        out List<(int Width, int Height)> scaledCellSizes)
    {
        scaledPositions = new List<CellRect>(geometry.NumImages);
        scaledCellSizes = new List<(int Width, int Height)>(geometry.NumImages);

        for (int i = 0; i < geometry.NumImages; i++)
        {
            CellRect pos = geometry.Positions[i];
            CellRect size = geometry.CellSizes[i];

            int x0 = (int)Math.Round(pos.X * scale);
            int y0 = (int)Math.Round(pos.Y * scale);
            int x1 = (int)Math.Round((pos.X + size.Width) * scale);
            int y1 = (int)Math.Round((pos.Y + size.Height) * scale);

            // Seal cells that touch the full-res canvas edge to the scaled canvas edge.
            if (pos.X <= 0)
            {
                x0 = 0;
            }

            if (pos.Y <= 0)
            {
                y0 = 0;
            }

            if (pos.X + size.Width >= geometry.CanvasWidth)
            {
                x1 = canvasW;
            }

            if (pos.Y + size.Height >= geometry.CanvasHeight)
            {
                y1 = canvasH;
            }

            int w = Math.Max(1, x1 - x0);
            int h = Math.Max(1, y1 - y0);
            scaledPositions.Add(new CellRect(x0, y0, w, h));
            scaledCellSizes.Add((w, h));
        }
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
