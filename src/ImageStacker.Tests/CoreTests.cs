using ImageStacker.Core;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Io;
using ImageStacker.Core.Jobs;
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
            LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-3", borderless: false);
            Assert.True(preview.Width < geom.CanvasWidth);
            Assert.True(preview.Height < geom.CanvasHeight);
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
        AssertPortraitExportDimensions("stack-3", 3);
    }

    [Fact]
    public void Stack1PortraitPhotoUses3840x4800Canvas()
    {
        string temp = CreateTempDir();
        try
        {
            string portrait = SyntheticImages.CreatePortraitJpeg(temp);
            var paths = new List<string> { portrait };
            LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-1", borderless: false, paths: paths);
            Assert.Equal(Constants.CanvasWidth, geom.CanvasWidth);
            Assert.Equal(Constants.CanvasHeight, geom.CanvasHeight);

            string output = Path.Combine(temp, "out.jpg");
            CollageExporter.ExportCollage(paths, "stack-1", borderless: false, color: "white", output);

            using var result = Image.NewFromFile(output);
            Assert.Equal(3840, result.Width);
            Assert.Equal(4800, result.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void Stack1LandscapePhotoUses4800x3200Canvas()
    {
        string temp = CreateTempDir();
        try
        {
            string landscape = SyntheticImages.CreateLandscapeJpeg(temp);
            var paths = new List<string> { landscape };
            LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-1", borderless: false, paths: paths);
            Assert.Equal(Constants.LandscapeCanvasWidth, geom.CanvasWidth);
            Assert.Equal(Constants.LandscapeCanvasHeight, geom.CanvasHeight);

            string output = Path.Combine(temp, "out.jpg");
            CollageExporter.ExportCollage(paths, "stack-1", borderless: false, color: "white", output);

            using var result = Image.NewFromFile(output);
            Assert.Equal(Constants.LandscapeCanvasWidth, result.Width);
            Assert.Equal(Constants.LandscapeCanvasHeight, result.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void Stack1BatchWritesOneFilePerPhoto()
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            File.Copy(SyntheticImages.CreatePortraitJpeg(temp), Path.Combine(folder, "a.jpg"));
            File.Copy(SyntheticImages.CreateLandscapeJpeg(temp), Path.Combine(folder, "b.jpg"));
            File.Copy(SyntheticImages.CreatePortraitJpeg(temp), Path.Combine(folder, "c.jpg"));

            IReadOnlyList<string> validPaths = ImageScanner.GetValidPaths(folder, LayoutOrientation.Mixed);
            IReadOnlyList<IReadOnlyList<string>> jobs = LayoutContracts.ListLayoutCandidatesFromPaths(
                validPaths,
                "stack-1",
                count: 1,
                batch: true,
                random: false,
                borderless: false);

            Assert.Equal(3, jobs.Count);
            string outDir = Path.Combine(temp, "out");
            Directory.CreateDirectory(outDir);
            for (int i = 0; i < jobs.Count; i++)
            {
                string output = Path.Combine(outDir, $"job_{i:00}.jpg");
                CollageExporter.ExportCollage(jobs[i], "stack-1", borderless: false, color: "white", output);
                using var result = Image.NewFromFile(output);
                LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-1", borderless: false, paths: jobs[i]);
                Assert.Equal(geom.CanvasWidth, result.Width);
                Assert.Equal(geom.CanvasHeight, result.Height);
            }
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ExportGrid2x2HProduces4800x3200Canvas()
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < 4; i++)
            {
                File.Copy(SyntheticImages.CreateLandscapeJpeg(temp), Path.Combine(folder, $"img_{i:00}.jpg"));
            }

            var paths = Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(4).ToList();
            LayoutGeometry geom = LayoutGeometryCalculator.Compute("grid-2x2-h", borderless: false);
            Assert.Equal(Constants.LandscapeCanvasWidth, geom.CanvasWidth);
            Assert.Equal(Constants.LandscapeCanvasHeight, geom.CanvasHeight);

            string output = Path.Combine(temp, "out.jpg");
            CollageExporter.ExportCollage(paths, "grid-2x2-h", borderless: false, color: "white", output);

            using var result = Image.NewFromFile(output);
            Assert.Equal(geom.CanvasWidth, result.Width);
            Assert.Equal(geom.CanvasHeight, result.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static void AssertPortraitExportDimensions(string layout, int imageCount)
    {
        string temp = CreateTempDir();
        try
        {
            string folder = Path.Combine(temp, "photos");
            Directory.CreateDirectory(folder);
            for (int i = 0; i < imageCount; i++)
            {
                File.Copy(SyntheticImages.CreateLandscapeJpeg(temp), Path.Combine(folder, $"img_{i:00}.jpg"));
            }

            var paths = Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(imageCount).ToList();
            LayoutGeometry geom = LayoutGeometryCalculator.Compute(layout, borderless: false);
            Assert.Equal(Constants.CanvasWidth, geom.CanvasWidth);
            Assert.Equal(Constants.CanvasHeight, geom.CanvasHeight);

            string output = Path.Combine(temp, "out.jpg");
            CollageExporter.ExportCollage(paths, layout, borderless: false, color: "white", output);

            using var result = Image.NewFromFile(output);
            Assert.Equal(geom.CanvasWidth, result.Width);
            Assert.Equal(geom.CanvasHeight, result.Height);
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
            Assert.True(pos.X + pos.Width <= geometry.CanvasWidth);
            Assert.True(pos.Y + pos.Height <= geometry.CanvasHeight);
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
        Assert.Equal(geometry.CanvasHeight, geometry.Positions[2].Y + geometry.Positions[2].Height);
        Assert.All(geometry.Positions, p => Assert.Equal(geometry.CanvasWidth, p.Width));
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

        Assert.Equal(geometry.CanvasHeight, geometry.Positions[3].Y + geometry.Positions[3].Height);
        Assert.All(geometry.Positions, p => Assert.Equal(geometry.CanvasWidth, p.Width));
    }

    [Fact]
    public void Grid2x2HIsFramedHorizontalFourCells()
    {
        var def = LayoutCatalog.GetRequired("grid-2x2-h");
        Assert.Equal(4, def.NumImages);
        Assert.True(def.Framed);
        Assert.Equal(LayoutOrientation.Horizontal, def.Orientation);

        LayoutGeometry framed = LayoutGeometryCalculator.Compute("grid-2x2-h", borderless: false);
        Assert.Equal(Constants.LandscapeCanvasWidth, framed.CanvasWidth);
        Assert.Equal(Constants.LandscapeCanvasHeight, framed.CanvasHeight);
        Assert.Equal(4, framed.Positions.Count);
        Assert.All(framed.Positions, p =>
        {
            Assert.True(p.X >= 150);
            Assert.True(p.Y >= 150);
            Assert.True(p.X + p.Width <= framed.CanvasWidth - 150);
            Assert.True(p.Y + p.Height <= framed.CanvasHeight - 150);
        });

        LayoutGeometry borderless = LayoutGeometryCalculator.Compute("grid-2x2-h", borderless: true);
        Assert.Contains(borderless.Positions, p => p.X == 0 || p.Y == 0);
    }

    [Fact]
    public void Row1x3CatalogHasVerticalOnly()
    {
        Assert.False(LayoutCatalog.Layouts.ContainsKey("grid-1x3-m"));
        Assert.False(LayoutCatalog.Layouts.ContainsKey("grid-1x3-h"));
        Assert.Equal(LayoutOrientation.Vertical, LayoutCatalog.GetRequired("grid-1x3-v").Orientation);
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
    public void Stack1Landscape32PhotoCoverUsesFullSourceWidth()
    {
        const int srcW = 4500;
        const int srcH = 3000;
        string temp = CreateTempDir();
        try
        {
            string photo = SyntheticImages.CreateLandscapeJpeg(temp, srcW, srcH);
            LayoutGeometry geom = LayoutGeometryCalculator.Compute(
                "stack-1",
                borderless: false,
                paths: new List<string> { photo });
            int cellW = geom.CellSizes[0].Width;
            int cellH = geom.CellSizes[0].Height;

            (_, _, int cropW, int cropH) = CoverMath.ComputeCropRect(srcW, srcH, cellW, cellH, 0, 0);
            Assert.Equal(srcW, cropW);
            Assert.Equal(srcH, cropH);

            using var cache = new SourceImageCache();
            using var cell = ImagePipeline.ProcessImageForCell(
                cache, photo, cellW, cellH, 0, 0, false, false);
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

public class LayoutCardCopyTests
{
    private const char Times = '\u00D7';

    [Fact]
    public void FormatAxB_gridParsesFromId_stacksUseCatalogRowsCols()
    {
        LayoutDefinition grid24 = LayoutCatalog.GetRequired("grid-2x4");
        Assert.Equal(4, grid24.Rows);
        Assert.Equal(2, grid24.Cols);
        Assert.Equal($"2{Times}4", LayoutCardCopy.FormatAxB("grid-2x4", grid24));

        LayoutDefinition stack3 = LayoutCatalog.GetRequired("stack-3");
        Assert.Equal($"3{Times}1", LayoutCardCopy.FormatAxB("stack-3", stack3));

        LayoutDefinition stack1 = LayoutCatalog.GetRequired("stack-1");
        Assert.Equal($"1{Times}1", LayoutCardCopy.FormatAxB("stack-1", stack1));
    }

    [Fact]
    public void Stack1CardShowsOutPhoto()
    {
        LayoutDefinition stack1 = LayoutCatalog.GetRequired("stack-1");
        string card = LayoutCardCopy.BuildCardText("stack-1", stack1);
        string[] lines = card.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Stack 1", lines[0]);
        Assert.Equal("in any → out photo", lines[1]);
    }

    [Fact]
    public void Stack3CardIsTwoLinesWithInOut()
    {
        LayoutDefinition stack3 = LayoutCatalog.GetRequired("stack-3");
        string card = LayoutCardCopy.BuildCardText("stack-3", stack3);
        string[] lines = card.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Stack 3", lines[0]);
        Assert.Equal("in H → out V", lines[1]);
    }

    [Fact]
    public void Grid2x4CardHasNoStandaloneAxBLine()
    {
        LayoutDefinition grid24 = LayoutCatalog.GetRequired("grid-2x4");
        string card = LayoutCardCopy.BuildCardText("grid-2x4", grid24);
        string[] lines = card.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal($"Grid 2{Times}4", lines[0]);
        Assert.DoesNotContain($"2{Times}4", lines[1]);
        Assert.False(string.Equals(lines[1], $"2{Times}4", StringComparison.Ordinal));
    }

    [Fact]
    public void LandscapeCanvas_trueOnlyOnGrid2x2H()
    {
        Assert.True(LayoutCatalog.GetRequired("grid-2x2-h").LandscapeCanvas);
        Assert.False(LayoutCatalog.GetRequired("grid-2x4").LandscapeCanvas);
        Assert.False(LayoutCatalog.GetRequired("grid-2x2-v").LandscapeCanvas);
        Assert.False(LayoutCatalog.GetRequired("stack-1").LandscapeCanvas);
    }

    [Fact]
    public void Stack1IsNotInComboJobSpecs()
    {
        Assert.DoesNotContain(
            ComboJobSpecs.All,
            spec => string.Equals(spec.LayoutName, "stack-1", StringComparison.OrdinalIgnoreCase));
    }
}

public class ThumbChromaClassifierTests
{
    private static byte[] SolidRgb24(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }

        return pixels;
    }

    [Fact]
    public void IsMonochromeRgb24_distinguishes_gray_and_red_at_64px()
    {
        const int size = 64;
        byte[] gray = SolidRgb24(size, size, 128, 128, 128);
        byte[] red = SolidRgb24(size, size, 255, 0, 0);
        byte[] fadedGray = SolidRgb24(size, size, 120, 128, 136);

        Assert.True(ThumbChromaClassifier.IsMonochromeRgb24(gray, size, size));
        Assert.False(ThumbChromaClassifier.IsMonochromeRgb24(red, size, size));
        Assert.True(ThumbChromaClassifier.IsMonochromeRgb24(fadedGray, size, size));
        Assert.Equal(0, ThumbChromaClassifier.GetPixelChroma(128, 128, 128));
        Assert.Equal(255, ThumbChromaClassifier.GetPixelChroma(255, 0, 0));
    }
}

public class CellPhotoEffectsTests
{
    [Fact]
    public void NoiseChangesPixelsComparedToDryExport()
    {
        var paths = CreateStack3Paths();
        using var dry = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: false, orton: false);
        using var withNoise = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: true, orton: false);

        Assert.Equal(dry.Width, withNoise.Width);
        Assert.Equal(dry.Height, withNoise.Height);
        LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-3", borderless: false);
        CellRect cell = geom.Positions[0];
        Assert.True(MeanAbsoluteDifferenceInRect(dry, withNoise, cell.X, cell.Y, cell.X + cell.Width, cell.Y + cell.Height) > 0.1);
    }

    [Fact]
    public void OrtonChangesPixelsComparedToDryExport()
    {
        var paths = CreateStack3Paths(useHighlightSource: true);
        using var dry = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: false, orton: false);
        using var withOrton = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: false, orton: true);

        Assert.Equal(dry.Width, withOrton.Width);
        Assert.Equal(dry.Height, withOrton.Height);
        LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-3", borderless: false);
        CellRect cell = geom.Positions[0];
        Assert.True(MeanAbsoluteDifferenceInRect(dry, withOrton, cell.X, cell.Y, cell.X + cell.Width, cell.Y + cell.Height) > 0.1);
    }

    [Fact]
    public void EffectsOffExportKeepsGeometryCanvasJpegDimensions()
    {
        string temp = CreateTempDir();
        try
        {
            var paths = CreateStack3Paths(temp);
            LayoutGeometry geom = LayoutGeometryCalculator.Compute("stack-3", borderless: false);
            string output = Path.Combine(temp, "dry.jpg");
            CollageExporter.ExportCollage(
                paths,
                "stack-3",
                borderless: false,
                color: "white",
                output,
                noise: false,
                orton: false);

            using var result = Image.NewFromFile(output);
            Assert.Equal(geom.CanvasWidth, result.Width);
            Assert.Equal(geom.CanvasHeight, result.Height);
            Assert.Equal(Constants.CanvasWidth, result.Width);
            Assert.Equal(Constants.CanvasHeight, result.Height);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ApplyToComposedPreview_ProtectColorLeavesWhiteUnchangedAndAltersGray()
    {
        const int w = 64;
        using var whiteBand = Image.Black(w, 32).NewFromImage(new double[] { 255, 255, 255 }).Cast(Enums.BandFormat.Uchar);
        using var grayBand = Image.Black(w, 32).NewFromImage(new double[] { 128, 128, 128 }).Cast(Enums.BandFormat.Uchar);
        using var composed = whiteBand.Join(grayBand, Enums.Direction.Vertical).Copy(interpretation: Enums.Interpretation.Srgb);

        var settings = CellEffectSettings.FromFlags(noise: true, orton: false);
        settings = settings with { NoiseSeed = 42 };
        using var fx = CellPhotoEffects.ApplyToComposedPreview(composed, settings, (255, 255, 255));

        Assert.Equal((255, 255, 255), GetRgbPixel(fx, 0, 0));
        Assert.NotEqual((128, 128, 128), GetRgbPixel(fx, 0, 40));
    }

    [Fact]
    public void FramedGutterCornerUnchangedWhenEffectsOn()
    {
        var paths = CreateGrid2x2VPaths();
        using var dry = CollageExporter.BuildCollageImage(
            paths, "grid-2x2-v", borderless: false, color: "white", noise: false, orton: false);
        using var fx = CollageExporter.BuildCollageImage(
            paths, "grid-2x2-v", borderless: false, color: "white", noise: true, orton: true);

        Assert.Equal((255, 255, 255), GetRgbPixel(dry, 0, 0));
        Assert.Equal((255, 255, 255), GetRgbPixel(fx, 0, 0));
        LayoutGeometry geom = LayoutGeometryCalculator.Compute("grid-2x2-v", borderless: false);
        CellRect cell = geom.Positions[0];
        Assert.True(MeanAbsoluteDifferenceInRect(dry, fx, cell.X, cell.Y, cell.X + cell.Width, cell.Y + cell.Height) > 0.1);
    }

    [Fact]
    public void BothEffectsOffMatchesDryExport()
    {
        var paths = CreateStack3Paths();
        using var dry = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: false, orton: false);
        using var again = CollageExporter.BuildCollageImage(
            paths, "stack-3", borderless: false, color: "white", noise: false, orton: false);

        Assert.True(MeanAbsoluteDifference(dry, again) < 0.01);
    }

    [Fact]
    public void OrtonLeavesNearBlackPatchMeanNearlyUnchanged()
    {
        using var dry = CreateOrtonTestCell();
        using var withOrton = CellPhotoEffects.ApplyToCell(
            dry,
            new CellEffectSettings(Noise: false, Orton: true));

        double dryMean = MeanRgbInRect(dry, 40, 40, 120, 120);
        double ortonMean = MeanRgbInRect(withOrton, 40, 40, 120, 120);
        Assert.True(Math.Abs(ortonMean - dryMean) <= 2.5);
    }

    [Fact]
    public void OrtonAddsGlowToBrightPatch()
    {
        using var dry = CreateOrtonTestCell();
        using var withOrton = CellPhotoEffects.ApplyToCell(
            dry,
            new CellEffectSettings(Noise: false, Orton: true));

        double brightMad = MeanAbsoluteDifferenceInRect(dry, withOrton, 140, 40, 220, 120);
        Assert.True(brightMad > 0.05);
    }

    [Fact]
    public void NoiseShadowsWeightChangesDarkMoreThanHighlights()
    {
        using var dry = CreateNoiseToneTestCell();
        var shadowsOnly = new CellEffectSettings(
            Noise: true,
            Orton: false,
            NoiseAmount: 0.25,
            NoiseShadows: 1.0,
            NoiseHighlights: 0.0,
            NoiseSeed: 42);
        var highlightsOnly = shadowsOnly with { NoiseShadows = 0.0, NoiseHighlights = 1.0 };

        using var shadowFx = CellPhotoEffects.ApplyToCell(dry, shadowsOnly);
        using var highlightFx = CellPhotoEffects.ApplyToCell(dry, highlightsOnly);

        double darkShadowMad = MeanAbsoluteDifferenceInRect(dry, shadowFx, 20, 80, 100, 160);
        double darkHighlightMad = MeanAbsoluteDifferenceInRect(dry, highlightFx, 20, 80, 100, 160);
        double lightShadowMad = MeanAbsoluteDifferenceInRect(dry, shadowFx, 140, 80, 220, 160);
        double lightHighlightMad = MeanAbsoluteDifferenceInRect(dry, highlightFx, 140, 80, 220, 160);

        Assert.True(darkShadowMad > lightShadowMad);
        Assert.True(lightHighlightMad > darkHighlightMad);
    }

    [Fact]
    public void NoiseBothShadowsAndHighlightsChangeBothTones()
    {
        using var dry = CreateNoiseToneTestCell();
        var both = new CellEffectSettings(
            Noise: true,
            Orton: false,
            NoiseAmount: 0.25,
            NoiseShadows: 1.0,
            NoiseHighlights: 1.0,
            NoiseSeed: 7);

        using var fx = CellPhotoEffects.ApplyToCell(dry, both);
        double darkMad = MeanAbsoluteDifferenceInRect(dry, fx, 20, 80, 100, 160);
        double lightMad = MeanAbsoluteDifferenceInRect(dry, fx, 140, 80, 220, 160);
        Assert.True(darkMad > 0.5);
        Assert.True(lightMad > 0.5);
    }

    [Fact]
    public void DifferentOrtonAmountsChangeBrightRegion()
    {
        using var dry = CreateOrtonTestCell();
        using var low = CellPhotoEffects.ApplyToCell(
            dry,
            new CellEffectSettings(Noise: false, Orton: true, OrtonAmount: 0.10));
        using var high = CellPhotoEffects.ApplyToCell(
            dry,
            new CellEffectSettings(Noise: false, Orton: true, OrtonAmount: 0.45));

        double lowBright = MeanRgbInRect(low, 140, 40, 220, 120);
        double highBright = MeanRgbInRect(high, 140, 40, 220, 120);
        Assert.True(Math.Abs(highBright - lowBright) > 0.5);
    }

    [Fact]
    public void DifferentNoiseAmountsChangePixels()
    {
        using var dry = CreateNoiseToneTestCell();
        var lowSettings = new CellEffectSettings(
            Noise: true,
            Orton: false,
            NoiseAmount: 0.04,
            NoiseSeed: 99);
        var highSettings = lowSettings with { NoiseAmount = 0.20 };

        using var low = CellPhotoEffects.ApplyToCell(dry, lowSettings);
        using var high = CellPhotoEffects.ApplyToCell(dry, highSettings);

        Assert.True(MeanAbsoluteDifference(low, high) > 0.5);
    }

    private static Image CreateOrtonTestCell()
    {
        const int size = 256;
        using var gray = Image.Black(size, size).NewFromImage(new double[] { 80, 80, 80 }).Cast(Enums.BandFormat.Uchar);
        using var dark = Image.Black(80, 80).NewFromImage(new double[] { 0, 0, 0 }).Cast(Enums.BandFormat.Uchar);
        using var brightLeft = Image.Black(40, 80).NewFromImage(new double[] { 190, 190, 190 }).Cast(Enums.BandFormat.Uchar);
        using var brightRight = Image.Black(40, 80).NewFromImage(new double[] { 230, 230, 230 }).Cast(Enums.BandFormat.Uchar);
        using var bright = brightLeft.Join(brightRight, Enums.Direction.Horizontal);
        using var withDark = gray.Insert(dark, 40, 40);
        return withDark.Insert(bright, 140, 40).Copy(interpretation: Enums.Interpretation.Srgb);
    }

    private static Image CreateNoiseToneTestCell()
    {
        const int w = 256;
        const int h = 200;
        using var dark = Image.Black(w / 2, h).NewFromImage(new double[] { 40, 40, 40 }).Cast(Enums.BandFormat.Uchar);
        using var light = Image.Black(w / 2, h).NewFromImage(new double[] { 230, 230, 230 }).Cast(Enums.BandFormat.Uchar);
        return dark.Join(light, Enums.Direction.Horizontal).Copy(interpretation: Enums.Interpretation.Srgb);
    }

    private static List<string> CreateGrid2x2VPaths(string? tempRoot = null)
    {
        string temp = tempRoot ?? CreateTempDir();
        string folder = Path.Combine(temp, "photos");
        Directory.CreateDirectory(folder);
        for (int i = 0; i < 4; i++)
        {
            File.Copy(SyntheticImages.CreatePortraitJpeg(temp), Path.Combine(folder, $"img_{i:00}.jpg"));
        }

        return Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(4).ToList();
    }

    private static List<string> CreateStack3Paths(string? tempRoot = null, bool useHighlightSource = false)
    {
        string temp = tempRoot ?? CreateTempDir();
        string folder = Path.Combine(temp, "photos");
        Directory.CreateDirectory(folder);
        string source = useHighlightSource
            ? CreateLandscapeWithBrightTop(temp)
            : SyntheticImages.CreateLandscapeJpeg(temp);
        for (int i = 0; i < 3; i++)
        {
            File.Copy(source, Path.Combine(folder, $"img_{i:00}.jpg"));
        }

        return Directory.GetFiles(folder).OrderBy(p => p, StringComparer.Ordinal).Take(3).ToList();
    }

    private static string CreateLandscapeWithBrightTop(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "landscape_bright_top.jpg");
        const int width = 4000;
        const int height = 3000;
        int split = height / 2;
        using var top = Image.Black(width, split).NewFromImage(new double[] { 250, 250, 250 }).Cast(Enums.BandFormat.Uchar);
        using var bottom = Image.Black(width, height - split).NewFromImage(new double[] { 40, 60, 90 }).Cast(Enums.BandFormat.Uchar);
        using var rgb = top.Join(bottom, Enums.Direction.Vertical);
        rgb.Jpegsave(path, q: 90);
        return path;
    }

    private static (byte R, byte G, byte B) GetRgbPixel(Image image, int x, int y)
    {
        double[] rgb = image.Getpoint(x, y);
        return ((byte)Math.Round(rgb[0]), (byte)Math.Round(rgb[1]), (byte)Math.Round(rgb[2]));
    }

    private static double PixelDistance(Image a, Image b, int x, int y)
    {
        var pa = GetRgbPixel(a, x, y);
        var pb = GetRgbPixel(b, x, y);
        return (Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B)) / 3.0;
    }

    private static double MeanRgbInRect(Image image, int x0, int y0, int x1, int y1)
    {
        int w = Math.Max(1, x1 - x0);
        int h = Math.Max(1, y1 - y0);
        using var crop = image.Crop(x0, y0, w, h);
        return crop.Avg();
    }

    private static double MeanAbsoluteDifferenceInRect(Image a, Image b, int x0, int y0, int x1, int y1)
    {
        int w = Math.Max(1, x1 - x0);
        int h = Math.Max(1, y1 - y0);
        using var ca = a.Crop(x0, y0, w, h);
        using var cb = b.Crop(x0, y0, w, h);
        return MeanAbsoluteDifference(ca, cb);
    }

    private static double MeanAbsoluteDifference(Image a, Image b)
    {
        using var diff = a.Subtract(b).Abs();
        return diff.Avg();
    }

    private static string CreateTempDir() =>
        Path.Combine(Path.GetTempPath(), "image-stacker-tests", Guid.NewGuid().ToString("N"));

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
