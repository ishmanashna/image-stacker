using System.Windows;
using ImageStacker.App.Services;
using ImageStacker.Core;

namespace ImageStacker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            FileLogger.Initialize();
            CoreDiagnostics.WarningHandler = FileLogger.Warning;
            FileLogger.Info("Image Stacker starting.");
        }
        catch (Exception ex)
        {
            // Logging must never block startup.
            System.Diagnostics.Debug.WriteLine($"Logger init failed: {ex}");
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try { FileLogger.Error("Unhandled exception", args.Exception); } catch { /* ignore */ }
            MessageBox.Show(
                $"An unexpected error occurred:\n{args.Exception.GetBaseException().Message}",
                "Image Stacker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
