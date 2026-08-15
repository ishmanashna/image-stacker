using ImageStacker.Core.Io;
using ImageStacker.Core.Jobs;
using ImageStacker.Core.Layout;
using ImageStacker.Core.Preview;
using NetVips;

namespace ImageStacker.Core.Contracts;

public static class LayoutContracts
{
    public static IReadOnlyList<IReadOnlyList<string>> ListLayoutCandidates(
        string folder,
        string layout,
        int count,
        bool batch,
        bool random,
        bool borderless)
    {
        _ = borderless;

        LayoutDefinition definition = LayoutCatalog.GetRequired(layout);
        IReadOnlyList<string> validPaths = ImageScanner.GetValidPaths(folder, definition.Orientation);
        if (validPaths.Count < definition.NumImages)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }

        return CombinationGenerator.Generate(validPaths, definition.NumImages, batch, random, count);
    }

    public static IReadOnlyList<(IReadOnlyList<string> Paths, string LayoutName, bool Borderless)> ListComboSequences(
        string folder)
    {
        var sequences = new List<(IReadOnlyList<string>, string, bool)>();

        foreach (ComboJobSpec spec in ComboJobSpecs.All)
        {
            IReadOnlyList<IReadOnlyList<string>> combos = ListLayoutCandidates(
                folder,
                spec.LayoutName,
                spec.Count,
                spec.Batch,
                spec.Random,
                spec.Borderless);

            foreach (IReadOnlyList<string> combo in combos)
            {
                sequences.Add((combo, spec.LayoutName, spec.Borderless));
            }
        }

        return sequences;
    }

    public static NetVips.Image RenderPreview(
        IReadOnlyList<string> orderedPaths,
        string layoutName,
        bool borderless,
        object color,
        bool bleed = false,
        IReadOnlyList<SlotAssignment>? slots = null,
        int previewLongEdge = 1000)
    {
        return CollagePreviewRenderer.Render(
            orderedPaths,
            layoutName,
            borderless,
            color,
            bleed,
            slots,
            previewLongEdge);
    }

    public static NetVips.Image RenderManualPreview(
        string layoutName,
        bool borderless,
        object color,
        bool bleed,
        IReadOnlyList<SlotAssignment?> slots,
        int previewLongEdge = 1000,
        int? livePanSlot = null,
        double? livePanX = null,
        double? livePanY = null)
    {
        return CollagePreviewRenderer.RenderManual(
            layoutName,
            borderless,
            color,
            bleed,
            slots,
            previewLongEdge,
            livePanSlot,
            livePanX,
            livePanY);
    }
}
