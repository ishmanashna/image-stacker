namespace ImageStacker.Core;

public sealed record SlotAssignment(
    string Path,
    double PanX = 0.0,
    double PanY = 0.0,
    bool FlipH = false,
    bool Grayscale = false);
