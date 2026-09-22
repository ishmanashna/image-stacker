using NetVips;

namespace ImageStacker.Core.Imaging;

/// <summary>
/// Orton and noise applied to a single photo cell after cover/pan (never gutters or canvas fill).
/// </summary>
public static class CellPhotoEffects
{
    private const double OrtonLift = 1.12;
    private const double NoiseLumaGain = 0.15;
    private const double LumaEpsilon = 1e-4;

    /// <summary>
    /// Applies configured effects to an RGB cell. Returns a new image; caller may dispose <paramref name="rgbCell"/>.
    /// When both effects are off, returns an RGB copy of the input.
    /// </summary>
    public static Image ApplyToCell(Image rgbCell, CellEffectSettings settings)
    {
        settings = settings.WithNormalizedOrtonMask();
        if (!settings.Noise && !settings.Orton)
        {
            return ImagePipeline.EnsureRgb(rgbCell);
        }

        using var rgb = ImagePipeline.EnsureRgb(rgbCell);
        using var memory = ImagePipeline.MaterializeRgb(rgb);
        Image current = ToUnitDouble(memory);

        try
        {
            if (settings.Orton)
            {
                Image next = ApplyOrton(current, settings);
                current.Dispose();
                current = next;
            }

            if (settings.Noise)
            {
                Image next = ApplyNoise(current, settings);
                current.Dispose();
                current = next;
            }

            using (current)
            {
                using var encoded = FromUnitDouble(current);
                return ImagePipeline.MaterializeRgb(encoded);
            }
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    public static Image ApplyToCell(Image rgbCell, bool noise, bool orton, CellEffectSettings? overrides = null)
    {
        CellEffectSettings settings = CellEffectSettings.Resolve(noise, orton, overrides);
        return ApplyToCell(rgbCell, settings);
    }

    /// <summary>
    /// Applies Orton/noise to a full composed preview (cells already pasted). Pixels that matched any
    /// <paramref name="protectColors"/> RGB in the input are restored after effects.
    /// </summary>
    public static Image ApplyToComposedPreview(
        Image rgb,
        CellEffectSettings settings,
        (byte R, byte G, byte B)? protectColor)
    {
        ReadOnlySpan<(byte R, byte G, byte B)> colors = protectColor is { } c
            ? stackalloc[] { c }
            : ReadOnlySpan<(byte R, byte G, byte B)>.Empty;
        return ApplyToComposedPreview(rgb, settings, colors);
    }

    public static Image ApplyToComposedPreview(
        Image rgb,
        CellEffectSettings settings,
        ReadOnlySpan<(byte R, byte G, byte B)> protectColors)
    {
        settings = settings.WithNormalizedOrtonMask();
        if (!settings.Noise && !settings.Orton)
        {
            return ImagePipeline.EnsureRgb(rgb);
        }

        using var dryRgb = ImagePipeline.MaterializeRgb(rgb);
        Image effected = ApplyToCell(dryRgb, settings);
        if (protectColors.Length == 0)
        {
            return effected;
        }

        try
        {
            return RestoreProtectedColors(dryRgb, effected, protectColors);
        }
        catch
        {
            effected.Dispose();
            throw;
        }
    }

    private static Image RestoreProtectedColors(
        Image originalRgb,
        Image effectedRgb,
        ReadOnlySpan<(byte R, byte G, byte B)> protectColors)
    {
        if (protectColors.Length == 0 || originalRgb.Bands != 3 || effectedRgb.Bands != 3)
        {
            return effectedRgb;
        }

        Image? combined = null;
        try
        {
            foreach ((byte pr, byte pg, byte pb) in protectColors)
            {
                using var eq = originalRgb.Equal(new double[] { pr, pg, pb });
                Image thisMatch = eq.Bandbool(Enums.OperationBoolean.And);
                if (combined is null)
                {
                    combined = thisMatch;
                    continue;
                }

                Image next = combined.Boolean(thisMatch, Enums.OperationBoolean.Or);
                combined.Dispose();
                thisMatch.Dispose();
                combined = next;
            }

            using (combined)
            {
                using var restored = combined!.Ifthenelse(originalRgb, effectedRgb);
                Image memory = ImagePipeline.MaterializeRgb(restored);
                effectedRgb.Dispose();
                return memory;
            }
        }
        catch
        {
            combined?.Dispose();
            throw;
        }
    }

    private static Image ApplyOrton(Image baseUnit, CellEffectSettings settings)
    {
        int minSide = Math.Min(baseUnit.Width, baseUnit.Height);
        double blurSigma = Math.Max(0.5, minSide * settings.OrtonBlurPercent / 100.0);
        double featherSigma = minSide * settings.OrtonFeatherPercent / 100.0;

        using var lifted = baseUnit.Linear(new[] { OrtonLift, OrtonLift, OrtonLift }, new[] { 0.0, 0.0, 0.0 }).Clamp(0, 1);
        using var glow = lifted.Gaussblur(blurSigma);
        using var glowBlendRaw = baseUnit.Composite2(glow, Enums.BlendMode.SoftLight);
        using var glowBlend = ImagePipeline.EnsureRgb(glowBlendRaw);

        using var luma = ExtractLuma(baseUnit);
        using var mask = FeatherMask(
            Smoothstep(settings.OrtonMaskLow, settings.OrtonMaskHigh, luma),
            featherSigma);

        using var diff = glowBlend.Subtract(baseUnit);
        using var maskRgb = mask.Bandjoin([mask, mask]);
        using var weighted = diff.Multiply(maskRgb).Linear(
            new[] { settings.OrtonAmount, settings.OrtonAmount, settings.OrtonAmount },
            new[] { 0.0, 0.0, 0.0 });

        return baseUnit.Add(weighted);
    }

    private static Image ApplyNoise(Image baseUnit, CellEffectSettings settings)
    {
        int w = baseUnit.Width;
        int h = baseUnit.Height;
        int longEdge = Math.Max(w, h);
        double spatialSigma = settings.NoiseSize * longEdge / 1000.0;
        spatialSigma = Math.Max(0.01, spatialSigma);

        int seed = settings.NoiseSeed ?? Random.Shared.Next();
        using var rawNoise = Image.Gaussnoise(w, h, sigma: 1.0, mean: 0.0, seed: seed)
            .Cast(Enums.BandFormat.Double);
        using var noiseBand = spatialSigma > 0.01
            ? rawNoise.Gaussblur(spatialSigma)
            : rawNoise;

        using var luma = ExtractLuma(baseUnit);
        using var toneT = Smoothstep(0.25, 0.75, luma);
        using var weight = toneT
            .Linear(new[] { settings.NoiseHighlights - settings.NoiseShadows }, new[] { settings.NoiseShadows })
            .Clamp(0, 1);

        using var delta = noiseBand
            .Multiply(weight)
            .Linear(new[] { settings.NoiseAmount * NoiseLumaGain }, new[] { 0.0 });

        using var lPrime = luma.Add(delta).Clamp(0, 1);
        using var eps = luma.NewFromImage(LumaEpsilon).Cast(Enums.BandFormat.Double);
        using var lSafe = luma.Maxpair(eps);
        using var scale = lPrime.Divide(lSafe);
        using var scaleRgb = scale.Bandjoin([scale, scale]);

        return baseUnit.Multiply(scaleRgb).Clamp(0, 1);
    }

    private static Image ExtractLuma(Image rgbUnit)
    {
        using var r = rgbUnit.ExtractBand(0);
        using var g = rgbUnit.ExtractBand(1);
        using var b = rgbUnit.ExtractBand(2);
        return r.Linear(new[] { 0.299 }, new[] { 0.0 })
            .Add(g.Linear(new[] { 0.587 }, new[] { 0.0 }))
            .Add(b.Linear(new[] { 0.114 }, new[] { 0.0 }));
    }

    private static Image Smoothstep(double edge0, double edge1, Image x)
    {
        double range = edge1 - edge0;
        if (Math.Abs(range) < 1e-12)
        {
            using var zero = x.NewFromImage(0.0).Cast(Enums.BandFormat.Double);
            return zero.Copy();
        }

        using var t = x
            .Linear(new[] { 1.0 / range }, new[] { -edge0 / range })
            .Clamp(0, 1);
        using var t2 = t.Multiply(t);
        using var factor = t.Linear(new[] { -2.0 }, new[] { 3.0 });
        return t2.Multiply(factor).Copy();
    }

    private static Image FeatherMask(Image mask, double featherSigma)
    {
        if (featherSigma <= 0.01)
        {
            return mask.Copy();
        }

        return mask.Gaussblur(featherSigma);
    }

    private static Image ToUnitDouble(Image rgb)
    {
        return rgb.Cast(Enums.BandFormat.Double).Linear(new[] { 1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0 }, new[] { 0.0, 0.0, 0.0 });
    }

    private static Image FromUnitDouble(Image unitDouble)
    {
        return unitDouble
            .Linear(new[] { 255.0, 255.0, 255.0 }, new[] { 0.0, 0.0, 0.0 })
            .Clamp(0, 255)
            .Cast(Enums.BandFormat.Uchar)
            .Copy(interpretation: Enums.Interpretation.Srgb);
    }
}
