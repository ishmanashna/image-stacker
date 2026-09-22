using ImageStacker.Core;
using ImageStacker.Core.Imaging;
using ImageStacker.Core.Jobs;
using ImageStacker.Core.Layout;

namespace ImageStacker.App.Services;

internal sealed class EditableCollage
{
    public EditableCollage(
        string layout,
        bool borderless,
        IReadOnlyList<string> paths,
        List<SlotAssignment?> slots)
    {
        Layout = layout;
        Borderless = borderless;
        Paths = paths;
        Slots = slots;
    }

    public string Layout { get; set; }

    public bool Borderless { get; set; }

    public bool Noise { get; set; }

    public bool Orton { get; set; }

    public double OrtonAmount { get; set; } = 0.22;

    public double OrtonBlurPercent { get; set; } = 0.70;

    public double OrtonMaskLow { get; set; } = 0.48;

    public double OrtonMaskHigh { get; set; } = 0.82;

    public double OrtonFeatherPercent { get; set; } = 0.25;

    public double NoiseAmount { get; set; } = 0.08;

    public double NoiseSize { get; set; } = 0.90;

    public double NoiseShadows { get; set; } = 1.00;

    public double NoiseHighlights { get; set; } = 0.15;

    public IReadOnlyList<string> Paths { get; }

    public List<SlotAssignment?> Slots { get; }

    public static EditableCollage FromJob(ExportJob job)
    {
        var collage = new EditableCollage(
            job.LayoutName,
            job.Borderless,
            job.Paths,
            MaterializeSlots(job.LayoutName, job.Paths));
        collage.Noise = job.CellEffects?.Noise ?? job.Noise;
        collage.Orton = job.CellEffects?.Orton ?? job.Orton;
        if (job.CellEffects is { } effects)
        {
            collage.OrtonAmount = effects.OrtonAmount;
            collage.OrtonBlurPercent = effects.OrtonBlurPercent;
            collage.OrtonMaskLow = effects.OrtonMaskLow;
            collage.OrtonMaskHigh = effects.OrtonMaskHigh;
            collage.OrtonFeatherPercent = effects.OrtonFeatherPercent;
            collage.NoiseAmount = effects.NoiseAmount;
            collage.NoiseSize = effects.NoiseSize;
            collage.NoiseShadows = effects.NoiseShadows;
            collage.NoiseHighlights = effects.NoiseHighlights;
        }

        return collage;
    }

    public static EditableCollage Blank(string layout, bool borderless)
    {
        int required = LayoutCatalog.GetRequired(layout).NumImages;
        var slots = Enumerable.Repeat<SlotAssignment?>(null, required).ToList();
        return new EditableCollage(layout, borderless, Array.Empty<string>(), slots);
    }

    public bool MatchesJob(ExportJob job) =>
        string.Equals(Layout, job.LayoutName, StringComparison.OrdinalIgnoreCase) &&
        Paths.SequenceEqual(job.Paths, StringComparer.OrdinalIgnoreCase);

    public CellEffectSettings ToCellEffectSettings() =>
        new(
            Noise,
            Orton,
            OrtonAmount,
            OrtonBlurPercent,
            OrtonMaskLow,
            OrtonMaskHigh,
            OrtonFeatherPercent,
            NoiseAmount,
            NoiseSize,
            NoiseShadows,
            NoiseHighlights);

    private static List<SlotAssignment?> MaterializeSlots(string layout, IReadOnlyList<string> paths)
    {
        var assignments = new List<SlotAssignment?>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            assignments.Add(new SlotAssignment(
                paths[i],
                PanX: CropDefaults.DefaultPanX(layout, i),
                PanY: CropDefaults.DefaultPanY(layout)));
        }

        return assignments;
    }
}
