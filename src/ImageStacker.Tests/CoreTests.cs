using ImageStacker.Core;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Io;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Tests;

public static class SyntheticImages
{
    public static string CreateLandscapeJpeg(string directory, int width = 4000, int height = 3000)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"landscape_{width}x{height}.jpg");
        using var image = Image.Black(width, height).NewFromImage(new double[] { 120, 80, 40 }).Cast(Enums.BandFormat.Uchar);
        image.Jpegsave(path, q: 90);
        return path;
    }

    public static string CreatePortraitJpeg(string directory, int width = 3000, int height = 4000)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"portrait_{width}x{height}.jpg");
        using var image = Image.Black(width, height).NewFromImage(new double[] { 40, 80, 120 }).Cast(Enums.BandFormat.Uchar);
        image.Jpegsave(path, q: 90);
        return path;
    }

    public static string CreatePortraitJpegWithHorizontalGradient(string directory, int width = 3000, int height = 4000)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"portrait_grad_{width}x{height}.jpg");
        int half = width / 2;
        using var left = Image.Black(half, height).NewFromImage(new double[] { 0, 0, 0 }).Cast(Enums.BandFormat.Uchar);
        using var right = Image.Black(width - half, height).NewFromImage(new double[] { 255, 255, 255 }).Cast(Enums.BandFormat.Uchar);
        using var rgb = left.Join(right, Enums.Direction.Horizontal);
        rgb.Jpegsave(path, q: 90);
        return path;
    }

    public static string CreatePngWithAlpha(string directory, int width = 800, int height = 600)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "alpha.png");
        using var rgb = Image.Black(width, height).NewFromImage(new double[] { 255, 0, 0 }).Cast(Enums.BandFormat.Uchar);
        using var alpha = Image.Black(width, height).NewFromImage(new double[] { 128 }).Cast(Enums.BandFormat.Uchar);
        using var rgba = rgb.Bandjoin(alpha);
        rgba.Pngsave(path);
        return path;
    }
}

public class PreviewTests
{
    [Fact]
    public void RenderPreviewOutputIsScaledBelowExportResolution()
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 3; i++)
            {
                File.Copy(SyntheticImages.CreateLandscapeJpeg(temp), Path.Combine(folder, $"img_{i:00}.jpg"));
            }

            var paths = Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(3).ToList();
            using var preview = LayoutContracts.RenderPreview(
                paths,
                "stack-3",
                borderless: false,
                color: "white",
                previewLongEdge: 1000);

            Assert.True(Math.Max(preview.Width, preview.Height) <= 1200);
            Assert.Equal(3, preview.Bands);
            Assert.True(preview.Width < Constants.CanvasWidth);
            Assert.True(preview.Height < Constants.CanvasHeight);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static string CreateTempDir() => Path.Combine(Path.GetTempPath(), "image-stacker-tests", Guid.NewGuid().ToString("N"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public class CanvasTests
{
    [Fact]
    public void ExportProduces3840x4800Canvas()
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 3; i++)
            {
                File.Copy(SyntheticImages.CreateLandscapeJpeg(temp), Path.Combine(folder, $"img_{i:00}.jpg"));
            }

            string output = Path.Combine(temp, "out.jpg");
            CollageExporter.ExportCollage(
                Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(3).ToList(),
                "stack-3",
                borderless: false,
                color: "white",
                output);

            using var result = Image.NewFromFile(output);
            Assert.Equal(Constants.CanvasWidth, result.Width);
            Assert.Equal(Constants.CanvasHeight, result.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void EncodedJpegIsAtMost8Mb()
    {
        string temp = CreateTempDir();
        try
        {
            using var canvas = Image.Black(Constants.CanvasWidth, Constants.CanvasHeight)
                .NewFromImage(new double[] { 200, 100, 50 })
                .Cast(Enums.BandFormat.Uchar);

            string output = Path.Combine(temp, "large.jpg");
            JpegEncoder.SaveOptimized(canvas, output);
            long size = new FileInfo(output).Length;
            Assert.True(size <= Constants.MaxOutputJpegBytes);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void SaveOptimizedLogsWarningWhenAllProbesExceedLimit()
    {
        var warnings = new List<string>();
        Action<string>? previous = CoreDiagnostics.WarningHandler;
        string temp = CreateTempDir();
        try
        {
            CoreDiagnostics.WarningHandler = warnings.Add;

            using var canvas = Image.Black(400, 400)
                .NewFromImage(new double[] { 200, 100, 50 })
                .Cast(Enums.BandFormat.Uchar);

            const int tinyLimit = 500;
            string output = Path.Combine(temp, "oversize.jpg");
            JpegEncoder.SaveOptimized(canvas, output, maxFileSize: tinyLimit);

            Assert.True(new FileInfo(output).Length > tinyLimit);
            Assert.Single(warnings);
            Assert.Contains("exceed", warnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains(output, warnings[0], StringComparison.Ordinal);
        }
        finally
        {
            CoreDiagnostics.WarningHandler = previous;
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void GenerateOutputFilenameIsUniqueUnderParallelCalls()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 64, i =>
        {
            names.Add(CollageExporter.GenerateOutputFilename(@"C:\out", "stack-3", i + 1));
        });

        Assert.Equal(64, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string CreateTempDir() => Path.Combine(Path.GetTempPath(), "image-stacker-tests", Guid.NewGuid().ToString("N"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test dirs.
        }
    }
}

public class LayoutGeometryTests
{
    public static IEnumerable<object[]> LayoutBorderBleedCases()
    {
        foreach (string layout in LayoutCatalog.Layouts.Keys.OrderBy(x => x))
        {
            yield return [layout, false, false];
            yield return [layout, true, false];
            yield return [layout, false, true];
        }
    }

    [Theory]
    [MemberData(nameof(LayoutBorderBleedCases))]
    public void CellRectsFitCanvas(string layout, bool borderless, bool bleed)
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute(layout, borderless, bleed);
        Assert.Equal(LayoutCatalog.GetRequired(layout).NumImages, geometry.Positions.Count);
        Assert.Equal(geometry.Positions.Count, geometry.CellSizes.Count);

        foreach (CellRect pos in geometry.Positions)
        {
            Assert.True(pos.X >= 0);
            Assert.True(pos.Y >= 0);
            Assert.True(pos.Width > 0);
            Assert.True(pos.Height > 0);
            Assert.True(pos.X + pos.Width <= Constants.CanvasWidth);
            Assert.True(pos.Y + pos.Height <= Constants.CanvasHeight);
        }
    }

    [Fact]
    public void BorderlessStack3TilesFillCanvasWithoutGaps()
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute("stack-3", borderless: true);
        Assert.Equal(3, geometry.Positions.Count);
        Assert.Equal(0, geometry.Positions[0].Y);
        Assert.Equal(geometry.Positions[0].Y + geometry.Positions[0].Height, geometry.Positions[1].Y);
        Assert.Equal(geometry.Positions[1].Y + geometry.Positions[1].Height, geometry.Positions[2].Y);
        Assert.Equal(Constants.CanvasHeight, geometry.Positions[2].Y + geometry.Positions[2].Height);
        Assert.All(geometry.Positions, p => Assert.Equal(Constants.CanvasWidth, p.Width));
    }

    [Fact]
    public void Stack4HasFourStackedCellsFillingHeight()
    {
        LayoutGeometry geometry = LayoutGeometryCalculator.Compute("stack-4", borderless: true);
        Assert.Equal(4, geometry.Positions.Count);
        Assert.Equal(LayoutOrientation.Horizontal, LayoutCatalog.GetRequired("stack-4").Orientation);
        Assert.Equal(0, geometry.Positions[0].Y);
        for (int i = 1; i < 4; i++)
        {
            Assert.Equal(
                geometry.Positions[i - 1].Y + geometry.Positions[i - 1].Height,
                geometry.Positions[i].Y);
        }

        Assert.Equal(Constants.CanvasHeight, geometry.Positions[3].Y + geometry.Positions[3].Height);
        Assert.All(geometry.Positions, p => Assert.Equal(Constants.CanvasWidth, p.Width));
    }

    [Fact]
    public void Grid2x2HIsFramedHorizontalFourCells()
    {
        var def = LayoutCatalog.GetRequired("grid-2x2-h");
        Assert.Equal(4, def.NumImages);
        Assert.True(def.Framed);
        Assert.Equal(LayoutOrientation.Horizontal, def.Orientation);

        LayoutGeometry framed = LayoutGeometryCalculator.Compute("grid-2x2-h", borderless: false);
        Assert.Equal(4, framed.Positions.Count);
        Assert.All(framed.Positions, p =>
        {
            Assert.True(p.X >= 150);
            Assert.True(p.Y >= 150);
            Assert.True(p.X + p.Width <= Constants.CanvasWidth - 150);
            Assert.True(p.Y + p.Height <= Constants.CanvasHeight - 150);
        });

        LayoutGeometry borderless = LayoutGeometryCalculator.Compute("grid-2x2-h", borderless: true);
        Assert.Contains(borderless.Positions, p => p.X == 0 || p.Y == 0);
    }

    [Fact]
    public void Row1x3SplitsIntoHonestHAndVLayouts()
    {
        Assert.False(LayoutCatalog.Layouts.ContainsKey("grid-1x3-m"));
        Assert.Equal(LayoutOrientation.Horizontal, LayoutCatalog.GetRequired("grid-1x3-h").Orientation);
        Assert.Equal(LayoutOrientation.Vertical, LayoutCatalog.GetRequired("grid-1x3-v").Orientation);
        Assert.Equal(3, LayoutGeometryCalculator.Compute("grid-1x3-h", borderless: false).Positions.Count);
        Assert.Equal(3, LayoutGeometryCalculator.Compute("grid-1x3-v", borderless: false).Positions.Count);
    }

    [Fact]
    public void UnknownLayoutThrows()
    {
        Assert.Throws<ArgumentException>(() => LayoutCatalog.GetRequired("nope"));
    }

    [Fact]
    public void Stack2BleedOverlapsVerticalSeam()
    {
        LayoutGeometry plain = LayoutGeometryCalculator.Compute("stack-2", borderless: false, bleed: false);
        LayoutGeometry bled = LayoutGeometryCalculator.Compute("stack-2", borderless: false, bleed: true);
        Assert.Equal(2, bled.Positions.Count);
        Assert.True(bled.Positions[0].Y + bled.Positions[0].Height > bled.Positions[1].Y);
        Assert.True(bled.Positions[0].Height > plain.Positions[0].Height);
        Assert.True(bled.Positions[1].Height > plain.Positions[1].Height);
    }

    [Fact]
    public void BleedPlusBorderlessEqualsBorderless()
    {
        LayoutGeometry borderless = LayoutGeometryCalculator.Compute("stack-2", borderless: true, bleed: false);
        LayoutGeometry both = LayoutGeometryCalculator.Compute("stack-2", borderless: true, bleed: true);
        Assert.Equal(borderless.Positions, both.Positions);
    }

    [Fact]
    public void GridBleedOverlapsInternalSeamsOnly()
    {
        LayoutGeometry bled = LayoutGeometryCalculator.Compute("grid-2x2-v", borderless: false, bleed: true);
        Assert.Equal(4, bled.Positions.Count);
        var a = bled.Positions[0];
        var b = bled.Positions[1];
        Assert.True(a.X + a.Width > b.X);
        Assert.True(a.X > 0);
        Assert.True(a.Y > 0);
    }
}

public class CoverMathTests
{
    [Theory]
    [InlineData(0, 0, 1000, 400, 100, 0)]
    [InlineData(-1, 0, 1000, 400, 0, 0)]
    [InlineData(1, 0, 1000, 400, 200, 0)]
    [InlineData(0, -1, 400, 400, 0, 0)]
    [InlineData(0, 1, 400, 400, 0, 200)]
    public void PanSelectsExpectedCropOrigin(double panX, double panY, int srcW, int srcH, int expectedLeft, int expectedTop)
    {
        (int left, int top, int cropW, int cropH) = CoverMath.ComputeCropRect(srcW, srcH, 200, 100, panX, panY);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
        Assert.True(cropW > 0);
        Assert.True(cropH > 0);
        Assert.True(left + cropW <= srcW);
        Assert.True(top + cropH <= srcH);
    }

    [Fact]
    public void PngWithAlphaFlattensToWhiteBackground()
    {
        string temp = CreateTempDir();
        try
        {
            string png = SyntheticImages.CreatePngWithAlpha(temp);
            using var loaded = Image.NewFromFile(png).Autorot();
            using var flattened = ImagePipeline.FlattenAlpha(loaded);
            Assert.False(flattened.HasAlpha());
            Assert.Equal(3, flattened.Bands);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void Grid1x2VUsesOppositeHalfPans()
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            string portrait = SyntheticImages.CreatePortraitJpegWithHorizontalGradient(temp);
            File.Copy(portrait, Path.Combine(folder, "a.jpg"));
            File.Copy(portrait, Path.Combine(folder, "b.jpg"));

            using var cache = new SourceImageCache();
            LayoutGeometry geometry = LayoutGeometryCalculator.Compute("grid-1x2-v", borderless: false);
            var paths = Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).ToList();

            int cellW = geometry.CellSizes[0].Width;
            int cellH = geometry.CellSizes[0].Height;
            using var source = cache.GetCopy(paths[0]);
            (int leftCrop, _, _, _) = CoverMath.ComputeCropRect(source.Width, source.Height, cellW, cellH, -1, 0);
            (int rightCrop, _, _, _) = CoverMath.ComputeCropRect(source.Width, source.Height, cellW, cellH, 1, 0);
            Assert.NotEqual(leftCrop, rightCrop);

            using var leftCell = ImagePipeline.ProcessImageForCell(
                cache, paths[0], cellW, cellH,
                -1, 0, false, false)!;
            using var rightCell = ImagePipeline.ProcessImageForCell(
                cache, paths[1], geometry.CellSizes[1].Width, geometry.CellSizes[1].Height,
                1, 0, false, false)!;

            Assert.NotNull(leftCell);
            Assert.NotNull(rightCell);
            Assert.False(CellsArePixelIdentical(leftCell, rightCell));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ProcessImageForCellAcceptsPortraitIntoHorizontalLayoutCell()
    {
        string temp = CreateTempDir();
        try
        {
            string portrait = SyntheticImages.CreatePortraitJpeg(temp);
            using var cache = new SourceImageCache();
            LayoutGeometry geometry = LayoutGeometryCalculator.Compute("stack-3", borderless: false);
            int cellW = geometry.CellSizes[0].Width;
            int cellH = geometry.CellSizes[0].Height;

            using var cell = ImagePipeline.ProcessImageForCell(
                cache, portrait, cellW, cellH, 0, 0, false, false);

            Assert.NotNull(cell);
            Assert.Equal(cellW, cell!.Width);
            Assert.Equal(cellH, cell.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void PreviewAndExportUseSamePanCropFractions()
    {
        string temp = CreateTempDir();
        try
        {
            string path = SyntheticImages.CreatePortraitJpeg(temp);
            LayoutGeometry geometry = LayoutGeometryCalculator.Compute("grid-1x2-v", borderless: false);
            int cellW = geometry.CellSizes[0].Width;
            int cellH = geometry.CellSizes[0].Height;

            using var exportSource = Image.NewFromFile(path).Autorot();
            using var previewSource = ImagePipeline.LoadForPreview(path, cellW, cellH);

            foreach (double pan in new[] { -1.0, 1.0 })
            {
                (double exportNormLeft, double exportNormTop) = CoverMath.NormalizedCropOrigin(
                    exportSource.Width, exportSource.Height, cellW, cellH, pan, 0);
                (double previewNormLeft, double previewNormTop) = CoverMath.NormalizedCropOrigin(
                    previewSource.Width, previewSource.Height, cellW, cellH, pan, 0);

                Assert.True(Math.Abs(exportNormLeft - previewNormLeft) < 0.01);
                Assert.True(Math.Abs(exportNormTop - previewNormTop) < 0.01);
            }
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static bool CellsArePixelIdentical(NetVips.Image a, NetVips.Image b)
    {
        if (a.Width != b.Width || a.Height != b.Height || a.Bands != b.Bands)
        {
            return false;
        }

        byte[] bytesA = a.WriteToBuffer(".png");
        byte[] bytesB = b.WriteToBuffer(".png");
        return bytesA.AsSpan().SequenceEqual(bytesB);
    }

    private static string CreateTempDir() => Path.Combine(Path.GetTempPath(), "image-stacker-tests", Guid.NewGuid().ToString("N"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public class ManualExportTests
{
    [Fact]
    public void GenerateManualOutputFilenameHasNoJobIndex()
    {
        string path = CollageExporter.GenerateManualOutputFilename(@"C:\out");
        string fileName = Path.GetFileName(path);
        Assert.StartsWith("manual_", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".jpg", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_000_", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderManualPreviewAllowsPartialSlots()
    {
        string temp = Path.Combine(Path.GetTempPath(), "image-stacker-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            string photo = SyntheticImages.CreateLandscapeJpeg(temp);
            File.Copy(photo, Path.Combine(folder, "a.jpg"));

            var slots = new SlotAssignment?[] { new SlotAssignment(Path.Combine(folder, "a.jpg")), null };
            using var preview = LayoutContracts.RenderManualPreview(
                "stack-2",
                borderless: false,
                color: "white",
                bleed: false,
                slots,
                previewLongEdge: 400);

            Assert.True(preview.Width > 0);
            Assert.True(preview.Height > 0);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}

internal static class CoverMath
{
    public static (int Left, int Top, int CropW, int CropH) ComputeCropRect(
        int srcW, int srcH, int targetW, int targetH, double panX, double panY)
    {
        panX = Math.Clamp(panX, -1.0, 1.0);
        panY = Math.Clamp(panY, -1.0, 1.0);

        double scale = Math.Max(targetW / (double)srcW, targetH / (double)srcH);
        double winW = targetW / scale;
        double winH = targetH / scale;
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
        return (left, top, cropW, cropH);
    }

    public static (double NormLeft, double NormTop) NormalizedCropOrigin(
        int srcW, int srcH, int targetW, int targetH, double panX, double panY)
    {
        (int left, int top, _, _) = ComputeCropRect(srcW, srcH, targetW, targetH, panX, panY);

        double scale = Math.Max(targetW / (double)srcW, targetH / (double)srcH);
        double winW = targetW / scale;
        double winH = targetH / scale;
        double excessW = srcW - winW;
        double excessH = srcH - winH;

        double normLeft = excessW > 0 ? left / excessW : 0;
        double normTop = excessH > 0 ? top / excessH : 0;
        return (normLeft, normTop);
    }
}

public class CropDefaultsTests
{
    [Fact]
    public void GoldilocksPanYBiasesUpForHorizontalLayouts()
    {
        Assert.True(CropDefaults.DefaultPanY("stack-4") < 0);
        Assert.True(CropDefaults.DefaultPanY("grid-2x2-h") < 0);
        Assert.Equal(0.0, CropDefaults.DefaultPanY("grid-2x2-v"));
        Assert.Equal(0.0, CropDefaults.DefaultPanY("grid-1x3-v"));
        Assert.Equal(-1.0, CropDefaults.DefaultPanX("grid-1x2-v", 0));
        Assert.Equal(1.0, CropDefaults.DefaultPanX("grid-1x2-v", 1));
    }
}
