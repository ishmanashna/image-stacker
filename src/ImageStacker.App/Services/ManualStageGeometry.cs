using ImageStacker.Core;
using ImageStacker.Core.Layout;

namespace ImageStacker.App.Services;

internal static class ManualStageGeometry
{
    public sealed record StageMetrics(double Scale, double OffsetX, double OffsetY, double DisplayWidth, double DisplayHeight);

    public static StageMetrics ComputeMetrics(double stageWidth, double stageHeight)
    {
        double sw = Math.Max(stageWidth, 50);
        double sh = Math.Max(stageHeight, 50);
        double scale = Math.Min(sw / Constants.CanvasWidth, sh / Constants.CanvasHeight);
        double cw = Constants.CanvasWidth * scale;
        double ch = Constants.CanvasHeight * scale;
        double ox = (sw - cw) / 2.0;
        double oy = (sh - ch) / 2.0;
        return new StageMetrics(scale, ox, oy, cw, ch);
    }

    public static IReadOnlyList<(int X0, int Y0, int X1, int Y1)> SlotPixelRects(
        StageMetrics metrics,
        LayoutGeometry geometry)
    {
        var rects = new List<(int, int, int, int)>(geometry.NumImages);
        for (int i = 0; i < geometry.NumImages; i++)
        {
            CellRect pos = geometry.Positions[i];
            CellRect size = geometry.CellSizes[i];
            int x0 = (int)Math.Floor(metrics.OffsetX + pos.X * metrics.Scale);
            int y0 = (int)Math.Floor(metrics.OffsetY + pos.Y * metrics.Scale);
            int x1 = (int)Math.Ceiling(metrics.OffsetX + (pos.X + size.Width) * metrics.Scale);
            int y1 = (int)Math.Ceiling(metrics.OffsetY + (pos.Y + size.Height) * metrics.Scale);
            rects.Add((x0, y0, Math.Max(x0 + 1, x1), Math.Max(y0 + 1, y1)));
        }

        return rects;
    }

    public static int? HitTestSlot(double px, double py, StageMetrics metrics, LayoutGeometry geometry)
    {
        IReadOnlyList<(int X0, int Y0, int X1, int Y1)> rects = SlotPixelRects(metrics, geometry);
        for (int i = rects.Count - 1; i >= 0; i--)
        {
            (int x0, int y0, int x1, int y1) = rects[i];
            if (px >= x0 && px < x1 && py >= y0 && py < y1)
            {
                return i;
            }
        }

        return null;
    }

    public static (double SensX, double SensY)? PanSensitivity(int slot, StageMetrics metrics, LayoutGeometry geometry)
    {
        IReadOnlyList<(int X0, int Y0, int X1, int Y1)> rects = SlotPixelRects(metrics, geometry);
        if (slot < 0 || slot >= rects.Count)
        {
            return null;
        }

        (int x0, int y0, int x1, int y1) = rects[slot];
        int tws = Math.Max(1, x1 - x0);
        int ths = Math.Max(1, y1 - y0);
        return (2.0 / tws, 2.0 / ths);
    }
}
