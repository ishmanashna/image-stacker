using ImageStacker.Core.Export;
using ImageStacker.Core.Imaging;

namespace ImageStacker.Core.Jobs;

public sealed record ExportJob(
    IReadOnlyList<string> Paths,
    string LayoutName,
    bool Borderless,
    int JobIndex,
    IReadOnlyList<SlotAssignment>? Slots = null,
    string? OutputPath = null);

public sealed record ExportJobResult(int Succeeded, int Failed, int Total);

public static class ExportJobRunner
{
    public static ExportJobResult RunJobs(
        IReadOnlyList<ExportJob> jobs,
        string outputDir,
        object color,
        bool bleed,
        Action<string>? onSuccess = null,
        Action<int, string>? onFailure = null)
    {
        if (jobs.Count == 0)
        {
            return new ExportJobResult(0, 0, 0);
        }

        Directory.CreateDirectory(outputDir);

        int degree = Math.Min(8, Math.Min(Environment.ProcessorCount, jobs.Count));
        int succeeded = 0;
        int failed = 0;

        using var cache = new SourceImageCache();

        // One thread per worker is enough with Parallel.ForEach; restore after batch so preview/UI is not stuck.
        int previousConcurrency = NetVips.NetVips.Concurrency;
        try
        {
            NetVips.NetVips.Concurrency = 1;

            Parallel.ForEach(
                jobs,
                new ParallelOptions { MaxDegreeOfParallelism = degree },
                job =>
                {
                    try
                    {
                        string outputPath = job.OutputPath ?? CollageExporter.GenerateOutputFilename(
                            outputDir,
                            job.LayoutName,
                            job.JobIndex);

                        CollageExporter.ExportCollage(
                            job.Paths,
                            job.LayoutName,
                            job.Borderless,
                            color,
                            outputPath,
                            bleed,
                            job.Slots,
                            cache);

                        Interlocked.Increment(ref succeeded);
                        onSuccess?.Invoke(outputPath);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        onFailure?.Invoke(job.JobIndex, ex.Message);
                    }
                });
        }
        finally
        {
            NetVips.NetVips.Concurrency = previousConcurrency;
        }

        return new ExportJobResult(succeeded, failed, jobs.Count);    }
}
