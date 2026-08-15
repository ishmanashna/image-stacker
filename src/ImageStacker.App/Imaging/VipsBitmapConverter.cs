using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetVips;

namespace ImageStacker.App.Imaging;

internal readonly record struct BitmapBuffer(int Width, int Height, byte[] Pixels);

internal static class VipsBitmapConverter
{
    public static BitmapBuffer CreateThumbTileBuffer(string path, int tileWidth, int tileHeight)
    {
        using Image tileImage = BuildThumbTileImage(path, tileWidth, tileHeight);
        return ToBuffer(tileImage);
    }

    public static WriteableBitmap BufferToWriteableBitmap(BitmapBuffer buffer)
    {
        var bitmap = new WriteableBitmap(
            buffer.Width,
            buffer.Height,
            96,
            96,
            PixelFormats.Rgb24,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, buffer.Width, buffer.Height),
            buffer.Pixels,
            buffer.Width * 3,
            0);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapBuffer ImageToBuffer(Image image) => ToBuffer(image);

    private static BitmapBuffer ToBuffer(Image image)
    {
        using Image rgb = image.Colourspace(Enums.Interpretation.Srgb);
        using Image flattened = rgb.Bands >= 4 ? rgb.Flatten() : rgb.Copy();
        int width = flattened.Width;
        int height = flattened.Height;
        byte[] bytes = flattened.WriteToMemory();

        return new BitmapBuffer(width, height, bytes);
    }

    private static Image BuildThumbTileImage(string path, int tileWidth, int tileHeight)
    {
        using Image background = NetVips.Image.Black(tileWidth, tileHeight)
            .NewFromImage(new double[] { 43, 43, 43 })
            .Cast(Enums.BandFormat.Uchar);

        using Image loaded = Image.Thumbnail(path, tileHeight, size: Enums.Size.Down);
        using Image oriented = loaded.Autorot();

        double scale = Math.Min(
            tileWidth / (double)oriented.Width,
            tileHeight / (double)oriented.Height);
        int newWidth = Math.Max(1, (int)Math.Round(oriented.Width * scale));
        int newHeight = Math.Max(1, (int)Math.Round(oriented.Height * scale));

        using Image resized = oriented.Resize(
            newWidth / (double)oriented.Width,
            kernel: Enums.Kernel.Lanczos3);

        int left = (tileWidth - newWidth) / 2;
        int top = (tileHeight - newHeight) / 2;

        return background.Composite2(resized, Enums.BlendMode.Over, left, top);
    }
}
