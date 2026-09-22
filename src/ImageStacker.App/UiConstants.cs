namespace ImageStacker.App;

internal static class UiConstants
{
    public const int ThumbDisplayCap = 200;
    public const int ThumbBatchSize = 22;
    public const int ThumbGridCols = 4;
    public const int ThumbTileWidth = 116;
    public const int ThumbTileHeight = 84;

    public static readonly string[] LayoutOrder =
    [
        "stack-1",
        "stack-2",
        "stack-3",
        "stack-4",
        "grid-1x2-v",
        "grid-1x3-v",
        "grid-2x4",
        "grid-3x3",
        "grid-2x2-v",
        "grid-2x2-h",
    ];

    public static readonly (string Label, string Value)[] ColorPresets =
    [
        ("White", "white"),
        ("Black", "black"),
        ("Beige", "beige"),
        ("Ivory", "ivory"),
        ("Gray", "gray"),
        ("Light gray", "lightgray"),
        ("Dark gray", "darkgray"),
        ("Wheat", "wheat"),
        ("Tan", "tan"),
        ("Navy", "navy"),
        ("Maroon", "maroon"),
        ("Red", "red"),
        ("Lime", "lime"),
        ("Yellow", "yellow"),
        ("Orange", "orange"),
        ("Magenta", "magenta"),
    ];

}
