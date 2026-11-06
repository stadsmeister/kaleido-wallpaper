import AppKit

/// Follows what Apple Music ("Music") or Spotify is playing and delivers the album cover as a
/// JPEG data URL for the wallpaper. Both apps announce track changes with a distributed
/// notification and answer AppleScript queries:
/// - Music hands over the embedded cover itself;
/// - Spotify hands over a link to its 640 px cover image.
/// When neither works (e.g. a streamed track without a cover), Apple's public iTunes catalogue is searched.
final class NowPlayingArtwork {
    private struct Player {
        let appName: String               // AppleScript "tell application" name
        let notification: String          // posted on every play / pause / track change
        let script: String                // returns {album, artist, cover data or cover URL}
        let coverIsURL: Bool
    }

    private static let players: [String: Player] = [
        "com.apple.Music": Player(
            appName: "Music",
            notification: "com.apple.Music.playerInfo",
            script: """
            tell application "Music"
                if player state is stopped then return {"", "", ""}
                set t to current track
                set al to album of t
                set ar to artist of t
                try
                    set d to raw data of artwork 1 of t
                on error
                    set d to ""
                end try
                return {al, ar, d}
            end tell
            """,
            coverIsURL: false),
        "com.spotify.client": Player(
            appName: "Spotify",
            notification: "com.spotify.client.PlaybackStateChanged",
            script: """
            tell application "Spotify"
                if player state is stopped then return {"", "", ""}
                set t to current track
                return {album of t, artist of t, artwork url of t}
            end tell
            """,
            coverIsURL: true)
    ]

    static func supports(_ bundleID: String?) -> Bool { bundleID.map { players[$0] != nil } ?? false }

    /// Called on the main thread with a `data:image/jpeg;base64,…` URL, or nil when nothing plays.
    var onArtwork: ((String?) -> Void)?
    private(set) var currentDataURL: String?
    private var bundleID: String?
    private var player: Player?
    private var lastAlbumKey = ""
    private var lookupToken = 0

    /// Starts following the given app (switching over if another app was followed before).
    func start(bundleID id: String) {
        guard let p = NowPlayingArtwork.players[id] else { stop(); return }
        if id == bundleID { return }
        stop()
        bundleID = id
        player = p
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(playerChanged(_:)), name: NSNotification.Name(p.notification), object: nil)
        refresh()
    }

    func stop() {
        guard bundleID != nil else { return }
        DistributedNotificationCenter.default().removeObserver(self)
        bundleID = nil
        player = nil
        lastAlbumKey = ""
        lookupToken += 1
        publish(nil)
    }

    @objc private func playerChanged(_ note: Notification) {
        if (note.userInfo?["Player State"] as? String) == "Stopped" {
            lastAlbumKey = ""
            lookupToken += 1
            publish(nil)
            return
        }
        refresh()
    }

    /// Asks the player for the current track. Never launches the player: only runs while it is open.
    private func refresh() {
        guard let id = bundleID, let p = player,
              NSRunningApplication.runningApplications(withBundleIdentifier: id).first != nil else {
            publish(nil)
            return
        }
        var error: NSDictionary?
        guard let result = NSAppleScript(source: p.script)?.executeAndReturnError(&error), error == nil,
              result.numberOfItems >= 3 else {
            return                                      // usually: no Automation permission yet (asked once)
        }
        let album = result.atIndex(1)?.stringValue ?? ""
        let artist = result.atIndex(2)?.stringValue ?? ""
        if album.isEmpty && artist.isEmpty { publish(nil); return }

        let key = artist + "\u{1F}" + album
        guard key != lastAlbumKey else { return }       // same album: keep cover and colours
        lastAlbumKey = key
        lookupToken += 1
        let token = lookupToken

        if p.coverIsURL {
            let link = (result.atIndex(3)?.stringValue ?? "").replacingOccurrences(of: "http://", with: "https://")
            if let url = URL(string: link), !link.isEmpty {
                download(url, token: token) { [weak self] dataURL in
                    if let dataURL { self?.publish(dataURL) }
                    else { self?.searchCover(artist: artist, album: album, token: token) }
                }
                return
            }
        } else if let data = result.atIndex(3)?.data, data.count > 100,
                  let dataURL = NowPlayingArtwork.jpegDataURL(from: data) {
            publish(dataURL)
            return
        }
        searchCover(artist: artist, album: album, token: token)
    }

    /// Downloads an image and hands back a data URL on the main thread (nil on failure or when outdated).
    private func download(_ url: URL, token: Int, completion: @escaping (String?) -> Void) {
        URLSession.shared.dataTask(with: url) { [weak self] data, _, _ in
            let dataURL = data.flatMap { NowPlayingArtwork.jpegDataURL(from: $0) }
            DispatchQueue.main.async {
                guard self?.lookupToken == token else { return }
                completion(dataURL)
            }
        }.resume()
    }

    /// Fallback: look the album up in Apple's public catalogue.
    private func searchCover(artist: String, album: String, token: Int) {
        var parts = URLComponents(string: "https://itunes.apple.com/search")!
        parts.queryItems = [
            URLQueryItem(name: "term", value: "\(artist) \(album)"),
            URLQueryItem(name: "entity", value: "album"),
            URLQueryItem(name: "limit", value: "1")
        ]
        guard let searchURL = parts.url else { return }
        URLSession.shared.dataTask(with: searchURL) { [weak self] data, _, _ in
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let first = (json["results"] as? [[String: Any]])?.first,
                  let small = first["artworkUrl100"] as? String,
                  let artURL = URL(string: small.replacingOccurrences(of: "100x100bb", with: "600x600bb")) else {
                DispatchQueue.main.async { if self?.lookupToken == token { self?.publish(nil) } }
                return
            }
            DispatchQueue.main.async {
                self?.download(artURL, token: token) { dataURL in self?.publish(dataURL) }
            }
        }.resume()
    }

    private func publish(_ url: String?) {
        guard url != currentDataURL else { return }
        currentDataURL = url
        onArtwork?(url)
    }

    /// JavaScript call for kaleido.html (a data URL only contains base64 characters, so quoting is safe).
    static func script(for dataURL: String?) -> String {
        guard let dataURL else { return "psyArt(null)" }
        return "psyArt('\(dataURL)')"
    }

    /// Scales any image to 512×512 (centre-cropped to a square) and returns it as a JPEG data URL.
    static func jpegDataURL(from data: Data) -> String? {
        guard let image = NSImage(data: data), image.size.width > 0, image.size.height > 0 else { return nil }
        let size = 512
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        let side = min(image.size.width, image.size.height)
        let crop = NSRect(x: (image.size.width - side) / 2, y: (image.size.height - side) / 2, width: side, height: side)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        NSGraphicsContext.current?.imageInterpolation = .high
        image.draw(in: NSRect(x: 0, y: 0, width: size, height: size), from: crop, operation: .copy, fraction: 1)
        NSGraphicsContext.restoreGraphicsState()
        guard let jpeg = rep.representation(using: .jpeg, properties: [.compressionFactor: 0.85]) else { return nil }
        return "data:image/jpeg;base64," + jpeg.base64EncodedString()
    }
}
