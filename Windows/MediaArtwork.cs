using System.Text.Json;
using Windows.Media.Control;

namespace KaleidoWallpaper;

/// <summary>
/// Follows what an app is playing through Windows' media controls (the same info as the volume flyout:
/// Apple Music, Spotify, browsers, …) and delivers the album cover as a JPEG data URL.
/// Falls back to Apple's public iTunes catalogue when a player does not provide a cover.
/// Events arrive on background threads; <see cref="ArtworkChanged"/> is raised from them.
/// </summary>
internal sealed class MediaArtwork
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public event Action<string?>? ArtworkChanged;
    public string? CurrentDataUrl { get; private set; }

    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _target;
    private string _lastKey = "";
    private bool _running;
    private int _token;

    /// <param name="targetProcess">Process name of the app to follow, or null for whatever Windows considers "now playing".</param>
    public async void Start(string? targetProcess)
    {
        lock (_gate) _target = targetProcess;
        if (!_running)
        {
            _running = true;
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.SessionsChanged += (_, _) => PickSession();
                _manager.CurrentSessionChanged += (_, _) => PickSession();
            }
            catch (Exception)
            {
                _running = false;
                return;
            }
        }
        lock (_gate) _lastKey = "";
        PickSession();
    }

    public void Stop()
    {
        _running = false;
        lock (_gate)
        {
            if (_session != null) _session.MediaPropertiesChanged -= OnPropertiesChanged;
            _session = null;
            _lastKey = "";
            _token++;
        }
        Publish(null);
    }

    private void PickSession()
    {
        if (!_running || _manager == null) return;
        GlobalSystemMediaTransportControlsSession? chosen;
        try
        {
            string? target;
            lock (_gate) target = _target;
            // Apple Music's id is like "AppleInc.AppleMusicWin_…!App", Spotify's "Spotify.exe", Chrome's "Chrome"
            chosen = target == null
                ? _manager.GetCurrentSession()
                : _manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.Contains(target, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception) { return; }

        lock (_gate)
        {
            if (chosen?.SourceAppUserModelId == _session?.SourceAppUserModelId && chosen != null && _session != null) return;
            if (_session != null) _session.MediaPropertiesChanged -= OnPropertiesChanged;
            _session = chosen;
            _lastKey = "";
            if (_session != null) _session.MediaPropertiesChanged += OnPropertiesChanged;
        }
        if (chosen == null) Publish(null); else _ = RefreshAsync();
    }

    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _session;
        if (session == null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;
            string album = string.IsNullOrEmpty(props.AlbumTitle) ? props.Title : props.AlbumTitle;
            string key = props.Artist + "\u001f" + album;
            int token;
            lock (_gate)
            {
                if (key == _lastKey) return;                            // same album: keep cover and colours
                _lastKey = key;
                token = ++_token;
            }

            string? url = null;
            if (props.Thumbnail != null)
            {
                using var stream = await props.Thumbnail.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var buffer = new MemoryStream();
                await input.CopyToAsync(buffer);
                url = ImageUtil.ToJpegDataUrl(buffer.ToArray());
            }
            if (url == null && !string.IsNullOrWhiteSpace(props.Artist))
                url = await LookupCoverAsync(props.Artist, album);

            lock (_gate) if (token != _token) return;
            Publish(url);
        }
        catch (Exception) { /* player went away mid-update */ }
    }

    private static async Task<string?> LookupCoverAsync(string artist, string album)
    {
        try
        {
            var term = Uri.EscapeDataString($"{artist} {album}");
            var json = await Http.GetStringAsync($"https://itunes.apple.com/search?term={term}&entity=album&limit=1");
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return null;
            var small = results[0].GetProperty("artworkUrl100").GetString();
            if (small == null) return null;
            var bytes = await Http.GetByteArrayAsync(small.Replace("100x100bb", "600x600bb"));
            return ImageUtil.ToJpegDataUrl(bytes);
        }
        catch (Exception) { return null; }
    }

    private void Publish(string? url)
    {
        lock (_gate)
        {
            if (url == CurrentDataUrl) return;
            CurrentDataUrl = url;
        }
        ArtworkChanged?.Invoke(url);
    }

    /// <summary>JavaScript call for kaleido.html (a data URL contains only base64 characters, so quoting is safe).</summary>
    public static string Script(string? dataUrl) => dataUrl == null ? "psyArt(null)" : $"psyArt('{dataUrl}')";
}
