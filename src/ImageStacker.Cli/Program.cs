using ImageStacker.Core;
using ImageStacker.Core.Contracts;
using ImageStacker.Core.Export;
using ImageStacker.Core.Io;
using ImageStacker.Core.Jobs;
using ImageStacker.Core.Layout;

namespace ImageStacker.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        CoreDiagnostics.WarningHandler = message => Console.Error.WriteLine($"[WARN] {message}");

        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            CliOptions options = CliOptions.Parse(args);
            return RunExport(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunExport(CliOptions options)
    {
        Directory.CreateDirectory(options.OutputFolder);

        if (options.Combo)
        {
            return RunCombo(options);
        }

        return RunLayout(options);
    }

    private static int RunCombo(CliOptions options)
    {
        IReadOnlyList<(IReadOnlyList<string> Paths, string LayoutName, bool Borderless)> sequences =
            LayoutContracts.ListComboSequences(options.InputFolder);

        if (sequences.Count == 0)
        {
            Console.Error.WriteLine("[SKIP] Combo: no collages to generate (check folder and orientations).");
            return 1;
        }

        var jobs = sequences
            .Select((seq, index) => new ExportJob(seq.Paths, seq.LayoutName, seq.Borderless, index + 1))
            .ToList();

        Console.WriteLine($"Processing {jobs.Count} combo collages...");
        ExportJobResult result = ExportJobRunner.RunJobs(
            jobs,
            options.OutputFolder,
            options.Color,
            options.Bleed,
            path => Console.WriteLine($"Wrote {path}"),
            (index, message) => Console.Error.WriteLine($"[FAIL] Job {index:000}: {message}"));

        PrintFinish(result);
        return result.Succeeded > 0 ? 0 : 1;
    }

    private static int RunLayout(CliOptions options)
    {
        LayoutDefinition layout = LayoutCatalog.GetRequired(options.Layout);
        IReadOnlyList<IReadOnlyList<string>> candidates = LayoutContracts.ListLayoutCandidates(
            options.InputFolder,
            options.Layout,
            options.Count,
            options.Batch,
            options.Random,
            options.Borderless);

        if (candidates.Count == 0)
        {
            IReadOnlyList<string> validPaths = ImageScanner.GetValidPaths(
                options.InputFolder,
                layout.Orientation);

            if (validPaths.Count < layout.NumImages)
            {
                Console.Error.WriteLine(
                    $"[SKIP] Not enough {LayoutCatalog.OrientationKey(layout.Orientation)} images " +
                    $"({validPaths.Count}/{layout.NumImages}).");
            }
            else
            {
                Console.Error.WriteLine("[SKIP] Could not generate valid combinations.");
            }

            return 1;
        }

        var jobs = candidates
            .Select((paths, index) => new ExportJob(paths, options.Layout, options.Borderless, index + 1))
            .ToList();

        string mode = options.Batch ? "batch" : options.Random ? $"random (count {options.Count})" : "single";
        Console.WriteLine($"Processing {jobs.Count} {options.Layout} collages ({mode})...");

        ExportJobResult result = ExportJobRunner.RunJobs(
            jobs,
            options.OutputFolder,
            options.Color,
            options.Bleed,
            path => Console.WriteLine($"Wrote {path}"),
            (index, message) => Console.Error.WriteLine($"[FAIL] Job {index:000}: {message}"));

        PrintFinish(result);
        return result.Succeeded > 0 ? 0 : 1;
    }

    private static void PrintFinish(ExportJobResult result)
    {
        if (result.Failed > 0)
        {
            Console.WriteLine(
                $"[FINISH] {result.Succeeded}/{result.Total} succeeded, {result.Failed} failed.");
        }
        else
        {
            Console.WriteLine($"[FINISH] Wrote {result.Succeeded} collage(s).");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            ImageStacker CLI

            Usage:
              ImageStacker.Cli <photos-folder> --output <dir> [mode options] [export options]

            Mode (pick one):
              --layout <name>           Layout for single/batch/random export (required unless --combo)
              --combo                   Standard combo set (40 collages when enough H/V photos)
              --batch                   Non-overlapping groups of N photos (sorted path order)
              --random                  Random groups; use with --count (default 1)
              --batch --random          Shuffle once, then non-overlapping chunks

            Export options:
              --count <N>               Groups for --random (ignored for single/batch-only)
              --borderless              Borderless spacing (ignored in --combo)
              --bleed                   Bleed first row to canvas edge
              --color <name|#RRGGBB>    Background color (default: white)

            Layouts:
              stack-2, stack-3, stack-4, grid-1x2-v, grid-1x3-h, grid-1x3-v, grid-2x4, grid-3x3, grid-2x2-v, grid-2x2-h

            Examples:
              dotnet run --project src/ImageStacker.Cli -- "C:\photos" --layout stack-3 --output "C:\out"
              dotnet run --project src/ImageStacker.Cli -- "C:\photos" --layout grid-2x4 --batch --output "C:\out"
              dotnet run --project src/ImageStacker.Cli -- "C:\photos" --layout stack-3 --random --count 5 --output "C:\out"
              dotnet run --project src/ImageStacker.Cli -- "C:\photos" --combo --output "C:\out" --color navy
            """);
    }
}

internal sealed class CliOptions
{
    public required string InputFolder { get; init; }
    public string Layout { get; init; } = "stack-3";
    public required string OutputFolder { get; init; }
    public int Count { get; init; } = 1;
    public bool Combo { get; init; }
    public bool Batch { get; init; }
    public bool Random { get; init; }
    public bool Borderless { get; init; }
    public bool Bleed { get; init; }
    public string Color { get; init; } = "white";

    public static CliOptions Parse(string[] args)
    {
        string? folder = null;
        string layout = "stack-3";
        string? output = null;
        int count = 1;
        bool combo = false;
        bool batch = false;
        bool random = false;
        bool borderless = false;
        bool bleed = false;
        string color = "white";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--layout":
                    layout = RequireValue(args, ref i, arg);
                    break;
                case "--output":
                    output = RequireValue(args, ref i, arg);
                    break;
                case "--count":
                    count = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--combo":
                    combo = true;
                    break;
                case "--batch":
                    batch = true;
                    break;
                case "--random":
                    random = true;
                    break;
                case "--borderless":
                    borderless = true;
                    break;
                case "--bleed":
                    bleed = true;
                    break;
                case "--color":
                    color = RequireValue(args, ref i, arg);
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }

                    folder ??= arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("Missing input photos folder.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("Missing required --output.");
        }

        if (!combo && string.IsNullOrWhiteSpace(layout))
        {
            throw new ArgumentException("Missing required --layout (or use --combo).");
        }

        return new CliOptions
        {
            InputFolder = folder,
            Layout = layout,
            OutputFolder = output,
            Count = count,
            Combo = combo,
            Batch = batch,
            Random = random,
            Borderless = borderless,
            Bleed = bleed,
            Color = color,
        };
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }
}
