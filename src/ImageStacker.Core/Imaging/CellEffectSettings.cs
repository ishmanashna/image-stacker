namespace ImageStacker.Core.Imaging;

/// <summary>
/// Per-cell Orton and noise parameters. Flags control whether each effect runs; numeric fields use locked defaults when only bools are passed from export/preview.
/// </summary>
public sealed record CellEffectSettings(
    bool Noise,
    bool Orton,
    double OrtonAmount = 0.22,
    double OrtonBlurPercent = 0.70,
    double OrtonMaskLow = 0.48,
    double OrtonMaskHigh = 0.82,
    double OrtonFeatherPercent = 0.25,
    double NoiseAmount = 0.08,
    double NoiseSize = 0.90,
    double NoiseShadows = 1.00,
    double NoiseHighlights = 0.15,
    int? NoiseSeed = null)
{
    public static CellEffectSettings FromFlags(bool noise, bool orton) => new(noise, orton);

    public CellEffectSettings WithNormalizedOrtonMask()
    {
        if (OrtonMaskLow <= OrtonMaskHigh)
        {
            return this;
        }

        return this with { OrtonMaskLow = OrtonMaskHigh, OrtonMaskHigh = OrtonMaskLow };
    }

    public static CellEffectSettings Resolve(bool noise, bool orton, CellEffectSettings? overrides)
    {
        if (overrides is not null)
        {
            return overrides.WithNormalizedOrtonMask();
        }

        return FromFlags(noise, orton).WithNormalizedOrtonMask();
    }
}
