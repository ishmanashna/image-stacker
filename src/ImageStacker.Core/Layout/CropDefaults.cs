namespace ImageStacker.Core.Layout;

public static class CropDefaults
{
    /// <summary>
    /// Slight upward bias so cover-crops on landscape cells keep the upper goldilocks
    /// (about two-thirds up the frame) instead of dead-center.
    /// </summary>
    public const double GoldilocksPanY = -1.0 / 3.0;

    public static double DefaultPanY(string layoutName)
    {
        LayoutDefinition def = LayoutCatalog.GetRequired(layoutName);
        return def.Orientation == LayoutOrientation.Horizontal ? GoldilocksPanY : 0.0;
    }

    public static double DefaultPanX(string layoutName, int slotIndex)
    {
        if (layoutName.Equals("grid-1x2-v", StringComparison.OrdinalIgnoreCase))
        {
            return slotIndex == 0 ? -1.0 : 1.0;
        }

        return 0.0;
    }
}
