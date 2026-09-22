using NetVips;

namespace ImageStacker.Core.Imaging;

public static class ImagePipeline
{
    public static NetVips.Image LoadForCell(SourceImageCache cache, string path, int targetWidth, int targetHeight)
    {
        // Full (cached) decode for export/cover so pan has real source excess.
        // Do not Thumbnail to the cell size first — that center-covers and makes pan a no-op.
        // Copy so callers may dispose without invalidating the shared cache entry.
        _ = targetWidth;
        _ = targetHeight;
        return cache.GetCopy(path);
    }

    public static NetVips.Image FlattenAlpha(NetVips.Image image)
    {
        using var srgb = image.Colourspace(Enums.Interpretation.Srgb);
        if (!srgb.HasAlpha())
        {
            return EnsureRgb(srgb);
        }

        using var white = NetVips.Image.Black(srgb.Width, srgb.Height)
            .NewFromImage(new double[] { 255, 255, 255 })
            .Cast(Enums.BandFormat.Uchar);
        using var flattened = white.Composite2(srgb, Enums.BlendMode.Over);
        return EnsureRgb(flattened);
    }

    public static NetVips.Image EnsureRgb(NetVips.Image image)
    {
        using var srgb = image.Colourspace(Enums.Interpretation.Srgb);
        if (srgb.Bands == 3)
        {
            return srgb.Copy();
        }

        if (srgb.Bands > 3)
        {
            return srgb.ExtractBand(0, n: 3).Copy();
        }

        if (srgb.Bands == 1)
        {
            return srgb.Bandjoin([srgb, srgb]).Copy();
        }

        return srgb.Copy();
    }

    /// <summary>
    /// Renders RGB pixels once into a random-access memory image. Needed before pan, blur, or a second read of a sequential JPEG.
    /// </summary>
    public static NetVips.Image MaterializeRgb(NetVips.Image image)
    {
        using var rgb = EnsureRgb(image);
        byte[] pixels = rgb.WriteToMemory();
        using var wrapped = NetVips.Image.NewFromMemory(
            pixels,
            rgb.Width,
            rgb.Height,
            rgb.Bands,
            rgb.Format);
        return wrapped.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    public static NetVips.Image CoverResizePanned(
        NetVips.Image source,
        int targetWidth,
        int targetHeight,
        double panX,
        double panY)
    {
        panX = Math.Clamp(panX, -1.0, 1.0);
        panY = Math.Clamp(panY, -1.0, 1.0);

        using var flattened = FlattenAlpha(source);
        int srcW = flattened.Width;
        int srcH = flattened.Height;
        if (srcW <= 0 || srcH <= 0)
        {
            throw new InvalidOperationException("Source image has invalid dimensions.");
        }

        double scale = Math.Max(targetWidth / (double)srcW, targetHeight / (double)srcH);
        double winW = targetWidth / scale;
        double winH = targetHeight / scale;

        double excessW = srcW - winW;
        double excessH = srcH - winH;

        int left = excessW > 0 ? (int)Math.Round((1.0 + panX) * excessW / 2.0) : 0;
        int top = excessH > 0 ? (int)Math.Round((1.0 + panY) * excessH / 2.0) : 0;

        int maxLeft = (int)Math.Max(0, Math.Floor(excessW));
        int maxTop = (int)Math.Max(0, Math.Floor(excessH));
        left = Math.Clamp(left, 0, maxLeft);
        top = Math.Clamp(top, 0, maxTop);

        int cropW = Math.Max(1, (int)Math.Round(winW));
        int cropH = Math.Max(1, (int)Math.Round(winH));
        cropW = Math.Min(cropW, srcW - left);
        cropH = Math.Min(cropH, srcH - top);

        using var cropped = flattened.ExtractArea(left, top, cropW, cropH);
        double resizeScale = targetWidth / (double)cropW;
        using var resized = cropped.Resize(resizeScale, kernel: Enums.Kernel.Lanczos3);
        return ForceExactSize(resized, targetWidth, targetHeight);
    }

    /// <summary>
    /// Lanczos resize can land 1px off the target; crop/embed so paste never leaves a canvas hairline.
    /// Always returns a new image; does not take ownership of <paramref name="image"/>.
    /// </summary>
    private static NetVips.Image ForceExactSize(NetVips.Image image, int targetWidth, int targetHeight)
    {
        if (image.Width == targetWidth && image.Height == targetHeight)
        {
            return image.Copy();
        }

        int cropW = Math.Min(image.Width, targetWidth);
        int cropH = Math.Min(image.Height, targetHeight);
        if (image.Width >= targetWidth && image.Height >= targetHeight)
        {
            return image.Crop(0, 0, targetWidth, targetHeight).Copy();
        }

        using var cropped = (cropW != image.Width || cropH != image.Height)
            ? image.Crop(0, 0, cropW, cropH)
            : null;
        NetVips.Image src = cropped ?? image;
        using var embedded = src.Embed(0, 0, targetWidth, targetHeight, extend: Enums.Extend.Copy);
        return embedded.Copy();
    }

    public static NetVips.Image ApplyTransforms(NetVips.Image image, bool flipH, bool grayscale)
    {
        NetVips.Image current = image.Copy();
        if (flipH)
        {
            using var flipped = current.Flip(Enums.Direction.Horizontal);
            current.Dispose();
            current = flipped.Copy();
        }

        if (grayscale)
        {
            using var gray = current.Colourspace(Enums.Interpretation.Bw);
            current.Dispose();
            current = gray.Bandjoin([gray, gray]).Copy();
        }

        return EnsureRgb(current);
    }

    public static NetVips.Image? ProcessImageForCell(
        SourceImageCache cache,
        string path,
        int targetWidth,
        int targetHeight,
        double panX,
        double panY,
        bool flipH,
        bool grayscale)
    {
        using var loaded = LoadForCell(cache, path, targetWidth, targetHeight);
        using var covered = CoverResizePanned(loaded, targetWidth, targetHeight, panX, panY);
        return ApplyTransforms(covered, flipH, grayscale);
    }

    public static NetVips.Image LoadForPreview(string path, int targetWidth, int targetHeight)
    {
        // Shrink-on-load for stage preview — large enough to preserve pan headroom, far below export decode.
        int thumbSize = Math.Clamp(Math.Max(targetWidth, targetHeight) * 3, 256, 1600);
        return NetVips.Image.Thumbnail(path, thumbSize, size: Enums.Size.Down).Autorot();
    }

    public static NetVips.Image? ProcessImageForPreviewCell(
        string path,
        int targetWidth,
        int targetHeight,
        double panX,
        double panY,
        bool flipH,
        bool grayscale)
    {
        using var loaded = LoadForPreview(path, targetWidth, targetHeight);
        using var decoded = MaterializeRgb(loaded);
        using var covered = CoverResizePanned(decoded, targetWidth, targetHeight, panX, panY);
        return ApplyTransforms(covered, flipH, grayscale);
    }
}
