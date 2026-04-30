using System.IO;
using System.Text.Json;

namespace Clauddy.Services;

public class Settings
{
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public bool RunAtLogin { get; set; }
    /// <summary>
    /// 5-hour token cap, back-solved from a /usage calibration. 0 = unset, raw counts shown.
    /// Non-zero: primary metrics line shows "5h: NN%" against this cap.
    /// </summary>
    public long Quota5hTokens { get; set; }
    /// <summary>
    /// 7-day token cap, back-solved from a /usage calibration. 0 = unset, raw counts shown.
    /// Non-zero: primary metrics line shows "Week: NN%" against this cap.
    /// </summary>
    public long Quota7dTokens { get; set; }
    /// <summary>UI scale factor. 1.0 = default size; clamped to [0.5, 3.0]. Persists across restarts.</summary>
    public double Scale { get; set; } = 1.0;
}

public class SettingsStore
{
    private readonly string _path;
    public SettingsStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Clauddy", "settings.json");

    public Settings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings();
        }
        catch { return new Settings(); }
    }

    public void Save(Settings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
