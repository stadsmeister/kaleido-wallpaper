namespace KaleidoWallpaper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // one instance per user session
        using var single = new Mutex(true, "Local\\KaleidoWallpaper", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}
