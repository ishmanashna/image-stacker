namespace ImageStacker.Core.Layout;

public sealed record CellRect(int X, int Y, int Width, int Height);

public sealed record LayoutGeometry(
    int NumImages,
    LayoutOrientation Orientation,
    int TargetWidth,
    int TargetHeight,
    IReadOnlyList<CellRect> Positions,
    IReadOnlyList<CellRect> CellSizes);

public static class LayoutGeometryCalculator
{
    public static LayoutGeometry Compute(string layoutName, bool borderless, bool bleed = false)
    {
        var config = LayoutCatalog.GetRequired(layoutName);
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
        int targetH = (Constants.CanvasHeight - totalSpacingH) / rows;
        int totalSpacingW = (spacing * (cols - 1)) + (margin * 2);
        int targetWNormal = (Constants.CanvasWidth - totalSpacingW) / cols;

        bool useBleed = bleed && !borderless && rows >= 1 && cols >= 1;
        int targetWBleedRow = useBleed ? Constants.CanvasWidth / cols : targetWNormal;

        var positions = new List<CellRect>();
        var cellSizes = new List<CellRect>();

        for (int r = 0; r < rows; r++)
        {
            if (useBleed && r == 0)
            {
                int tw = targetWBleedRow;
                for (int c = 0; c < cols; c++)
                {
                    positions.Add(new CellRect(c * tw, margin + r * (targetH + spacing), tw, targetH));
                    cellSizes.Add(new CellRect(0, 0, tw, targetH));
                }
            }
            else
            {
                for (int c = 0; c < cols; c++)
                {
                    int x = margin + c * (targetWNormal + spacing);
                    int y = margin + r * (targetH + spacing);
                    positions.Add(new CellRect(x, y, targetWNormal, targetH));
                    cellSizes.Add(new CellRect(0, 0, targetWNormal, targetH));
                }
            }
        }

        return new LayoutGeometry(
            config.NumImages,
            config.Orientation,
            targetWNormal,
            targetH,
            positions,
            cellSizes);
    }
}
