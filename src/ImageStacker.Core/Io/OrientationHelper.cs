using ImageStacker.Core.Layout;
using NetVips;

namespace ImageStacker.Core.Io;

public static class OrientationHelper
{
    public static bool MatchesOrientation(string imagePath, LayoutOrientation requiredOrientation)
    {
        if (requiredOrientation == LayoutOrientation.Mixed)
        {
            return CanReadImage(imagePath);
        }

        if (!TryGetOrientedDimensions(imagePath, out int width, out int height))
        {
            return false;
        }

        return requiredOrientation switch
        {
            LayoutOrientation.Horizontal => width > height,
            LayoutOrientation.Vertical => height > width,
            _ => false,
        };
    }

    public static bool CanReadImage(string imagePath)
    {
        try
        {
            using var image = Image.NewFromFile(imagePath, access: Enums.Access.Sequential);
            return image.Width > 0 && image.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetOrientedDimensions(string imagePath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var image = Image.NewFromFile(imagePath, access: Enums.Access.Sequential);
            using var oriented = image.Autorot();
            width = oriented.Width;
            height = oriented.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetRawDimensions(string imagePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var image = Image.NewFromFile(imagePath, access: Enums.Access.Sequential);
            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetOrientationMismatchMessage(string imagePath, LayoutOrientation requiredOrientation)
    {
        if (requiredOrientation == LayoutOrientation.Mixed)
        {
            return CanReadImage(imagePath)
                ? null
                : $"Cannot read photo: {Path.GetFileName(imagePath)}.";
        }

        if (!TryGetOrientedDimensions(imagePath, out int width, out int height))
        {
            return $"Cannot read photo: {Path.GetFileName(imagePath)}.";
        }

        if (MatchesOrientation(imagePath, requiredOrientation))
        {
            return null;
        }

        string fileName = Path.GetFileName(imagePath);
        string photoOrientation = DescribePhotoShape(width, height);
        string requiredLabel = DescribeRequiredOrientation(requiredOrientation);
        return $"\"{fileName}\" is {photoOrientation}; this layout requires {requiredLabel} photos.";
    }

    private static string DescribePhotoShape(int width, int height)
    {
        if (width > height)
        {
            return "landscape (horizontal)";
        }

        if (height > width)
        {
            return "portrait (vertical)";
        }

        return "square";
    }

    private static string DescribeRequiredOrientation(LayoutOrientation requiredOrientation) =>
        requiredOrientation switch
        {
            LayoutOrientation.Horizontal => "landscape (horizontal)",
            LayoutOrientation.Vertical => "portrait (vertical)",
            _ => LayoutCatalog.OrientationKey(requiredOrientation),
        };
}
