using System.Text.RegularExpressions;

namespace ImageStacker.Core.Layout;

public static partial class LayoutCardCopy
{
    private const char Times = '\u00D7';

    private static readonly Dictionary<string, string> ShortNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["stack-1"] = $"Stack 1",
            ["stack-2"] = $"Stack 2",
            ["stack-3"] = $"Stack 3",
            ["stack-4"] = $"Stack 4",
            ["grid-1x2-v"] = $"Split 1{Times}2 V",
            ["grid-1x3-v"] = $"Row 1{Times}3 V",
            ["grid-2x4"] = $"Grid 2{Times}4",
            ["grid-3x3"] = $"Grid 3{Times}3",
            ["grid-2x2-v"] = $"Grid 2{Times}2 V",
            ["grid-2x2-h"] = $"Grid 2{Times}2 H",
        };

    [GeneratedRegex(@"^grid-(\d+)x(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GridIdAxBPattern();

    public static string GetShortName(string layoutId)
    {
        if (ShortNames.TryGetValue(layoutId, out string? name))
        {
            return name;
        }

        return layoutId;
    }

    public static string FormatInputOrientation(LayoutOrientation orientation) => orientation switch
    {
        LayoutOrientation.Horizontal => "in H",
        LayoutOrientation.Vertical => "in V",
        LayoutOrientation.Mixed => "in any",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation)),
    };

    public static string FormatOutputOrientation(string layoutId, LayoutDefinition definition)
    {
        if (string.Equals(layoutId, "stack-1", StringComparison.OrdinalIgnoreCase))
        {
            return "out photo";
        }

        return definition.LandscapeCanvas ? "out H" : "out V";
    }

    public static string FormatAxB(string layoutId, LayoutDefinition definition)
    {
        Match match = GridIdAxBPattern().Match(layoutId);
        if (match.Success)
        {
            return $"{match.Groups[1].Value}{Times}{match.Groups[2].Value}";
        }

        return $"{definition.Rows}{Times}{definition.Cols}";
    }

    public static string BuildCardText(string layoutId, LayoutDefinition definition) =>
        string.Join(
            "\n",
            GetShortName(layoutId),
            FormatInputOrientation(definition.Orientation),
            FormatOutputOrientation(layoutId, definition),
            FormatAxB(layoutId, definition));
}
