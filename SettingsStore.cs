using System.IO;
using System.Text.Json;

namespace MindMapCanvas;

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool RememberLastStyle { get; set; } = true;
    public string LastColor { get; set; } = "#FFF9B1";
    public string LastShape { get; set; } = "Rect";
    public List<string> CustomColors { get; set; } = new();
    public string CustomPanel { get; set; } = "#2B3442";
    public string CustomCanvas { get; set; } = "#26303C";
    public string CustomAccent { get; set; } = "#7FA3E0";
    public string LastConnColor { get; set; }
}

public static class SettingsStore
{
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MindMapCanvas");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }
}
