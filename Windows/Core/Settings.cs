using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaleidoWallpaper.Core;

/// <summary>Everything that can be set from the tray menu. Stored in %APPDATA%\KaleidoWallpaper\settings.json.</summary>
public sealed class Settings
{
    /// <summary>Process name to react to (e.g. "AppleMusic", "Spotify", "chrome"); null = all computer audio.</summary>
    public string? TargetProcess { get; set; }
    public string? TargetName { get; set; }

    public double Sensitivity { get; set; } = 1.2;
    public double Trails { get; set; } = 0.72;
    public double RingSpeed { get; set; } = 1.0;
    public bool ReverseRings { get; set; }
    public int MaxFps { get; set; } = 60;
    public double Scale { get; set; } = 0.6;
    public int Responsiveness { get; set; } = 1;          // 0 = smooth (like the browser), 1 = fast, 2 = instant
    public bool AlbumArt { get; set; } = true;
    public double AlbumVibrancy { get; set; } = 0.6;      // album colours: 0 = natural, 0.6 = vibrant, 1 = max
    public double BeatBounce { get; set; } = 1.0;         // visual kick on sudden hits: 0 = off, 0.4 = subtle, 1 = normal
    public bool BeatColorSwitch { get; set; }             // on a hit, jump to the next colour instead of bouncing
    public bool UseCustomColors { get; set; }             // false = album colours (rainbow without a cover)
    public List<string> CustomColors { get; set; } = new() { "#FF3FA4", "#FFB347", "#28C6E6", "#7A3CFF" };

    // response speed: window length, spectrum smoothing, how fast the visuals follow
    private int Level => Math.Clamp(Responsiveness, 0, 2);
    [JsonIgnore] public int FftSize => Level == 0 ? 4096 : 2048;
    [JsonIgnore] public double Smoothing => new[] { 0.65, 0.4, 0.2 }[Level];
    [JsonIgnore] public double Attack => new[] { 1.0, 2.2, 3.5 }[Level];

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KaleidoWallpaper", "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception) { /* damaged file: start with defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception) { /* settings are a convenience */ }
    }

    /// <summary>"#RRGGBB" → sRGB components 0…1 (white for anything unreadable).</summary>
    public static (double R, double G, double B) Rgb(string hex)
    {
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            return (1, 1, 1);
        return (((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>null (album colours) or the four custom colours as [[r,g,b], …].</summary>
    private string CustomJavaScript()
    {
        if (!UseCustomColors) return "null";
        var colors = CustomColors.Take(4).ToList();
        while (colors.Count < 4) colors.Add(colors.LastOrDefault() ?? "#FFFFFF");
        return "[" + string.Join(",", colors.Select(h => { var (r, g, b) = Rgb(h); return $"[{F(r)},{F(g)},{F(b)}]"; })) + "]";
    }

    /// <summary>Argument for window.psySettings(...). Always invariant culture: a Dutch locale would write 1,2.</summary>
    public string ToJavaScript()
    {
        var dirs = string.Join(",", Enumerable.Range(0, 10).Select(i => (i % 2 == 0 ? 1 : -1) * (ReverseRings ? -1 : 1)));
        return $"{{\"sens\":{F(Sensitivity)},\"trail\":{F(Trails)},\"ringSpeed\":{F(RingSpeed)}," +
               $"\"maxFps\":{MaxFps},\"scale\":{F(Scale)},\"react\":{F(Attack)},\"bounce\":{F(BeatBounce)}," +
               $"\"beatColor\":{(BeatColorSwitch ? "true" : "false")},\"vibrancy\":{F(AlbumVibrancy)}," +
               $"\"custom\":{CustomJavaScript()},\"dirs\":[{dirs}]}}";
    }
}
