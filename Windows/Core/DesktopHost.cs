using System.Text;
using static KaleidoWallpaper.Core.Native;

namespace KaleidoWallpaper.Core;

/// <summary>How Explorer has built the desktop.</summary>
public enum DesktopLayout
{
    /// <summary>Windows 10 / 11 up to 23H2: two top-level WorkerW windows, the second one draws the wallpaper.</summary>
    Classic,
    /// <summary>Windows 11 24H2+: icons (SHELLDLL_DefView) and the wallpaper WorkerW are children of Progman.</summary>
    Raised,
    /// <summary>Explorer did not split the desktop: the window ends up in front of the icons.</summary>
    Fallback,
    None
}

/// <summary>Puts a window behind the desktop icons, above the normal wallpaper.</summary>
public static class DesktopHost
{
    public readonly record struct Target(IntPtr Parent, IntPtr DefView, DesktopLayout Layout);

    /// <summary>Finds where the wallpaper window must go, asking Explorer to split the desktop when needed.</summary>
    public static Target Locate()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return new Target(IntPtr.Zero, IntPtr.Zero, DesktopLayout.None);

        var target = Classify(progman);
        if (target.Layout != DesktopLayout.Fallback) return target;

        // undocumented: makes Explorer create the separate wallpaper layer (sent only when it is missing)
        SendMessageTimeout(progman, 0x052C, (IntPtr)0xD, (IntPtr)0x1, SMTO_NORMAL, 1000, out _);
        for (int i = 0; i < 20 && target.Layout == DesktopLayout.Fallback; i++)
        {
            Thread.Sleep(100);
            target = Classify(progman);
        }
        return target;
    }

    private static Target Classify(IntPtr progman)
    {
        // Windows 11 24H2+ ("raised desktop"): DefView is a child of Progman and Progman draws without a
        // redirection bitmap. The wallpaper must be a layered child of Progman, right below DefView.
        IntPtr defInProgman = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        long progmanEx = (long)GetWindowLongPtr(progman, GWL_EXSTYLE);
        if (defInProgman != IntPtr.Zero && (progmanEx & WS_EX_NOREDIRECTIONBITMAP) != 0)
            return new Target(progman, defInProgman, DesktopLayout.Raised);

        // classic: the top-level WorkerW after the one that holds DefView is the wallpaper layer
        IntPtr wallpaperWorker = IntPtr.Zero, defView = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            IntPtr dv = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv == IntPtr.Zero) return true;
            IntPtr worker = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            if (worker == IntPtr.Zero) return true;
            wallpaperWorker = worker;
            defView = dv;
            return false;
        }, IntPtr.Zero);
        if (wallpaperWorker != IntPtr.Zero) return new Target(wallpaperWorker, defView, DesktopLayout.Classic);

        return new Target(progman, defInProgman, DesktopLayout.Fallback);
    }

    /// <summary>Makes <paramref name="hwnd"/> a child of the desktop and covers <paramref name="screenBounds"/> (physical pixels).</summary>
    public static Target Attach(IntPtr hwnd, RECT screenBounds)
    {
        var target = Locate();
        if (target.Parent == IntPtr.Zero) return target;

        long style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)) | WS_CHILD | WS_VISIBLE;
        SetWindowLongPtr(hwnd, GWL_STYLE, (IntPtr)style);

        long ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & ~WS_EX_APPWINDOW;
        if (target.Layout == DesktopLayout.Raised)
        {
            // only an opaque layered child is composited in the raised desktop; set it before SetParent
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_LAYERED));
            SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        }
        else
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
        }

        SetParent(hwnd, target.Parent);
        Place(hwnd, target, screenBounds);
        return target;
    }

    /// <summary>Sets position/size (and, on the raised desktop, the z-order right below the icons).</summary>
    public static void Place(IntPtr hwnd, Target target, RECT screenBounds)
    {
        var r = screenBounds;
        MapWindowPoints(IntPtr.Zero, target.Parent, ref r, 2);           // screen → parent client coordinates
        bool raised = target.Layout == DesktopLayout.Raised && target.DefView != IntPtr.Zero;
        uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW | (raised ? 0 : SWP_NOZORDER);
        SetWindowPos(hwnd, raised ? target.DefView : IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags);
    }

    /// <summary>True when the window is still where it belongs (Explorer can rebuild its windows).</summary>
    public static bool IsIntact(IntPtr hwnd, Target target)
    {
        if (!IsWindow(target.Parent) || GetParent(hwnd) != target.Parent) return false;
        if (target.Layout == DesktopLayout.Raised && target.DefView != IntPtr.Zero)
            return GetWindow(target.DefView, GW_HWNDNEXT) == hwnd;               // directly below the icons
        return true;
    }

    /// <summary>Lets Explorer redraw the normal wallpaper (after pausing or quitting).</summary>
    public static void RefreshWallpaper()
    {
        var path = new StringBuilder(1024);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)path.Capacity, path, 0) && path.Length > 0)
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
