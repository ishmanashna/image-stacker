namespace ImageStacker.Core.Imaging;

public static class ThumbChromaClassifier
{
    /// <summary>Pixels with chroma below this are treated as monochrome (gray / faded B&amp;W).</summary>
    public const int MonochromeChromaThreshold = 28;

    public static int GetPixelChroma(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        return max - min;
    }

    /// <summary>
    /// True when every RGB pixel has chroma strictly below <see cref="MonochromeChromaThreshold"/>.
    /// </summary>
    public static bool IsMonochromeRgb24(ReadOnlySpan<byte> pixels, int width, int height, int strideBytes = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        if (strideBytes <= 0)
        {
            strideBytes = width * 3;
        }

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * strideBytes;
            for (int x = 0; x < width; x++)
            {
                int i = rowStart + (x * 3);
                if (GetPixelChroma(pixels[i], pixels[i + 1], pixels[i + 2]) >= MonochromeChromaThreshold)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
