namespace ImageStacker.Core.Layout;

public enum LayoutOrientation
{
    Horizontal,
    Vertical,
    Mixed,
}

public sealed record LayoutDefinition(
    string Name,
    int NumImages,
    int Rows,
    int Cols,
    LayoutOrientation Orientation,
    bool Framed,
    bool LandscapeCanvas = false);

public static class LayoutCatalog
{
    public static readonly IReadOnlyDictionary<string, LayoutDefinition> Layouts =
        new Dictionary<string, LayoutDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["stack-1"] = new("stack-1", 1, 1, 1, LayoutOrientation.Mixed, Framed: false),
            ["stack-2"] = new("stack-2", 2, 2, 1, LayoutOrientation.Horizontal, Framed: false),
            ["stack-3"] = new("stack-3", 3, 3, 1, LayoutOrientation.Horizontal, Framed: false),
            ["stack-4"] = new("stack-4", 4, 4, 1, LayoutOrientation.Horizontal, Framed: false),
            ["grid-1x2-v"] = new("grid-1x2-v", 2, 1, 2, LayoutOrientation.Vertical, Framed: false),
            ["grid-1x3-v"] = new("grid-1x3-v", 3, 1, 3, LayoutOrientation.Vertical, Framed: false),
            ["grid-2x4"] = new("grid-2x4", 8, 4, 2, LayoutOrientation.Horizontal, Framed: false),
            ["grid-3x3"] = new("grid-3x3", 9, 3, 3, LayoutOrientation.Horizontal, Framed: false),
            ["grid-2x2-v"] = new("grid-2x2-v", 4, 2, 2, LayoutOrientation.Vertical, Framed: true),
            ["grid-2x2-h"] = new("grid-2x2-h", 4, 2, 2, LayoutOrientation.Horizontal, Framed: true, LandscapeCanvas: true),
        };

    public static LayoutDefinition GetRequired(string layoutName)
    {
        if (!Layouts.TryGetValue(layoutName, out var layout))
        {
            throw new ArgumentException($"Unknown layout '{layoutName}'.", nameof(layoutName));
        }

        return layout;
    }

    public static string OrientationKey(LayoutOrientation orientation) => orientation switch
    {
        LayoutOrientation.Horizontal => "horizontal",
        LayoutOrientation.Vertical => "vertical",
        LayoutOrientation.Mixed => "mixed",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
    };
}
