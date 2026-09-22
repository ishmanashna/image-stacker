using ImageStacker.Core;
using ImageStacker.Core.Io;

namespace ImageStacker.Core.Layout;

public sealed record CellRect(int X, int Y, int Width, int Height);

public sealed record LayoutGeometry(
    int NumImages,
    LayoutOrientation Orientation,
    int TargetWidth,
    int TargetHeight,
    IReadOnlyList<CellRect> Positions,
    IReadOnlyList<CellRect> CellSizes,
    int CanvasWidth,
    int CanvasHeight);

public static class LayoutGeometryCalculator
{
    /// <summary>How far neighboring cells grow into each other along a shared seam when bleed is on.</summary>
    public const int BleedOverlapPx = 48;

    public static LayoutGeometry Compute(
        string layoutName,
        bool borderless,
        bool bleed = false,
        IReadOnlyList<string>? paths = null)
    {
        var config = LayoutCatalog.GetRequired(layoutName);
        (int canvasW, int canvasH) = ResolveCanvasSize(layoutName, config, paths);
        int rows = config.Rows;
        int cols = config.Cols;

        int spacing;
        int margin;
        if (borderless)
        {
            spacing = 0;
            margin = 0;
        }
        else if (config.Framed)
        {
            spacing = 150;
            margin = 150;
        }
        else
        {
            spacing = 75;
            margin = 0;
        }

        int totalSpacingH = (spacing * (rows - 1)) + (margin * 2);
        int availableH = canvasH - totalSpacingH;
        int targetH = availableH / rows;
        int remH = availableH % rows;
        int totalSpacingW = (spacing * (cols - 1)) + (margin * 2);
        int availableW = canvasW - totalSpacingW;
        int targetW = availableW / cols;
        int remW = availableW % cols;

        var positions = new List<CellRect>(rows * cols);
        var cellSizes = new List<CellRect>(rows * cols);

        int y = margin;
        for (int r = 0; r < rows; r++)
        {
            int h = targetH + (r == rows - 1 ? remH : 0);
            int x = margin;
            for (int c = 0; c < cols; c++)
            {
                int tw = targetW + (c == cols - 1 ? remW : 0);
                positions.Add(new CellRect(x, y, tw, h));
                cellSizes.Add(new CellRect(0, 0, tw, h));
                x += tw + spacing;
            }

            y += h + spacing;
        }

        // Bleed softens seams: grow through the gutter so neighbors actually overlap.
        // Grow each internal edge by half the gutter plus half the overlap budget.
        bool useBleed = bleed && !borderless && spacing > 0 && BleedOverlapPx > 0 && (rows > 1 || cols > 1);
        if (useBleed)
        {
            int grow = (spacing / 2) + (BleedOverlapPx / 2);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = (r * cols) + c;
                    CellRect cell = positions[i];
                    int x0 = cell.X;
                    int y0 = cell.Y;
                    int x1 = cell.X + cell.Width;
                    int y1 = cell.Y + cell.Height;

                    if (c > 0)
                    {
                        x0 -= grow;
                    }

                    if (c < cols - 1)
                    {
                        x1 += grow;
                    }

                    if (r > 0)
                    {
                        y0 -= grow;
                    }

                    if (r < rows - 1)
                    {
                        y1 += grow;
                    }

                    x0 = Math.Max(0, x0);
                    y0 = Math.Max(0, y0);
                    x1 = Math.Min(canvasW, x1);
                    y1 = Math.Min(canvasH, y1);

                    int w = Math.Max(1, x1 - x0);
                    int hCell = Math.Max(1, y1 - y0);
                    positions[i] = new CellRect(x0, y0, w, hCell);
                    cellSizes[i] = new CellRect(0, 0, w, hCell);
                }
            }
        }

        return new LayoutGeometry(
            config.NumImages,
            config.Orientation,
            targetW,
            targetH,
            positions,
            cellSizes,
            canvasW,
            canvasH);
    }

    private static (int CanvasW, int CanvasH) ResolveCanvasSize(
        string layoutName,
        LayoutDefinition config,
        IReadOnlyList<string>? paths)
    {
        if (string.Equals(layoutName, "stack-1", StringComparison.OrdinalIgnoreCase))
        {
            if (paths is { Count: > 0 })
            {
                if (OrientationHelper.TryGetOrientedDimensions(paths[0], out int width, out int height)
                    || OrientationHelper.TryGetRawDimensions(paths[0], out width, out height))
                {
                    if (width > height)
                    {
                        return (Constants.LandscapeCanvasWidth, Constants.LandscapeCanvasHeight);
                    }

                    return (Constants.CanvasWidth, Constants.CanvasHeight);
                }
            }

            return (Constants.CanvasWidth, Constants.CanvasHeight);
        }

        int canvasW = config.LandscapeCanvas ? Constants.LandscapeCanvasWidth : Constants.CanvasWidth;
        int canvasH = config.LandscapeCanvas ? Constants.LandscapeCanvasHeight : Constants.CanvasHeight;
        return (canvasW, canvasH);
    }
}
