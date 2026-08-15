namespace ImageStacker.Core.Color;

public static class ColorParser
{
    private static readonly Dictionary<string, (byte R, byte G, byte B)> NamedColors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = (255, 255, 255),
            ["black"] = (0, 0, 0),
            ["beige"] = (245, 245, 220),
            ["ivory"] = (255, 255, 240),
            ["gray"] = (128, 128, 128),
            ["grey"] = (128, 128, 128),
            ["lightgray"] = (211, 211, 211),
            ["lightgrey"] = (211, 211, 211),
            ["darkgray"] = (169, 169, 169),
            ["darkgrey"] = (169, 169, 169),
            ["wheat"] = (245, 222, 179),
            ["tan"] = (210, 180, 140),
            ["navy"] = (0, 0, 128),
            ["maroon"] = (128, 0, 0),
            ["red"] = (255, 0, 0),
            ["green"] = (0, 128, 0),
            ["blue"] = (0, 0, 255),
        };

    public static (byte R, byte G, byte B) Parse(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return (255, 255, 255);
        }

        string s = color.Trim();
        if (s.StartsWith('#'))
        {
            string hex = s[1..];
            if (hex.Length == 6 && IsHex(hex))
            {
                return (
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
            }

            return (255, 255, 255);
        }

        if (NamedColors.TryGetValue(s, out var rgb))
        {
            return rgb;
        }

        return (255, 255, 255);
    }

    public static (byte R, byte G, byte B) Parse(object color)
    {
        if (color is string s)
        {
            return Parse(s);
        }

        if (color is ValueTuple<byte, byte, byte> tuple)
        {
            return tuple;
        }

        return (255, 255, 255);
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
