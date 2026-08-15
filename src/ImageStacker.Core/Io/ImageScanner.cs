using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Core.Io;

public static class ImageScanner
{
    public static IReadOnlyList<string> GetValidPaths(string folderPath, LayoutOrientation requiredOrientation)
    {
        string fullFolder = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolder))
        {
            return Array.Empty<string>();
        }

        var valid = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fullFolder))
        {
            string extension = Path.GetExtension(file);
            if (!Constants.ImageExtensions.Contains(extension))
            {
                continue;
            }

            if (!OrientationHelper.MatchesOrientation(file, requiredOrientation))
            {
                continue;
            }

            valid.Add(Path.GetFullPath(file));
        }

        valid.Sort(StringComparer.Ordinal);
        return valid;
    }
}
