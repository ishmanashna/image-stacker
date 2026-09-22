using ImageStacker.Core;
using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Io;
using ImageStacker.Core.Jobs;
using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Tests;

public class SoftDeckRefreshTests
{
    [Fact]
    public void MatchingJobIdentityPreservesEditedSlotAssignments()
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

            IReadOnlyList<string> paths = Directory.GetFiles(folder)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Take(3)
                .ToList();
            ExportJob jobBefore = new(paths, "stack-3", false, 1);
            ExportJob jobAfter = new(paths, "stack-3", false, 1);

            List<SlotAssignment?> slots = paths.Select(p => (SlotAssignment?)new SlotAssignment(p)).ToList();
            slots[0] = slots[0]! with { PanX = 0.5, FlipH = true };

            Assert.True(MatchesJobIdentity(jobBefore, jobAfter));
            Assert.Equal(0.5, slots[0]!.PanX);
            Assert.True(slots[0]!.FlipH);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static bool MatchesJobIdentity(ExportJob a, ExportJob b) =>
        a.JobIndex == b.JobIndex &&
        string.Equals(a.LayoutName, b.LayoutName, StringComparison.OrdinalIgnoreCase) &&
        a.Borderless == b.Borderless &&
        a.Paths.SequenceEqual(b.Paths, StringComparer.OrdinalIgnoreCase);

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

public class ExportSlotsTests
{
    [Fact]
    public void ExportCollageAppliesSlotPanDistinctFromDefaults()
    {
        string temp = CreateTempDir();
        try
        {
            string gradient = SyntheticImages.CreatePortraitJpegWithHorizontalGradient(temp);
            List<string> paths =
            [
                Path.Combine(temp, "left.jpg"),
                Path.Combine(temp, "right.jpg"),
            ];
            File.Copy(gradient, paths[0]);
            File.Copy(gradient, paths[1]);

            string defaultOutput = Path.Combine(temp, "default.jpg");
            string pannedOutput = Path.Combine(temp, "panned.jpg");

            CollageExporter.ExportCollage(paths, "grid-1x2-v", borderless: false, color: "white", defaultOutput);

            IReadOnlyList<SlotAssignment> centeredSlots =
            [
                new SlotAssignment(paths[0], PanX: 0.0),
                new SlotAssignment(paths[1], PanX: 0.0),
            ];
            CollageExporter.ExportCollage(
                paths, "grid-1x2-v", borderless: false, color: "white", pannedOutput, slots: centeredSlots);

            using var defaultImage = Image.NewFromFile(defaultOutput);
            using var pannedImage = Image.NewFromFile(pannedOutput);
            Assert.False(ImagesAreIdentical(defaultImage, pannedImage));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ExportJobRunnerAppliesNonNullSlots()
    {
        string temp = CreateTempDir();
        try
        {
            string gradient = SyntheticImages.CreatePortraitJpegWithHorizontalGradient(temp);
            List<string> paths = [];
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(temp, $"slot_{i}.jpg");
                File.Copy(gradient, path);
                paths.Add(path);
            }

            string baselineOutput = Path.Combine(temp, "baseline.jpg");
            CollageExporter.ExportCollage(paths, "stack-3", borderless: false, color: "white", baselineOutput);

            IReadOnlyList<SlotAssignment> slots = paths
                .Select((path, index) => index == 0
                    ? new SlotAssignment(path, FlipH: true)
                    : new SlotAssignment(path))
                .ToList();
            string runnerOutput = Path.Combine(temp, "runner.jpg");
            ExportJob job = new(paths, "stack-3", false, 1, slots, runnerOutput);
            ExportJobResult result = ExportJobRunner.RunJobs([job], temp, "white", bleed: false);

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            using var baselineImage = Image.NewFromFile(baselineOutput);
            using var runnerImage = Image.NewFromFile(runnerOutput);
            Assert.False(ImagesAreIdentical(baselineImage, runnerImage));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static bool ImagesAreIdentical(Image a, Image b)
    {
        if (a.Width != b.Width || a.Height != b.Height || a.Bands != b.Bands)
        {
            return false;
        }

        byte[] bytesA = a.WriteToBuffer(".png");
        byte[] bytesB = b.WriteToBuffer(".png");
        return bytesA.AsSpan().SequenceEqual(bytesB);
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

public class ScannerOrientationFilterTests
{
    [Fact]
    public void GetValidPathsExcludesPortraitForHorizontalLayout()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 3);
            CreatePortraitPhotos(temp, 3);

            IReadOnlyList<string> valid = ImageScanner.GetValidPaths(temp, LayoutOrientation.Horizontal);

            Assert.Equal(3, valid.Count);
            Assert.All(valid, path => Assert.DoesNotContain("portrait", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static List<string> CreateLandscapePhotos(string directory, int count)
    {
        string synthDir = Path.Combine(directory, "_synth");
        Directory.CreateDirectory(synthDir);
        string template = SyntheticImages.CreateLandscapeJpeg(synthDir);

        var paths = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(directory, $"landscape_{i:00}.jpg");
            File.Copy(template, path);
            paths.Add(path);
        }

        return paths;
    }

    private static List<string> CreatePortraitPhotos(string directory, int count)
    {
        string synthDir = Path.Combine(directory, "_synth");
        Directory.CreateDirectory(synthDir);
        string template = SyntheticImages.CreatePortraitJpeg(synthDir);

        var paths = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(directory, $"portrait_{i:00}.jpg");
            File.Copy(template, path);
            paths.Add(path);
        }

        return paths;
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
