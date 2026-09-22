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
        "stack-2",
        "stack-3",
        "stack-4",
        "grid-1x2-v",
        "grid-1x3-h",
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
    ];

    public static readonly Dictionary<string, (string Title, string Subtitle)> LayoutCardText =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["stack-2"] = ("Stack 2", "2 photos — H"),
            ["stack-3"] = ("Stack 3", "3 photos — H"),
            ["stack-4"] = ("Stack 4", "4 photos — H"),
            ["grid-1x2-v"] = ("Split 1×2 V", "2 photos — V — opposite halves"),
            ["grid-1x3-h"] = ("Row 1×3 H", "3 photos — H"),
            ["grid-1x3-v"] = ("Row 1×3 V", "3 photos — V"),
            ["grid-2x4"] = ("Grid 2×4", "8 photos — H"),
            ["grid-3x3"] = ("Grid 3×3", "9 photos — H"),
            ["grid-2x2-v"] = ("Grid 2×2 V", "4 photos — V — frame"),
            ["grid-2x2-h"] = ("Grid 2×2 H", "4 photos — H — frame"),
        };
}
