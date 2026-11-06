import Foundation

/// Everything the user can set from the menu bar. Stored in UserDefaults as JSON.
struct Settings: Codable, Equatable {
    /// Bundle identifier of the app to react to. `nil` = all computer audio.
    var targetBundleID: String? = nil
    var targetName: String? = nil

    var sensitivity: Double = 1.2      // same scale as the "Gevoeligheid" slider in the visualizer
    var trails: Double = 0.72          // feedback amount
    var ringSpeed: Double = 1.0
    var reverseRings = false
    var maxFPS = 60
    var scale = 0.6                    // render resolution relative to the screen (power vs. sharpness)
    var responsiveness = 1             // 0 = smooth (like the browser), 1 = fast, 2 = instant
    var albumArt = true                // album art mode when reacting to Apple Music
    var beatBounce = 1.0               // visual kick on sudden hits: 0 = off, 0.4 = subtle, 1 = normal
    var beatColorSwitch = false        // on a hit, jump to the next colour instead of bouncing
    var albumVibrancy = 0.6            // album colours: 0 = natural, 0.6 = vibrant, 1 = max
    var useCustomColors = false        // false = album colours (rainbow without a cover)
    var customColors = ["#FF3FA4", "#FFB347", "#28C6E6", "#7A3CFF"]   // four sRGB hex colours

    init() {}

    /// Tolerant decoding: settings saved by an older version (with fewer keys) still load.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Settings()
        targetBundleID = try c.decodeIfPresent(String.self, forKey: .targetBundleID) ?? d.targetBundleID
        targetName = try c.decodeIfPresent(String.self, forKey: .targetName) ?? d.targetName
        sensitivity = try c.decodeIfPresent(Double.self, forKey: .sensitivity) ?? d.sensitivity
        trails = try c.decodeIfPresent(Double.self, forKey: .trails) ?? d.trails
        ringSpeed = try c.decodeIfPresent(Double.self, forKey: .ringSpeed) ?? d.ringSpeed
        reverseRings = try c.decodeIfPresent(Bool.self, forKey: .reverseRings) ?? d.reverseRings
        maxFPS = try c.decodeIfPresent(Int.self, forKey: .maxFPS) ?? d.maxFPS
        scale = try c.decodeIfPresent(Double.self, forKey: .scale) ?? d.scale
        responsiveness = try c.decodeIfPresent(Int.self, forKey: .responsiveness) ?? d.responsiveness
        albumArt = try c.decodeIfPresent(Bool.self, forKey: .albumArt) ?? d.albumArt
        beatBounce = try c.decodeIfPresent(Double.self, forKey: .beatBounce) ?? d.beatBounce
        beatColorSwitch = try c.decodeIfPresent(Bool.self, forKey: .beatColorSwitch) ?? d.beatColorSwitch
        albumVibrancy = try c.decodeIfPresent(Double.self, forKey: .albumVibrancy) ?? d.albumVibrancy
        useCustomColors = try c.decodeIfPresent(Bool.self, forKey: .useCustomColors) ?? d.useCustomColors
        customColors = try c.decodeIfPresent([String].self, forKey: .customColors) ?? d.customColors
    }

    // MARK: response speed
    // Window length, spectrum smoothing and how fast the visuals follow the music.
    // Smooth matches the browser visualizer exactly.

    private var level: Int { min(max(responsiveness, 0), 2) }
    var fftSize: Int { level == 0 ? 4096 : 2048 }
    var smoothing: Float { let v: [Float] = [0.65, 0.4, 0.2]; return v[level] }
    var attack: Double { let v: [Double] = [1.0, 2.2, 3.5]; return v[level] }

    private static let key = "settings.v1"

    static func load() -> Settings {
        guard let data = UserDefaults.standard.data(forKey: key),
              let s = try? JSONDecoder().decode(Settings.self, from: data) else { return Settings() }
        return s
    }

    func save() {
        if let data = try? JSONEncoder().encode(self) {
            UserDefaults.standard.set(data, forKey: Settings.key)
        }
    }

    /// "#RRGGBB" → sRGB components 0…1 (white for anything unreadable).
    static func rgb(fromHex hex: String) -> (Double, Double, Double) {
        var text = hex.trimmingCharacters(in: .whitespaces)
        if text.hasPrefix("#") { text.removeFirst() }
        guard text.count == 6, let v = UInt32(text, radix: 16) else { return (1, 1, 1) }
        return (Double((v >> 16) & 0xFF) / 255, Double((v >> 8) & 0xFF) / 255, Double(v & 0xFF) / 255)
    }

    /// `null` (album colours) or the four custom colours as [[r,g,b], …].
    private var customJavaScript: String {
        guard useCustomColors else { return "null" }
        var colors = Array(customColors.prefix(4))
        while colors.count < 4 { colors.append(colors.last ?? "#FFFFFF") }
        let parts = colors.map { hex -> String in
            let (r, g, b) = Settings.rgb(fromHex: hex)
            return String(format: "[%.3f,%.3f,%.3f]", r, g, b)
        }
        return "[" + parts.joined(separator: ",") + "]"
    }

    /// Argument for `window.psySettings(...)` in kaleido.html.
    var javaScriptObject: String {
        let dirs = (0..<10).map { i in (i % 2 == 0 ? 1 : -1) * (reverseRings ? -1 : 1) }
        return "{\"sens\":\(sensitivity),\"trail\":\(trails),\"ringSpeed\":\(ringSpeed)," +
               "\"maxFps\":\(maxFPS),\"scale\":\(scale),\"react\":\(attack),\"bounce\":\(beatBounce),\"beatColor\":\(beatColorSwitch),\"vibrancy\":\(albumVibrancy),\"custom\":\(customJavaScript),\"dirs\":\(dirs)}"
    }
}
