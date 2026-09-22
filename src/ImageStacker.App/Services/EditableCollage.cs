using ImageStacker.Core;
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

    public IReadOnlyList<string> Paths { get; }

    public List<SlotAssignment?> Slots { get; }

    public static EditableCollage FromJob(ExportJob job) =>
        new(job.LayoutName, job.Borderless, job.Paths, MaterializeSlots(job.LayoutName, job.Paths));

    public static EditableCollage Blank(string layout, bool borderless)
    {
        int required = LayoutCatalog.GetRequired(layout).NumImages;
        var slots = Enumerable.Repeat<SlotAssignment?>(null, required).ToList();
        return new EditableCollage(layout, borderless, Array.Empty<string>(), slots);
    }

    public bool MatchesJob(ExportJob job) =>
        string.Equals(Layout, job.LayoutName, StringComparison.OrdinalIgnoreCase) &&
        Paths.SequenceEqual(job.Paths, StringComparer.OrdinalIgnoreCase);

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
