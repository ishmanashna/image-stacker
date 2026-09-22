using NetVips;

namespace ImageStacker.Core.Imaging;

/// <summary>
/// Full-canvas RGB effects applied once after <see cref="Export.CollageExporter.ComposeCanvas"/>.
/// </summary>
public static class PostComposeEffects
{
    public const double NoiseMix = 0.12;
    public const double OrtonScreenMix = 0.5;
    public const double OrtonBlurFractionOfMinSide = 0.03;

    /// <summary>Fine grain deviation (0–255 scale) for gaussian noise.</summary>
    private const double NoiseSigma = 12.0;

    private const double NoiseMean = 128.0;

    /// <summary>
    /// Applies optional noise and Orton to an RGB canvas. Returns a new image; caller disposes <paramref name="rgbCanvas"/>.
    /// </summary>
    public static Image Apply(Image rgbCanvas, bool noise, bool orton)
    {
        if (!noise && !orton)
        {
            throw new ArgumentException("At least one effect must be enabled.", nameof(noise));
        }

        using var rgb = ImagePipeline.EnsureRgb(rgbCanvas);
        Image current = rgb.Copy();

        try
        {
            if (noise)
            {
                Image next = ApplyNoise(current);
                current.Dispose();
                current = next;
            }

            if (orton)
            {
                Image next = ApplyOrton(current);
                current.Dispose();
                current = next;
            }

            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static Image ApplyNoise(Image rgb)
    {
        int w = rgb.Width;
        int h = rgb.Height;
        using var grain = NetVips.Image.Gaussnoise(w, h, sigma: NoiseSigma, mean: NoiseMean, seed: Random.Shared.Next());
        using var grainRgb = grain.Bandjoin([grain, grain]);

        using var baseD = ToUnitDouble(rgb);
        using var noiseD = ToUnitDouble(grainRgb);
        using var mixedD = baseD
            .Linear(new[] { 1.0 - NoiseMix }, new[] { 0.0 })
            .Add(noiseD.Linear(new[] { NoiseMix }, new[] { 0.0 }));

        return FromUnitDouble(mixedD);
    }

    private static Image ApplyOrton(Image rgb)
    {
        int minSide = Math.Min(rgb.Width, rgb.Height);
        double sigma = Math.Max(1.0, minSide * OrtonBlurFractionOfMinSide);

        using var baseD = ToUnitDouble(rgb);
        using var blurD = baseD.Gaussblur(sigma);
        using var screenD = baseD.Add(blurD).Subtract(baseD.Multiply(blurD));
        using var mixedD = baseD
            .Linear(new[] { 1.0 - OrtonScreenMix }, new[] { 0.0 })
            .Add(screenD.Linear(new[] { OrtonScreenMix }, new[] { 0.0 }));

        return FromUnitDouble(mixedD);
    }

    private static Image ToUnitDouble(Image rgb)
    {
        return rgb.Cast(Enums.BandFormat.Double).Linear(new[] { 1.0 / 255.0 }, new[] { 0.0 });
    }

    private static Image FromUnitDouble(Image unitDouble)
    {
        return unitDouble
            .Linear(new[] { 255.0 }, new[] { 0.0 })
            .Clamp(0, 255)
            .Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
    }
}
