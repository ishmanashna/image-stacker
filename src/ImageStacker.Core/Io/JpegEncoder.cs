using ImageStacker.Core;
using NetVips;

namespace ImageStacker.Core.Io;

public static class JpegEncoder
{
    public static void SaveOptimized(NetVips.Image image, string outputPath, int maxFileSize = Constants.MaxOutputJpegBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var rgb = image.Colourspace(Enums.Interpretation.Srgb);
        byte[]? smallest = null;
        int smallestLength = int.MaxValue;

        foreach (int quality in Constants.JpegQualityProbes)
        {
            byte[] buffer = rgb.JpegsaveBuffer(q: quality, subsampleMode: Enums.ForeignSubsample.Off);
            if (buffer.Length <= maxFileSize)
            {
                File.WriteAllBytes(outputPath, buffer);
                return;
            }

            if (buffer.Length < smallestLength)
            {
                smallestLength = buffer.Length;
                smallest = buffer;
            }
        }

        if (smallest is null)
        {
            throw new InvalidOperationException("JPEG encode produced no output.");
        }

        CoreDiagnostics.Warn(
            $"JPEG output exceeds {maxFileSize:N0} byte limit at all quality probes " +
            $"({string.Join(", ", Constants.JpegQualityProbes)}); wrote smallest encode " +
            $"({smallestLength:N0} bytes) to '{outputPath}'.");

        File.WriteAllBytes(outputPath, smallest);
    }
}
