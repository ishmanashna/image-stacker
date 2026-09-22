using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageStacker.App.Services;

internal sealed class AppSettings
{
    [JsonPropertyName("input_folder")]
    public string InputFolder { get; set; } = Environment.CurrentDirectory;

    [JsonPropertyName("output_folder")]
    public string OutputFolder { get; set; } = "output";

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = "stack-3";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "single";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("borderless")]
    public bool Borderless { get; set; }

    [JsonPropertyName("bleed")]
    public bool Bleed { get; set; }

    [JsonPropertyName("color")]
    public string Color { get; set; } = "white";
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        string path = AppPaths.SettingsFile;
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(path);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is null)
            {
                return new AppSettings();
            }

            if (string.Equals(settings.Layout, "grid-1x3-h", StringComparison.OrdinalIgnoreCase))
            {
                settings.Layout = "grid-1x3-v";
            }

            return settings;
        }
        catch (Exception ex)
        {
            FileLogger.Warning($"Could not load settings: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch (Exception ex)
        {
            FileLogger.Warning($"Could not save settings: {ex.Message}");
        }
    }
}
