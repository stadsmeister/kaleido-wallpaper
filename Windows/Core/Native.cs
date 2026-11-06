using System.Runtime.InteropServices;
using System.Text;

namespace KaleidoWallpaper.Core;

/// <summary>Win32 functions used to place a window behind the desktop icons. 64-bit Windows only.</summary>
public static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_POPUP = 0x80000000,
                      WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
    public const long WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000,
                      WS_EX_NOREDIRECTIONBITMAP = 0x00200000, WS_EX_APPWINDOW = 0x00040000;
    public const uint LWA_ALPHA = 0x2;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const uint GW_HWNDNEXT = 2;
    public const uint SMTO_NORMAL = 0x0;
    public const uint SPI_GETDESKWALLPAPER = 0x0073, SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x1, SPIF_SENDCHANGE = 0x2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                   uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT points, int count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint action, uint param, StringBuilder? pvParam, uint winIni);
}
