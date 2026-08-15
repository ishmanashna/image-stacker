namespace ImageStacker.Core;

public static class Constants
{
    public const int CanvasWidth = 3840;
    public const int CanvasHeight = 4800;
    public const int MaxOutputJpegBytes = 8 * 1024 * 1024;

    public static readonly int[] JpegQualityProbes = [92, 80, 68, 65];

    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
    };
}
