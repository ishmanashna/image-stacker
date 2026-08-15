namespace ImageStacker.App.Services;

internal static class AppPaths
{
    public static string AppDataDir
    {
        get
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(baseDir, "ImageStacker");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(AppDataDir, "settings.json");

    public static string LogDir
    {
        get
        {
            string dir = Path.Combine(AppDataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogFile => Path.Combine(LogDir, "app.log");
}
