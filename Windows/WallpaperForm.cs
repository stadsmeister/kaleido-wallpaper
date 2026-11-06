using KaleidoWallpaper.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KaleidoWallpaper;

/// <summary>
/// One wallpaper per screen: a borderless window behind the desktop icons with kaleido.html in WebView2.
/// It never paints white (black background, shown only once the page runs) and re-attaches itself when
/// Explorer rebuilds the desktop.
/// </summary>
internal sealed class WallpaperForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly CoreWebView2Environment _env;
    private readonly System.Windows.Forms.Timer _guard = new() { Interval = 5000 };
    private DesktopHost.Target _target;

    public Screen TargetScreen { get; private set; }
    public bool IsReady { get; private set; }
    public DesktopLayout Layout => _target.Layout;
    public event Action<WallpaperForm>? Ready;

    public WallpaperForm(Screen screen, CoreWebView2Environment env)
    {
        TargetScreen = screen;
        _env = env;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        Controls.Add(_web);
        _guard.Tick += (_, _) => KeepAttached();
    }

    // never in Alt+Tab, never takes focus
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Attach();
        try
        {
            await _web.EnsureCoreWebView2Async(_env);
        }
        catch (Exception)
        {
            return;                                                  // window is being closed
        }
        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.SetVirtualHostNameToFolderMapping("kaleido.local",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += (_, args) => OnPageMessage(args.TryGetWebMessageAsString());
        core.ProcessFailed += (_, _) => Reload();                     // renderer or GPU process crashed
        core.Navigate("https://kaleido.local/kaleido.html");
        _guard.Start();
    }

    private void OnPageMessage(string message)
    {
        if (message == "contextlost") { Reload(); return; }          // WebGL context gone: start over
        IsReady = true;
        Ready?.Invoke(this);
    }

    private void Reload()
    {
        IsReady = false;
        try { _web.CoreWebView2?.Reload(); } catch (Exception) { /* closing */ }
    }

    public void Run(string script)
    {
        if (!IsReady || _web.CoreWebView2 == null) return;
        _ = _web.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void MoveTo(Screen screen)
    {
        TargetScreen = screen;
        if (_target.Parent != IntPtr.Zero) DesktopHost.Place(Handle, _target, ToRect(screen.Bounds));
    }

    public void Attach() => _target = DesktopHost.Attach(Handle, ToRect(TargetScreen.Bounds));

    /// <summary>Explorer may recreate its windows (restart, theme change, 24H2 z-order shuffles).</summary>
    private void KeepAttached()
    {
        if (_target.Parent == IntPtr.Zero || !DesktopHost.IsIntact(Handle, _target)) Attach();
    }

    private static Native.RECT ToRect(Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _guard.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
