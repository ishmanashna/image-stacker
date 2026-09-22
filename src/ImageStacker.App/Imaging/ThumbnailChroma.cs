using System.Windows.Media.Imaging;
using ImageStacker.Core.Imaging;

namespace ImageStacker.App.Imaging;

internal static class ThumbnailChroma
{
    internal static bool? TryIsMonochrome(WriteableBitmap? bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int stride = width * 3;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        return ThumbChromaClassifier.IsMonochromeRgb24(pixels, width, height, stride);
    }
}
