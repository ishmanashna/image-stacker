using ImageStacker.Core.Contracts;
using ImageStacker.Core.Jobs;

namespace ImageStacker.Tests;

public class CombinationTests
{
    [Fact]
    public void SingleModeReturnsFirstNOnly()
    {
        string temp = CreateTempDir();
        try
        {
            var paths = CreateLandscapePhotos(temp, 10);
            IReadOnlyList<IReadOnlyList<string>> combos = LayoutContracts.ListLayoutCandidates(
                temp, "stack-3", count: 99, batch: false, random: false, borderless: false);

            Assert.Single(combos);
            Assert.Equal(3, combos[0].Count);
            Assert.Equal(paths.Take(3).ToList(), combos[0].ToList());
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void BatchModeReturnsNonOverlappingChunks()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 10);
            IReadOnlyList<IReadOnlyList<string>> combos = LayoutContracts.ListLayoutCandidates(
                temp, "stack-3", count: 1, batch: true, random: false, borderless: false);

            Assert.Equal(3, combos.Count);
            foreach (IReadOnlyList<string> combo in combos)
            {
                Assert.Equal(3, combo.Count);
            }
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void RandomModeReturnsRequestedCount()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 10);
            IReadOnlyList<IReadOnlyList<string>> combos = LayoutContracts.ListLayoutCandidates(
                temp, "stack-3", count: 5, batch: false, random: true, borderless: false);

            Assert.Equal(5, combos.Count);
            foreach (IReadOnlyList<string> combo in combos)
            {
                Assert.Equal(3, combo.Count);
            }
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void BatchRandomModeReturnsChunkCount()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 10);
            IReadOnlyList<IReadOnlyList<string>> combos = LayoutContracts.ListLayoutCandidates(
                temp, "stack-3", count: 99, batch: true, random: true, borderless: false);

            Assert.Equal(3, combos.Count);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ComboSequencesReturnsUpTo40WhenEnoughPhotos()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 80);
            CreatePortraitPhotos(temp, 40);

            IReadOnlyList<(IReadOnlyList<string> Paths, string LayoutName, bool Borderless)> sequences =
                LayoutContracts.ListComboSequences(temp);

            Assert.Equal(40, sequences.Count);
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "grid-2x4"));
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "stack-3"));
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "grid-2x2-v" && !s.Borderless));
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "grid-2x2-v" && s.Borderless));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void ComboSkipsLayoutsWithoutEnoughOrientation()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 80);

            IReadOnlyList<(IReadOnlyList<string> Paths, string LayoutName, bool Borderless)> sequences =
                LayoutContracts.ListComboSequences(temp);

            Assert.Equal(20, sequences.Count);
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "grid-2x4"));
            Assert.Equal(10, sequences.Count(s => s.LayoutName == "stack-3"));
            Assert.Empty(sequences.Where(s => s.LayoutName == "grid-2x2-v"));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void NotEnoughImagesReturnsEmpty()
    {
        string temp = CreateTempDir();
        try
        {
            CreateLandscapePhotos(temp, 2);
            IReadOnlyList<IReadOnlyList<string>> combos = LayoutContracts.ListLayoutCandidates(
                temp, "stack-3", count: 1, batch: false, random: false, borderless: false);

            Assert.Empty(combos);
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Fact]
    public void BadFileDoesNotAbortBatchExport()
    {
        string temp = CreateTempDir();
        try
        {
            var paths = CreateLandscapePhotos(temp, 6);
            string corrupt = Path.Combine(temp, "corrupt.jpg");
            File.WriteAllText(corrupt, "not a jpeg");
            paths.Add(corrupt);

            var jobs = new List<ExportJob>
            {
                new(paths.Take(3).ToList(), "stack-3", false, 1),
                new([paths[0], paths[1], corrupt], "stack-3", false, 2),
                new(paths.Skip(3).Take(3).ToList(), "stack-3", false, 3),
            };

            string outputDir = Path.Combine(temp, "out");
            ExportJobResult result = ExportJobRunner.RunJobs(jobs, outputDir, "white", bleed: false);

            Assert.Equal(2, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Equal(2, Directory.GetFiles(outputDir, "*.jpg").Length);
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

        return paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
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

        return paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
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
