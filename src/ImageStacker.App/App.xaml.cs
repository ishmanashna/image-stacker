using System.Windows;
using ImageStacker.App.Services;
using ImageStacker.Core;

namespace ImageStacker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        FileLogger.Initialize();
        CoreDiagnostics.WarningHandler = FileLogger.Warning;
        FileLogger.Info("Image Stacker starting.");
        DispatcherUnhandledException += (_, args) =>
        {
            FileLogger.Error("Unhandled exception", args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred:\n{args.Exception.Message}",
                "Image Stacker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
