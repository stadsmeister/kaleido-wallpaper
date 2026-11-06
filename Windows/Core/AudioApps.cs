using System.Diagnostics;
using System.Runtime.InteropServices;
using static KaleidoWallpaper.Core.AudioNative;

namespace KaleidoWallpaper.Core;

/// <summary>Finds the apps that play (or have played) sound, for the "React to" menu.</summary>
public static class AudioApps
{
    public readonly record struct App(string ProcessName, string DisplayName);

    /// <summary>Apps with an audio session on the default output, sorted by name.</summary>
    public static List<App> List()
    {
        var result = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var device = DefaultRenderDevice();
            Guid iid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out object obj));
            var manager = (IAudioSessionManager2)obj;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0) continue;
                try
                {
                    if (session.IsSystemSoundsSession() == 0) continue;            // S_OK = Windows system sounds
                    if (session.GetProcessId(out uint pid) != 0 || pid == 0) continue;
                    using var process = Process.GetProcessById((int)pid);
                    var name = process.ProcessName;
                    if (!result.ContainsKey(name)) result[name] = new App(name, FriendlyName(process));
                }
                catch (Exception) { /* process already gone */ }
                finally { Marshal.ReleaseComObject(session); }
            }
            Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(manager);
            Marshal.ReleaseComObject(device);
        }
        catch (Exception) { /* no audio device */ }
        return result.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The top-level process of an app (e.g. the main chrome.exe, not a renderer): process loopback
    /// captures its whole tree, which includes the helper process that actually plays the sound.
    /// </summary>
    public static int? FindRootProcess(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            var withWindow = processes.FirstOrDefault(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } });
            if (withWindow != null) return withWindow.Id;
            var oldest = processes.OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } }).FirstOrDefault();
            return oldest?.Id;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public static bool IsAlive(int processId)
    {
        try { using var p = Process.GetProcessById(processId); return !p.HasExited; }
        catch (Exception) { return false; }
    }

    private static string FriendlyName(Process process)
    {
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (Exception) { /* packaged or elevated apps hide their module */ }
        return process.ProcessName;
    }
}
