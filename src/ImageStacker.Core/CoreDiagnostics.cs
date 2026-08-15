namespace ImageStacker.Core;

/// <summary>
/// Optional warning sink for Core (CLI stderr, App file log, tests).
/// </summary>
public static class CoreDiagnostics
{
    public static Action<string>? WarningHandler { get; set; }

    internal static void Warn(string message) => WarningHandler?.Invoke(message);
}
