using KaleidoWallpaper.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace KaleidoWallpaper;

/// <summary>
/// Tray app: one wallpaper window per screen, audio of the chosen app, settings in the tray menu.
/// Everything here runs on the UI thread; background events are posted back with <see cref="_ui"/>.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "KaleidoWallpaper";

    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly AudioCapture _capture;
    private readonly MediaArtwork _artwork = new();
    private readonly ColorEditorForm _colorEditor = new();
    private readonly List<WallpaperForm> _forms = new();
    private readonly System.Windows.Forms.Timer _feed = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 3000 };
    private readonly ExplorerWatcher _explorer;
    private Settings _settings = Settings.Load();
    private CoreWebView2Environment? _env;
    private int? _capturedPid;
    private bool _paused;
    private string _status = "Starting…";

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _capture = new AudioCapture(_analyzer);
        _capture.Status += text => _ui.Post(_ => _status = text, null);
        _artwork.ArtworkChanged += url => _ui.Post(_ =>
        {
            if (!ArtworkWanted) return;
            var script = MediaArtwork.Script(url);
            foreach (var f in _forms) f.Run(script);
        }, null);

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Kaleido Wallpaper",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _menu.Opening += (_, _) => BuildMenu();

        _feed.Tick += (_, _) => Feed();
        _watch.Tick += (_, _) => Watch();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _explorer = new ExplorerWatcher(() => _forms.ForEach(f => f.Attach()));   // Explorer restarted

        _colorEditor.ColorsChanged += colors => Update(s => { s.CustomColors = colors; s.UseCustomColors = true; });
        _analyzer.Configure(_settings.FftSize, _settings.Smoothing);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "KaleidoWallpaper", "WebView2");
            _env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
        }
        catch (Exception)
        {
            MessageBox.Show("The Microsoft Edge WebView2 Runtime is missing. Install it from " +
                            "https://developer.microsoft.com/microsoft-edge/webview2/ and start Kaleido Wallpaper again.",
                            "Kaleido Wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ExitThread();
            return;
        }
        SyncForms();
        RestartCapture();
        UpdateArtwork();
        _feed.Start();
        _watch.Start();
    }

    // ---------- wallpaper windows ----------

    /// <summary>Exactly one window per screen; existing ones are only moved (a new web view needs time to start).</summary>
    private void SyncForms()
    {
        if (_env == null) return;
        if (_paused)
        {
            foreach (var f in _forms) f.Close();
            _forms.Clear();
            DesktopHost.RefreshWallpaper();
            return;
        }
        var screens = Screen.AllScreens;
        foreach (var f in _forms.Where(f => screens.All(s => s.DeviceName != f.TargetScreen.DeviceName)).ToList())
        {
            f.Close();
            _forms.Remove(f);
        }
        foreach (var screen in screens)
        {
            var existing = _forms.FirstOrDefault(f => f.TargetScreen.DeviceName == screen.DeviceName);
            if (existing != null) { existing.MoveTo(screen); continue; }
            var form = new WallpaperForm(screen, _env);
            form.Ready += OnFormReady;
            _forms.Add(form);
            form.Show();
        }
        if (_forms.Any(f => f.Layout == DesktopLayout.Fallback))
            _status = "Note: Windows did not split the desktop; the wallpaper may cover the icons";
    }

    private void OnFormReady(WallpaperForm form)
    {
        form.Run($"psyInit({{sampleRate:{AudioCapture.SampleRate},fftSize:{_settings.FftSize}}})");
        form.Run($"psySettings({_settings.ToJavaScript()})");
        if (ArtworkWanted) form.Run(MediaArtwork.Script(_artwork.CurrentDataUrl));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _ui.Post(_ => SyncForms(), null);

    // ---------- audio → web views ----------

    private void Feed()
    {
        if (_paused || _forms.Count == 0 || !_analyzer.HasRecentInput) return;
        var script = $"psyFeed('{Convert.ToBase64String(_analyzer.ByteSpectrum())}')";
        foreach (var f in _forms) f.Run(script);
    }

    private void RestartCapture()
    {
        _capture.Stop();
        _capturedPid = null;
        if (_paused) { _status = "Paused"; return; }
        if (_settings.TargetProcess == null)
        {
            _capture.Start(null, "all computer audio");
            return;
        }
        var pid = AudioApps.FindRootProcess(_settings.TargetProcess);
        if (pid == null)
        {
            _status = $"Waiting for {_settings.TargetName ?? _settings.TargetProcess} to start";
            return;
        }
        _capturedPid = pid;
        _capture.Start(pid, _settings.TargetName ?? _settings.TargetProcess);
    }

    /// <summary>Picks the chosen app up when it starts, and again after it was closed and reopened.</summary>
    private void Watch()
    {
        if (_paused) return;
        if (_settings.TargetProcess == null)
        {
            if (!_capture.IsRunning) RestartCapture();
            return;
        }
        if (_capturedPid is int pid && AudioApps.IsAlive(pid) && _capture.IsRunning) return;
        RestartCapture();
    }

    // ---------- album art ----------

    private bool ArtworkWanted => _settings.AlbumArt && !_paused;

    private void UpdateArtwork()
    {
        if (ArtworkWanted) _artwork.Start(_settings.TargetProcess);
        else
        {
            _artwork.Stop();
            foreach (var f in _forms) f.Run(MediaArtwork.Script(null));
        }
    }

    // ---------- settings ----------

    private void Update(Action<Settings> change, bool recapture = false)
    {
        int oldFft = _settings.FftSize;
        change(_settings);
        _settings.Save();
        _analyzer.Configure(_settings.FftSize, _settings.Smoothing);
        foreach (var f in _forms)
        {
            if (_settings.FftSize != oldFft)
                f.Run($"psyInit({{sampleRate:{AudioCapture.SampleRate},fftSize:{_settings.FftSize}}})");
            f.Run($"psySettings({_settings.ToJavaScript()})");
        }
        if (recapture) RestartCapture();
        UpdateArtwork();
    }

    // ---------- tray menu ----------

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem(_status) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(TargetMenu());
        _menu.Items.Add(Toggle("Album art mode", _settings.AlbumArt, () => Update(s => s.AlbumArt = !s.AlbumArt),
            "Cover in the centre and the cover's colors. Works with apps that report 'now playing' to Windows, such as Apple Music and Spotify."));
        var albumColors = Choice("Album colors", new[] { ("Natural", 0.0), ("Vibrant", 0.6), ("Max", 1.0) },
            _settings.AlbumVibrancy, v => Update(s => s.AlbumVibrancy = v));
        albumColors.Enabled = _settings.AlbumArt && !_settings.UseCustomColors;
        _menu.Items.Add(albumColors);
        _menu.Items.Add(ColorsMenu());
        _menu.Items.Add(Choice("Response", new[] { ("Smooth (like the browser)", 0.0), ("Fast", 1.0), ("Instant", 2.0) },
            _settings.Responsiveness, v => Update(s => s.Responsiveness = (int)v)));
        _menu.Items.Add(BeatEffectMenu());
        _menu.Items.Add(Choice("Sensitivity", new[] { ("Low", 0.8), ("Normal", 1.2), ("High", 1.8), ("Extreme", 2.5) },
            _settings.Sensitivity, v => Update(s => s.Sensitivity = v)));
        _menu.Items.Add(Choice("Trails", new[] { ("Short", 0.4), ("Normal", 0.72), ("Long", 0.88) },
            _settings.Trails, v => Update(s => s.Trails = v)));
        _menu.Items.Add(Choice("Ring speed", new[] { ("Calm", 0.5), ("Normal", 1.0), ("Fast", 2.0) },
            _settings.RingSpeed, v => Update(s => s.RingSpeed = v)));
        _menu.Items.Add(Toggle("Reverse ring rotation", _settings.ReverseRings, () => Update(s => s.ReverseRings = !s.ReverseRings)));
        _menu.Items.Add(Choice("Quality", new[] { ("Battery saver", 0.4), ("Normal", 0.6), ("Sharp", 1.0) },
            _settings.Scale, v => Update(s => s.Scale = v)));
        _menu.Items.Add(Choice("Frame rate", new[] { ("30 fps", 30.0), ("60 fps", 60.0) },
            _settings.MaxFps, v => Update(s => s.MaxFps = (int)v)));
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(Toggle("Pause", _paused, TogglePause));
        _menu.Items.Add(Toggle("Launch at sign-in", LaunchAtLogin, () => LaunchAtLogin = !LaunchAtLogin));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Quit Kaleido Wallpaper", null, (_, _) => Quit()));
    }

    /// <summary>"React to ▸": all audio, or one of the apps that have played sound.</summary>
    private ToolStripMenuItem TargetMenu()
    {
        var root = new ToolStripMenuItem("React to");
        root.DropDownItems.Add(Toggle("All computer audio", _settings.TargetProcess == null,
            () => Update(s => { s.TargetProcess = null; s.TargetName = null; }, recapture: true)));
        root.DropDownItems.Add(new ToolStripSeparator());

        var apps = AudioApps.List().Where(a => !a.ProcessName.Equals("KaleidoWallpaper", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var app in apps)
        {
            bool chosen = string.Equals(_settings.TargetProcess, app.ProcessName, StringComparison.OrdinalIgnoreCase);
            root.DropDownItems.Add(Toggle(app.DisplayName, chosen,
                () => Update(s => { s.TargetProcess = app.ProcessName; s.TargetName = app.DisplayName; }, recapture: true)));
        }
        if (_settings.TargetProcess != null &&
            !apps.Any(a => a.ProcessName.Equals(_settings.TargetProcess, StringComparison.OrdinalIgnoreCase)))
        {
            root.DropDownItems.Add(new ToolStripMenuItem($"{_settings.TargetName ?? _settings.TargetProcess} (not running)") { Checked = true });
        }
        if (apps.Count == 0)
            root.DropDownItems.Add(new ToolStripMenuItem("Play something to see your app here") { Enabled = false });
        return root;
    }

    /// <summary>Ready-made custom palettes (same as the Mac version).</summary>
    private static readonly (string Name, string[] Colors)[] Presets =
    {
        ("Sunset", new[] { "#FF5E3A", "#FF2A68", "#FFB13B", "#8E2DE2" }),
        ("Ocean", new[] { "#00C6FF", "#0072FF", "#00F5D4", "#3A0CA3" }),
        ("Neon", new[] { "#FF00E5", "#00FFF0", "#ADFF00", "#7B00FF" }),
        ("Forest", new[] { "#2ECC71", "#A8E063", "#16A085", "#F4D03F" }),
        ("Ice", new[] { "#E0F7FF", "#7FDBFF", "#5B8CFF", "#B388FF" })
    };

    /// <summary>"Colors ▸": album colours by default, or the user's own four colours.</summary>
    private ToolStripMenuItem ColorsMenu()
    {
        var root = new ToolStripMenuItem("Colors");
        root.DropDownItems.Add(Toggle("From album (default)", !_settings.UseCustomColors, () => Update(s => s.UseCustomColors = false),
            "The cover's colors in album art mode, the rainbow otherwise."));
        var custom = Toggle("Custom", _settings.UseCustomColors, () => Update(s => s.UseCustomColors = true));
        custom.Image = ColorEditorForm.Swatch(_settings.CustomColors);
        root.DropDownItems.Add(custom);
        root.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (name, colors) in Presets)
        {
            bool on = _settings.UseCustomColors && _settings.CustomColors.SequenceEqual(colors, StringComparer.OrdinalIgnoreCase);
            var preset = Toggle(name, on, () => Update(s => { s.CustomColors = colors.ToList(); s.UseCustomColors = true; }));
            preset.Image = ColorEditorForm.Swatch(colors);
            root.DropDownItems.Add(preset);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(new ToolStripMenuItem("Edit custom colors…", null, (_, _) => _colorEditor.ShowWith(_settings.CustomColors)));
        return root;
    }

    /// <summary>"Beat effect ▸": what happens on a sudden hit. Color switch replaces the bounce.</summary>
    private ToolStripMenuItem BeatEffectMenu()
    {
        var root = new ToolStripMenuItem("Beat effect");
        var s0 = _settings;
        void Add(string text, bool on, double bounce, bool colorSwitch) =>
            root.DropDownItems.Add(Toggle(text, on, () => Update(s => { s.BeatBounce = bounce; s.BeatColorSwitch = colorSwitch; })));
        Add("Off", !s0.BeatColorSwitch && s0.BeatBounce < 0.01, 0, false);
        Add("Subtle bounce", !s0.BeatColorSwitch && Math.Abs(s0.BeatBounce - 0.4) < 0.01, 0.4, false);
        Add("Bounce", !s0.BeatColorSwitch && Math.Abs(s0.BeatBounce - 1.0) < 0.01, 1.0, false);
        Add("Color switch", s0.BeatColorSwitch, s0.BeatBounce, true);
        return root;
    }

    private static ToolStripMenuItem Toggle(string text, bool on, Action action, string? tooltip = null) =>
        new(text, null, (_, _) => action()) { Checked = on, ToolTipText = tooltip };

    private static ToolStripMenuItem Choice(string title, (string Name, double Value)[] options, double current, Action<double> set)
    {
        var root = new ToolStripMenuItem(title);
        foreach (var (name, value) in options)
            root.DropDownItems.Add(Toggle(name, Math.Abs(current - value) < 0.001, () => set(value)));
        return root;
    }

    private void TogglePause()
    {
        _paused = !_paused;
        SyncForms();
        RestartCapture();
        UpdateArtwork();
    }

    private static bool LaunchAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, false);
        }
    }

    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        try { if (File.Exists(path)) return new Icon(path, SystemInformation.SmallIconSize); }
        catch (Exception) { /* fall through */ }
        return SystemIcons.Application;
    }

    private void Quit()
    {
        _feed.Stop();
        _watch.Stop();
        _capture.Stop();
        _artwork.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var f in _forms) f.Close();
        _forms.Clear();
        DesktopHost.RefreshWallpaper();
        _explorer.Dispose();
        _colorEditor.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}

/// <summary>Hidden window that hears "TaskbarCreated", which Windows broadcasts when Explorer (re)starts.</summary>
internal sealed class ExplorerWatcher : NativeWindow, IDisposable
{
    private readonly uint _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly Action _onRestart;

    public ExplorerWatcher(Action onRestart)
    {
        _onRestart = onRestart;
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)_taskbarCreated) _onRestart();
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
