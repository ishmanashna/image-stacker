using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Core.Io;

public static class ImageScanner
{
    public static IReadOnlyList<string> EnumerateImagePaths(string folderPath)
    {
        string fullFolder = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullFolder))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fullFolder, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(file);
            if (!Constants.ImageExtensions.Contains(extension))
            {
                continue;
            }

            paths.Add(Path.GetFullPath(file));
        }

        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    public static IReadOnlyList<string> GetValidPaths(string folderPath, LayoutOrientation requiredOrientation)
    {
        var valid = new List<string>();
        foreach (string file in EnumerateImagePaths(folderPath))
        {
            if (!OrientationHelper.MatchesOrientation(file, requiredOrientation))
            {
                continue;
            }

            valid.Add(file);
        }

        return valid;
    }

    public static IReadOnlyList<string> FilterByOrientation(
        IReadOnlyList<string> paths,
        LayoutOrientation requiredOrientation)
    {
        var valid = new List<string>();
        foreach (string path in paths)
        {
            if (OrientationHelper.MatchesOrientation(path, requiredOrientation))
            {
                valid.Add(path);
            }
        }

        return valid;
    }
}
