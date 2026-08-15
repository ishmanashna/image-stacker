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
        int previewLongEdge = 1000)
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

        double scale = previewLongEdge / (double)Math.Max(Constants.CanvasWidth, Constants.CanvasHeight);
        int canvasW = Math.Max(1, (int)Math.Round(Constants.CanvasWidth * scale));
        int canvasH = Math.Max(1, (int)Math.Round(Constants.CanvasHeight * scale));

        var scaledPositions = new List<CellRect>(geometry.NumImages);
        var scaledCellSizes = new List<(int Width, int Height)>(geometry.NumImages);
        for (int i = 0; i < geometry.NumImages; i++)
        {
            CellRect pos = geometry.Positions[i];
            CellRect size = geometry.CellSizes[i];
            scaledPositions.Add(new CellRect(
                (int)Math.Round(pos.X * scale),
                (int)Math.Round(pos.Y * scale),
                Math.Max(1, (int)Math.Round(pos.Width * scale)),
                Math.Max(1, (int)Math.Round(pos.Height * scale))));

            scaledCellSizes.Add((
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale))));
        }

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
            else if (layoutName.Equals("grid-1x2-v", StringComparison.OrdinalIgnoreCase))
            {
                panX = i == 0 ? -1.0 : 1.0;
                panY = 0.0;
                flipH = false;
                grayscale = false;
            }
            else
            {
                panX = 0.0;
                panY = 0.0;
                flipH = false;
                grayscale = false;
            }

            NetVips.Image? processed = ImagePipeline.ProcessImageForPreviewCell(
                orderedPaths[i],
                tw,
                th,
                geometry.Orientation,
                panX,
                panY,
                flipH,
                grayscale);

            if (processed is null)
            {
                throw new InvalidOperationException(
                    $"Image unusable for this layout (orientation or read error): {orderedPaths[i]}");
            }

            cells.Add(processed);
        }

        return CollageExporter.ComposeCanvas(cells, canvasColor, scaledPositions, canvasW, canvasH);
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
        double? livePanY = null)
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute(layoutName, borderless, bleed);
        if (slots.Count != geometry.NumImages)
        {
            throw new ArgumentException(
                $"Need exactly {geometry.NumImages} slot entries for layout '{layoutName}', got {slots.Count}.");
        }

        double scale = previewLongEdge / (double)Math.Max(Constants.CanvasWidth, Constants.CanvasHeight);
        int canvasW = Math.Max(1, (int)Math.Round(Constants.CanvasWidth * scale));
        int canvasH = Math.Max(1, (int)Math.Round(Constants.CanvasHeight * scale));

        var scaledPositions = new List<CellRect>(geometry.NumImages);
        var scaledCellSizes = new List<(int Width, int Height)>(geometry.NumImages);
        for (int i = 0; i < geometry.NumImages; i++)
        {
            CellRect pos = geometry.Positions[i];
            CellRect size = geometry.CellSizes[i];
            scaledPositions.Add(new CellRect(
                (int)Math.Round(pos.X * scale),
                (int)Math.Round(pos.Y * scale),
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale))));

            scaledCellSizes.Add((
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale))));
        }

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
                geometry.Orientation,
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

        return CollageExporter.ComposeCanvas(cells, canvasColor, scaledPositions, canvasW, canvasH);
    }

    private static NetVips.Image CreateEmptySlotCell(int width, int height)
    {
        return NetVips.Image.Black(width, height)
            .NewFromImage(new double[] { 48, 48, 48 })
            .Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
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
